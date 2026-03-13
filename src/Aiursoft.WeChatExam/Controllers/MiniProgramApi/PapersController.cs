using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using System.Security.Claims;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// API for exam system to access paper snapshots
/// </summary>
[Route("api/[controller]")]
[WeChatUserOnly]
[ApiController]
public class PapersController : ControllerBase
{
    private readonly IPaperService _paperService;
    private readonly WeChatExamDbContext _context;
    private readonly IWeChatPayService _payService;

    public PapersController(IPaperService paperService, WeChatExamDbContext context, IWeChatPayService payService)
    {
        _paperService = paperService;
        _context = context;
        _payService = payService;
    }

    /// <summary>
    /// 获取所有可用试卷列表（仅返回有快照的试卷）
    /// </summary>
    /// <param name="categoryId">可选：分类ID</param>
    /// <param name="tag">可选：标签</param>
    /// <param name="isRealExam">可选：是否为真题</param>
    /// <returns>试卷列表</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<PaperListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPapers(
        [FromQuery] Guid? categoryId, 
        [FromQuery] string? tag, 
        [FromQuery] bool? isRealExam)
    {
        var query = _context.Papers
            .Where(p => p.Status == PaperStatus.Publishable || p.Status == PaperStatus.Frozen)
            .Include(p => p.PaperSnapshots)
            .Include(p => p.PaperTags)
            .ThenInclude(pt => pt.Tag)
            .Where(p => p.PaperSnapshots.Any());

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.PaperCategories.Any(pc => pc.CategoryId == categoryId.Value));
        }

        if (isRealExam.HasValue)
        {
            query = query.Where(p => p.IsRealExam == isRealExam.Value);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(p => p.PaperTags.Any(pt => pt.Tag.DisplayName == tag));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var dtos = new List<PaperListDto>();
        var papers = await query.ToListAsync();
        foreach (var p in papers)
        {
            var dto = new PaperListDto
            {
                Id = p.Id,
                Title = p.Title,
                TimeLimit = p.TimeLimit,
                IsFree = p.IsFree,
                IsRealExam = p.IsRealExam,
                Status = p.Status.ToString(),
                Tags = p.PaperTags.Select(pt => pt.Tag.DisplayName).ToList(),
                LatestSnapshotId = p.PaperSnapshots.OrderByDescending(s => s.Version).First().Id,
                LatestVersion = p.PaperSnapshots.Max(s => s.Version)
            };

            if (p.IsFree)
            {
                dto.HasAccess = true;
            }
            else
            {
                // Has access if user has active VIP for ANY of the paper's categories
                var categoryIds = p.PaperCategories.Select(pc => pc.CategoryId).ToList();
                dto.HasAccess = false;
                foreach (var catId in categoryIds)
                {
                    if (userId != null && await _payService.HasVipForCategoryAsync(userId, catId))
                    {
                        dto.HasAccess = true;
                        break;
                    }
                }
            }
            dtos.Add(dto);
        }

        return Ok(dtos);
    }

    /// <summary>
    /// 获取试卷快照详情（用于考试）
    /// </summary>
    /// <param name="snapshotId">快照ID</param>
    /// <returns>快照详情</returns>
    [HttpGet("snapshots/{snapshotId}")]
    [ProducesResponseType(typeof(PaperSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(Guid snapshotId)
    {
        var snapshot = await _paperService.GetSnapshotAsync(snapshotId);
        if (snapshot == null)
        {
            return NotFound(new { Message = "Snapshot not found" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool hasAccess = snapshot.IsFree;

        if (!hasAccess)
        {
            var paper = await _context.Papers.Include(p => p.PaperCategories)
                .FirstOrDefaultAsync(p => p.Id == snapshot.PaperId);
                
            if (paper != null)
            {
                var categoryIds = paper.PaperCategories.Select(pc => pc.CategoryId).ToList();
                foreach (var catId in categoryIds)
                {
                    if (userId != null && await _payService.HasVipForCategoryAsync(userId, catId))
                    {
                        hasAccess = true;
                        break;
                    }
                }
            }
        }

        var dto = new PaperSnapshotDto
        {
            Id = snapshot.Id,
            PaperId = snapshot.PaperId,
            Version = snapshot.Version,
            Title = snapshot.Title,
            TimeLimit = snapshot.TimeLimit,
            IsFree = snapshot.IsFree,
            HasAccess = hasAccess,
            Questions = hasAccess ? snapshot.QuestionSnapshots.OrderBy(q => q.Order).Select(q => new QuestionSnapshotDto
            {
                Id = q.Id,
                Order = q.Order,
                Score = q.Score,
                Content = q.Content,
                QuestionType = q.QuestionType,
                Metadata = q.Metadata
            }).ToList() : new List<QuestionSnapshotDto>()
        };

        return Ok(dto);
    }

    /// <summary>
    /// 获取试卷的所有快照版本
    /// </summary>
    /// <param name="paperId">试卷ID</param>
    /// <returns>快照列表</returns>
    [HttpGet("{paperId}/snapshots")]
    [ProducesResponseType(typeof(List<SnapshotListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshotsForPaper(Guid paperId)
    {
        var snapshots = await _paperService.GetSnapshotsForPaperAsync(paperId);
        var dtos = snapshots.Select(s => new SnapshotListDto
        {
            Id = s.Id,
            Version = s.Version,
            CreationTime = s.CreationTime
        }).ToList();

        return Ok(dtos);
    }
}
