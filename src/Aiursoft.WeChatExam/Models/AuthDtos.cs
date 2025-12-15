using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models;

public class Code2SessionDto
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public class TokenDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
    public string OpenId { get; set; } = string.Empty;
}
