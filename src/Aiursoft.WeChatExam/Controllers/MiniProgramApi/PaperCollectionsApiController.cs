using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/PaperCollections")]
[ApiController]
[WeChatUserOnly]
public class PaperCollectionsApiController(WeChatExamDbContext db) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Index([FromQuery] bool? isRealExam)
    {
        var available = PaperCollectionService.Available(db.Papers);
        if (isRealExam.HasValue) available = available.Where(p => p.IsRealExam == isRealExam.Value);
        var availableIds = available.Select(p => p.Id);
        var result = await db.PaperCollections.AsNoTracking().Where(c => c.IsVisible && !c.IsArchived)
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.OrderIndex, AvailablePaperCount = c.Papers.Count(p => availableIds.Contains(p.PaperId)) }).ToListAsync();
        return Ok(result);
    }

    [HttpGet("{id:guid}/papers"), AllowAnonymous]
    public async Task<IActionResult> Papers(Guid id, [FromQuery] bool? isRealExam, [FromServices] IPaperAccessService access)
    {
        if (!await db.PaperCollections.AnyAsync(c => c.Id == id && c.IsVisible && !c.IsArchived)) return NotFound();
        var members = db.PaperCollectionPapers.Where(p => p.CollectionId == id).Select(p => p.PaperId);
        var query = PaperCollectionService.Available(db.Papers).Where(p => members.Contains(p.Id));
        if (isRealExam.HasValue) query = query.Where(p => p.IsRealExam == isRealExam.Value);
        var papers = await query.Include(p => p.PaperSnapshots).Include(p => p.PaperCategories)
            .Include(p => p.PaperTags).ThenInclude(t => t.Tag)
            .OrderByDescending(p => p.IsFree).ThenBy(p => p.Title).ThenBy(p => p.Id).ToListAsync();
        var status = await access.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(papers.Select(p => new PaperListDto
        {
            Id = p.Id, Title = p.Title, TimeLimit = p.TimeLimit, IsFree = p.IsFree, IsRealExam = p.IsRealExam,
            Status = p.Status.ToString(), Tags = p.PaperTags.Select(t => t.Tag.DisplayName).ToList(),
            LatestSnapshotId = p.PaperSnapshots.OrderByDescending(s => s.Version).First().Id,
            LatestVersion = p.PaperSnapshots.Max(s => s.Version), HasAccess = access.HasAccess(p, status)
        }).ToList());
    }
}
