namespace Aiursoft.WeChatExam.MTQL.Tokens;

public enum TokenType
{
    Tag,
    And,
    Or,
    Not,
    LParen,
    RParen
}

public record Token(TokenType Type, string Value);
