using Aiursoft.WeChatExam.MTQL.Tokens;

namespace Aiursoft.WeChatExam.MTQL.Services;

public static class Tokenizer
{
    public static List<Token> Tokenize(string mtql)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < mtql.Length)
        {
            var charC = mtql[i];

            if (char.IsWhiteSpace(charC))
            {
                i++;
                continue;
            }

            switch (charC)
            {
                case '(':
                    tokens.Add(new Token(TokenType.LParen, "("));
                    i++;
                    break;
                case ')':
                    tokens.Add(new Token(TokenType.RParen, ")"));
                    i++;
                    break;
                case '&':
                    if (i + 1 < mtql.Length && mtql[i + 1] == '&')
                    {
                        tokens.Add(new Token(TokenType.And, "&&"));
                        i += 2;
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid character '&' at index {i}. Did you mean '&&'?");
                    }
                    break;
                case '|':
                    if (i + 1 < mtql.Length && mtql[i + 1] == '|')
                    {
                        tokens.Add(new Token(TokenType.Or, "||"));
                        i += 2;
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid character '|' at index {i}. Did you mean '||'?");
                    }
                    break;
                default:
                {
                    // Read word
                    var start = i;
                    while (i < mtql.Length)
                    {
                        var c = mtql[i];
                        if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '&' || c == '|')
                        {
                            break;
                        }
                        i++;
                    }
                    var value = mtql.Substring(start, i - start);
                    if (string.Equals(value, "not", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Not, "not"));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Tag, value));
                    }
                    break;
                }
            }
        }
        return tokens;
    }
}
