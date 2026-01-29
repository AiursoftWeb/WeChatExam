using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class TaxonomyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TagDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}
