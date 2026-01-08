using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class ExamService : IExamService
{
    private readonly TemplateDbContext _dbContext;
    private readonly IGradingService _gradingService;

    public ExamService(TemplateDbContext dbContext, IGradingService gradingService)
    {
        _dbContext = dbContext;
        _gradingService = gradingService;
    }

    #region Admin & Management

    public async Task<Exam> CreateExamAsync(string title, Guid paperId, DateTime startTime, DateTime endTime, int? durationMinutes = null)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");

        // Find the latest snapshot to lock this exam to a specific version
        var latestSnapshot = await _dbContext.PaperSnapshots
            .Where(s => s.PaperId == paperId)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync();

        if (latestSnapshot == null) throw new InvalidOperationException("Cannot create exam: The selected paper has no published snapshots. Please publish the paper first.");

        var exam = new Exam
        {
            Id = Guid.NewGuid(),
            Title = title,
            PaperId = paperId,
            PaperSnapshotId = latestSnapshot.Id, // Lock to this version
            StartTime = startTime,
            EndTime = endTime,
            DurationMinutes = durationMinutes ?? paper.TimeLimit,
            IsPublic = true,
            AllowedAttempts = 1
        };

        _dbContext.Exams.Add(exam);
        await _dbContext.SaveChangesAsync();
        return exam;
    }

    public async Task UpdateExamAsync(Guid examId, string title, DateTime startTime, DateTime endTime, int durationMinutes, bool isPublic, int allowedAttempts, bool allowReview, DateTime? showAnswerAfter)
    {
        var exam = await _dbContext.Exams.FindAsync(examId);
        if (exam == null) throw new InvalidOperationException("Exam not found");

        exam.Title = title;
        exam.StartTime = startTime;
        exam.EndTime = endTime;
        exam.DurationMinutes = durationMinutes;
        exam.IsPublic = isPublic;
        exam.AllowedAttempts = allowedAttempts;
        exam.AllowReview = allowReview;
        exam.ShowAnswerAfter = showAnswerAfter;

        _dbContext.Exams.Update(exam);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteExamAsync(Guid examId)
    {
        var exam = await _dbContext.Exams.FindAsync(examId);
        if (exam == null) return;

        // Note: In a real system you might want to soft-delete or check for existing records first.
        // For strict cleanup, we'll cascade delete records if EF is configured, usually manual needed:
        var records = await _dbContext.ExamRecords.Where(r => r.ExamId == examId).ToListAsync();
        _dbContext.ExamRecords.RemoveRange(records); // Remove records first

        _dbContext.Exams.Remove(exam);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Exam?> GetExamAsync(Guid examId)
    {
        return await _dbContext.Exams
            .Include(e => e.Paper)
            .FirstOrDefaultAsync(e => e.Id == examId);
    }

    public async Task<List<Exam>> GetAllExamsAsync()
    {
        return await _dbContext.Exams
            .Include(e => e.Paper)
            .OrderByDescending(e => e.CreationTime)
            .ToListAsync();
    }

    #endregion

    #region Student Methods

    public async Task<List<Exam>> GetAvailableExamsForUserAsync(string userId)
    {
        var now = DateTime.UtcNow;
        // Logic: Exam is public, and current time is between start and end (or upcoming)
        // We return upcoming and active exams.
        return await _dbContext.Exams
            .Include(e => e.Paper)
            .Where(e => e.IsPublic && e.EndTime > now)
            .OrderBy(e => e.StartTime)
            .ToListAsync();
    }

    public async Task<List<ExamRecord>> GetExamRecordsForUserAsync(string userId)
    {
        return await _dbContext.ExamRecords
            .Include(r => r.Exam)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.StartTime)
            .ToListAsync();
    }

    public async Task<ExamRecord> StartExamAsync(Guid examId, string userId)
    {
        var now = DateTime.UtcNow;
        var exam = await _dbContext.Exams.FirstOrDefaultAsync(e => e.Id == examId);
        if (exam == null) throw new InvalidOperationException("Exam not found");

        // 1. Time Window Check
        if (now < exam.StartTime) throw new InvalidOperationException("Exam has not started yet.");
        if (now > exam.EndTime) throw new InvalidOperationException("Exam has ended.");

        // 2. Attempt Limit Check
        var existingRecords = await _dbContext.ExamRecords
            .Where(r => r.ExamId == examId && r.UserId == userId)
            .ToListAsync();

        // Check if there is an in-progress record to resume
        var inProgressRecord = existingRecords.FirstOrDefault(r => r.Status == ExamRecordStatus.InProgress);
        if (inProgressRecord != null)
        {
            // Resume existing session
            return inProgressRecord;
        }

        if (existingRecords.Count >= exam.AllowedAttempts)
        {
            throw new InvalidOperationException("Maximum attempts reached.");
        }

        // 3. Create New Record
        // Lock to the latest Paper Snapshot OR the specifically locked snapshot for this exam
        Guid targetSnapshotId;
        
        if (exam.PaperSnapshotId.HasValue)
        {
            targetSnapshotId = exam.PaperSnapshotId.Value;
            // Verify it exists (optional but safe)
            var exists = await _dbContext.PaperSnapshots.AnyAsync(s => s.Id == targetSnapshotId);
            if (!exists) throw new InvalidOperationException("Configured paper snapshot not found.");
        }
        else
        {
            // We find the latest snapshot for the paper
            var latestSnapshot = await _dbContext.PaperSnapshots
                .Where(s => s.PaperId == exam.PaperId)
                .OrderByDescending(s => s.Version)
                .FirstOrDefaultAsync();
                
            if (latestSnapshot == null) throw new InvalidOperationException("No published version of this paper exists.");
            targetSnapshotId = latestSnapshot.Id;
        }

        var newRecord = new ExamRecord
        {
            Id = Guid.NewGuid(),
            ExamId = examId,
            UserId = userId,
            PaperSnapshotId = targetSnapshotId,
            AttemptIndex = existingRecords.Count + 1,
            StartTime = now,
            Status = ExamRecordStatus.InProgress
        };

        _dbContext.ExamRecords.Add(newRecord);
        await _dbContext.SaveChangesAsync();
        return newRecord;
    }

    public async Task SubmitAnswerAsync(Guid examRecordId, Guid questionSnapshotId, string answer)
    {
        var record = await _dbContext.ExamRecords
            .Include(r => r.Exam)
            .FirstOrDefaultAsync(r => r.Id == examRecordId);

        if (record == null) throw new InvalidOperationException("Record not found");
        EnsureExamNotExpired(record);
        
        var exists = await _dbContext.QuestionSnapshots
            .AnyAsync(q => q.Id == questionSnapshotId && q.PaperSnapshotId == record.PaperSnapshotId);

        if (!exists) throw new InvalidOperationException("Question snapshot does not belong to this exam.");


        var ansRecord = await _dbContext.AnswerRecords
            .FirstOrDefaultAsync(a => a.ExamRecordId == examRecordId && a.QuestionSnapshotId == questionSnapshotId);

        if (ansRecord == null)
        {
            ansRecord = new AnswerRecord
            {
                Id = Guid.NewGuid(),
                ExamRecordId = examRecordId,
                QuestionSnapshotId = questionSnapshotId,
                UserAnswer = answer,
                IsMarked = false
            };
            _dbContext.AnswerRecords.Add(ansRecord);
        }
        else
        {
            ansRecord.UserAnswer = answer;
            _dbContext.AnswerRecords.Update(ansRecord);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<ExamRecord> FinishExamAsync(Guid examRecordId)
    {
        var record = await _dbContext.ExamRecords
            .Include(r => r.Exam)
            .Include(r => r.PaperSnapshot)
            .Include(r => r.AnswerRecords)
            .FirstOrDefaultAsync(r => r.Id == examRecordId);

        if (record == null) throw new InvalidOperationException("Record not found");
        
        // If already submitted, just return it (idempotent), but if time expired but not submitted, we allow finishing (since it calculates score)
        // actually, FinishExam is called TO submit. If time expired, we should allow them to "finish" but only if they are just doing it right now?
        // Wait, if time expired, they CANNOT submit new answers, but they SHOULD be able to "Hand in" the paper to see result?
        // Actually the requirement is "prevent students from submitting answers ... after the allowed duration".
        // If they click "Finish", it just calculates score. It deals with existing answers. So technically FinishExam check might be less strict?
        // But the user asked: "FinishExamAsync (交卷) ... completely lacks validation".
        // So we should strictly valid current time. If they are late, they might have to rely on a background job to auto-submit, or we allow them to submit but stamp it?
        // User said: "Requests arriving after ... will be rejected".
        // So let's reject.
        EnsureExamNotExpired(record);

        record.SubmitTime = DateTime.UtcNow;
        
        // Auto Grading Logic
        var totalScore = 0;
        
        // Fetch full question snapshots for grading
        var questionSnapshots = await _dbContext.QuestionSnapshots
            .Where(qs => qs.PaperSnapshotId == record.PaperSnapshotId)
            .ToListAsync();

        foreach (var qSnap in questionSnapshots)
        {
            var ans = record.AnswerRecords.FirstOrDefault(a => a.QuestionSnapshotId == qSnap.Id);
            var userAnswer = ans?.UserAnswer ?? "";

            var result = await _gradingService.GradeAsync(userAnswer, qSnap.StandardAnswer, qSnap.GradingStrategy, qSnap.Score, qSnap.Content);
            
            // If answer record didn't exist (unanswered), create one to store score 0
            if (ans == null)
            {
                ans = new AnswerRecord
                {
                    Id = Guid.NewGuid(),
                    ExamRecordId = examRecordId,
                    QuestionSnapshotId = qSnap.Id,
                    UserAnswer = "",
                    Score = result.Score,
                    IsMarked = true,
                    GradingResult = System.Text.Json.JsonSerializer.Serialize(result)
                };
                _dbContext.AnswerRecords.Add(ans);
            }
            else
            {
                ans.Score = result.Score;
                ans.IsMarked = true;
                ans.GradingResult = System.Text.Json.JsonSerializer.Serialize(result);
                _dbContext.AnswerRecords.Update(ans);
            }

            totalScore += result.Score;
        }

        record.TotalScore = totalScore;
        record.Status = ExamRecordStatus.Submitted;
        
        _dbContext.ExamRecords.Update(record);
        await _dbContext.SaveChangesAsync();

        return record;
    }

    public async Task<ExamRecord?> GetExamRecordAsync(Guid recordId)
    {
        return await _dbContext.ExamRecords
            .Include(r => r.Exam)
            .Include(r => r.User)
            .Include(r => r.AnswerRecords)
            .ThenInclude(ar => ar.QuestionSnapshot)
            .FirstOrDefaultAsync(r => r.Id == recordId);
    }
    
    #endregion

    #region Grading

    public async Task UpdateScoreAsync(Guid examRecordId, int newScore, string comment)
    {
        var record = await _dbContext.ExamRecords.FindAsync(examRecordId);
        if (record == null) throw new InvalidOperationException("Record not found");

        record.TotalScore = newScore;
        record.TeacherComment = comment;

        _dbContext.ExamRecords.Update(record);
        await _dbContext.SaveChangesAsync();
    }

    #endregion


    private void EnsureExamNotExpired(ExamRecord record)
    {
        if (record.Status != ExamRecordStatus.InProgress) 
        {
             // If already submitted, we shouldn't be modifying it or submitting again.
             // For FinishExam, purely idempotent return is handled by caller logic if needed, but here we strictly check status.
             // But simpler: just throw if not InProgress.
             throw new InvalidOperationException("Exam is already submitted.");
        }

        if (record.Exam == null)
        {
             // Should verify Exam is included
             throw new InvalidOperationException("Exam data is missing.");
        }

        var now = DateTime.UtcNow;
        var examEndTime = record.Exam.EndTime;
        var sessionEndTime = record.StartTime.AddMinutes(record.Exam.DurationMinutes);

        // Take the earlier of the two end times
        var effectiveEndTime = examEndTime < sessionEndTime ? examEndTime : sessionEndTime;

        // Add 1 minute grace period
        var deadline = effectiveEndTime.AddMinutes(1);

        if (now > deadline)
        {
            throw new InvalidOperationException("Exam time has expired.");
        }
    }
}
