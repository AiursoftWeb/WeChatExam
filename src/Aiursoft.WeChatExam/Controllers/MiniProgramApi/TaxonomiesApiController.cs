using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

using Aiursoft.WeChatExam.Services.Authentication;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/miniprogramapi/taxonomies")]
[ApiController]
[WeChatUserOnly]
public class TaxonomiesApiController(
    ITaxonomyService taxonomyService,
    ITagService tagService,
    IPaperAccessService paperAccessService) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(List<TaxonomyDto>))]
    public async Task<IActionResult> Index([FromQuery] Guid? categoryId)
    {
        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(categoryId, includeCategory: true);
        var dtos = taxonomies.Select(t => new TaxonomyDto
        {
            Id = t.Id,
            Name = t.Name
        });
        return Ok(dtos);
    }

    [HttpGet("{id}/tags")]
    [Produces(typeof(List<TagDto>))]
    public async Task<IActionResult> GetTags(int id)
    {
        var tags = await tagService.GetTagsByTaxonomyIdAsync(id);
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessStatus = await paperAccessService.GetUserAccessStatusAsync(userId);

        var dtos = tags.Select(t =>
        {
            bool hasAccess = t.IsFree;
            if (!hasAccess && t.Taxonomy?.CategoryTaxonomies != null)
            {
                var categoryIds = t.Taxonomy.CategoryTaxonomies.Select(ct => ct.CategoryId).ToList();
                hasAccess = categoryIds.Any(catId => accessStatus.ActiveCategoryVips.Contains(catId));
            }

            return new TagDto
            {
                Id = t.Id,
                DisplayName = t.DisplayName,
                NormalizedName = t.NormalizedName,
                IsFree = t.IsFree,
                HasAccess = hasAccess
            };
        }).ToList();
        
        return Ok(dtos);
    }
}
