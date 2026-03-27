using Aiursoft.WeChatExam.MTQL.Ast;

namespace Aiursoft.WeChatExam.MTQL.Services;

public static class TagExtractor
{
    public static List<string> ExtractTags(Expr expr)
    {
        var tags = new List<string>();
        Extract(expr, tags);
        return tags.Distinct().ToList();
    }

    private static void Extract(Expr expr, List<string> tags)
    {
        switch (expr)
        {
            case TagExpr t:
                tags.Add(t.Tag);
                break;
            case NotExpr n:
                Extract(n.Inner, tags);
                break;
            case AndExpr a:
                Extract(a.Left, tags);
                Extract(a.Right, tags);
                break;
            case OrExpr o:
                Extract(o.Left, tags);
                Extract(o.Right, tags);
                break;
            default:
                throw new NotSupportedException($"Unknown expression type: {expr.GetType().Name}");
        }
    }
}
