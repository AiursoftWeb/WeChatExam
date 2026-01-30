using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/miniprogramapi/taxonomies")]
[ApiController]
[AllowAnonymous] 
public class TaxonomiesApiController(
    ITaxonomyService taxonomyService,
    ITagService tagService) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(List<TaxonomyDto>))]
    public async Task<IActionResult> Index()
    {
        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync();
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

        var dtos = tags.Select(t => new TagDto
        {
            Id = t.Id,
            DisplayName = t.DisplayName,
            NormalizedName = t.NormalizedName
        });
        return Ok(dtos);
    }
}
