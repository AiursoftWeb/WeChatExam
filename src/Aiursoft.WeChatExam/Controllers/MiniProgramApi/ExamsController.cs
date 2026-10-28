using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
public class ExamsController : ControllerBase
{
    private readonly IExamService _examService;

    public ExamsController(IExamService examService)
    {
        _examService = examService;
    }

    private string? GetUserId()
    {
        return User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// 获取当前用户可参加的所有考试
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<ExamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExams()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var exams = await _examService.GetAvailableExamsForUserAsync(userId);
        
        // Also fetch user's attempts to show status
        var records = await _examService.GetExamRecordsForUserAsync(userId);

        var dtos = exams.Select(e => new ExamDto
        {
            Id = e.Id,
            Title = e.Title,
            PaperTitle = e.PaperSnapshot?.Title ?? e.Paper?.Title ?? "",
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            DurationMinutes = e.DurationMinutes,
            AllowedAttempts = e.AllowedAttempts,
            MyAttemptCount = records.Count(r => r.ExamId == e.Id),
            Status = GetStudentStatus(e, records.FirstOrDefault(r => r.ExamId == e.Id && r.Status == ExamRecordStatus.InProgress))
        }).ToList();

        return Ok(dtos);
    }
    
    /// <summary>
    /// 获取我的考试记录历史
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(List<ExamRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var records = await _examService.GetExamRecordsForUserAsync(userId);
        
        var dtos = records.Select(r => new ExamRecordDto
        {
            Id = r.Id,
            ExamTitle = r.Exam.Title,
            StartTime = r.StartTime,
            SubmitTime = r.SubmitTime,
            Status = r.Status.ToString(),
            TotalScore = r.TotalScore,
            HasReview = CanReview(r)
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// 开始或恢复考试
    /// </summary>
    [HttpPost("{examId}/start")]
    [ProducesResponseType(typeof(ExamSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartExam(Guid examId)
    {
        try
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var record = await _examService.StartExamAsync(examId, userId);
            
            // Re-fetch to get details if needed, or mapping immediately 
            // We need to return question content which comes from Snapshot
            record = await _examService.GetExamRecordAsync(record.Id) ?? record;

            // Load Snapshot if not loaded

            // The client should obtains the complete test paper snapshot from PaperController based on the SnapshotId

            return Ok(new ExamSessionDto
            {
                RecordId = record.Id,
                PaperSnapshotId = record.PaperSnapshotId,
                StartTime = record.StartTime,
                EndTime = record.StartTime.AddMinutes(record.Exam.DurationMinutes), // Theoretical end for this session
                Answers = record.AnswerRecords.ToDictionary(a => a.QuestionSnapshotId, a => a.UserAnswer)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// 提交单个答案（实时保存进度）
    /// </summary>
    [HttpPost("record/{recordId}/answer")]
    public async Task<IActionResult> SubmitAnswer(Guid recordId, [FromBody] SubmitAnswerDto model)
    {
        try
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _examService.SubmitAnswerAsync(recordId, userId, model.QuestionSnapshotId, model.Answer);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// 交卷
    /// </summary>
    [HttpPost("record/{recordId}/submit")]
    public async Task<IActionResult> FinishExam(Guid recordId)
    {
        try
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _examService.FinishExamAsync(recordId, userId);
            return Ok(new { Score = result.TotalScore, Status = result.Status.ToString() });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// 获取考试记录详情（回顾/查分）
    /// </summary>
    [HttpGet("record/{recordId}")]
    public async Task<IActionResult> GetRecordDetails(Guid recordId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var record = await _examService.GetExamRecordAsync(recordId);
        
        if (record == null) return NotFound();
        if (record.UserId != userId) return Forbid();
        
        // Check if review is allowed
        if (!CanReview(record))
        {
            // Only return basic info
            return Ok(new ExamRecordDto
            {
                Id = record.Id,
                ExamTitle = record.Exam.Title,
                StartTime = record.StartTime,
                SubmitTime = record.SubmitTime,
                TotalScore = record.TotalScore,
                Status = record.Status.ToString(),
                HasReview = false
            });
        }

        // Return full details including questions and correct answers (if allowed)
        var showAnswers = record.Exam.ShowAnswerAfter.HasValue && DateTime.UtcNow > record.Exam.ShowAnswerAfter.Value;

        var details = new ExamRecordDetailDto
        {
            Id = record.Id,
            Score = record.TotalScore,
            // Teacher feedback and grading explanations may contain the correct answer too.
            TeacherComment = showAnswers ? record.TeacherComment : string.Empty,
            Answers = record.AnswerRecords.Select(a => new AnswerResultDto
            {
                QuestionSnapshotId = a.QuestionSnapshotId,
                UserAnswer = a.UserAnswer,
                Score = a.Score,
                // Only show standard answer if configured
                StandardAnswer = showAnswers ? a.QuestionSnapshot.StandardAnswer : null,
                GradingResult = showAnswers ? a.GradingResult : string.Empty
            }).ToList()
        };
        
        return Ok(details);
    }

    // An answer-release date never permits reviewing an attempt still in progress.
    private static bool CanReview(ExamRecord record) =>
        record.Exam.AllowReview && record.Status != ExamRecordStatus.InProgress;

    private string GetStudentStatus(Exam exam, ExamRecord? activeRecord)
    {
        if (activeRecord != null) return "InProgress";
        var now = DateTime.UtcNow;
        if (now < exam.StartTime) return "NotStarted";
        if (now > exam.EndTime) return "Ended";
        return "Available";
    }
}

