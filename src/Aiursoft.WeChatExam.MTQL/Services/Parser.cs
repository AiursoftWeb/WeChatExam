using Aiursoft.WeChatExam.MTQL.Tokens;

namespace Aiursoft.WeChatExam.MTQL.Services;

public static class Parser
{
    public static List<Token> ToRpn(List<Token> tokens)
    {
        var output = new List<Token>();
        var stack = new Stack<Token>();

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.Tag:
                    output.Add(token);
                    break;
                case TokenType.And:
                case TokenType.Or:
                case TokenType.Not:
                    HandleOperator(token, output, stack);
                    break;
                case TokenType.LParen:
                    stack.Push(token);
                    break;
                case TokenType.RParen:
                    while (stack.Count > 0 && stack.Peek().Type != TokenType.LParen)
                    {
                        output.Add(stack.Pop());
                    }

                    if (stack.Count == 0 || stack.Peek().Type != TokenType.LParen)
                    {
                        throw new ArgumentException("Mismatched parentheses.");
                    }
                    stack.Pop(); // Pop LParen
                    break;
            }
        }

        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top.Type == TokenType.LParen)
            {
                throw new ArgumentException("Mismatched parentheses.");
            }
            output.Add(top);
        }

        if (output.Count == 0 && tokens.Count > 0)
        {
             // Could happen if tokens were just paranthesis ()
        }

        return output;
    }

    private static void HandleOperator(Token op, List<Token> output, Stack<Token> stack)
    {
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (top.Type == TokenType.LParen) break;

            var opPrec = GetPrecedence(op.Type);
            var topPrec = GetPrecedence(top.Type);

            if (op.Type == TokenType.Not) // Right associative
            {
                if (topPrec > opPrec)
                {
                    output.Add(stack.Pop());
                }
                else
                {
                    break;
                }
            }
            else // Left associative
            {
                if (topPrec >= opPrec)
                {
                    output.Add(stack.Pop());
                }
                else
                {
                    break;
                }
            }
        }
        stack.Push(op);
    }

    private static int GetPrecedence(TokenType type)
    {
        return type switch
        {
            TokenType.Not => 3,
            TokenType.And => 2,
            TokenType.Or => 1,
            _ => 0
        };
    }
}
