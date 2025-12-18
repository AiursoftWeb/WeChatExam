using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

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

public class UpdateProfileDto
{
    [Required]
    public string NickName { get; set; } = string.Empty;
    [Required]
    public string AvatarUrl { get; set; } = string.Empty;
}

public class AdminLoginDto
{
    [Required]
    [MinLength(3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
}

public class DebugTokenRequestDto
{
    [Required]
    public string MagicKey { get; set; } = string.Empty;
}
