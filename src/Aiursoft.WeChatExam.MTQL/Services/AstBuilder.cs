using Aiursoft.WeChatExam.MTQL.Tokens;
using Aiursoft.WeChatExam.MTQL.Ast;

namespace Aiursoft.WeChatExam.MTQL.Services;

public static class AstBuilder
{
    public static Expr Build(List<Token> rpn)
    {
        var stack = new Stack<Expr>();

        foreach (var token in rpn)
        {
            switch (token.Type)
            {
                case TokenType.Tag:
                    stack.Push(new TagExpr(token.Value));
                    break;
                case TokenType.Not:
                    if (stack.Count < 1) throw new ArgumentException("Missing operand for 'not'.");
                    var inner = stack.Pop();
                    stack.Push(new NotExpr(inner));
                    break;
                case TokenType.And:
                    if (stack.Count < 2) throw new ArgumentException("Missing operand for '&&'.");
                    var rightAnd = stack.Pop();
                    var leftAnd = stack.Pop();
                    stack.Push(new AndExpr(leftAnd, rightAnd));
                    break;
                case TokenType.Or:
                    if (stack.Count < 2) throw new ArgumentException("Missing operand for '||'.");
                    var rightOr = stack.Pop();
                    var leftOr = stack.Pop();
                    stack.Push(new OrExpr(leftOr, rightOr));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected token type in RPN: {token.Type}");
            }
        }

        if (stack.Count == 0)
        {
            throw new ArgumentException("Empty expression.");
        }

        if (stack.Count > 1)
        {
            throw new ArgumentException("Invalid expression. Too many operands.");
        }

        return stack.Pop();
    }
}
