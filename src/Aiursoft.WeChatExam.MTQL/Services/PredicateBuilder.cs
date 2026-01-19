using System.Linq.Expressions;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.MTQL.Ast;

namespace Aiursoft.WeChatExam.MTQL.Services;

public static class PredicateBuilder
{
    public static Expression<Func<Question, bool>> Build(Expr expr)
    {
        switch (expr)
        {
            case TagExpr t:
                return HasTag(t.Tag);

            case NotExpr n:
                var inner = Build(n.Inner);
                return Not(inner);

            case AndExpr a:
                var leftAnd = Build(a.Left);
                var rightAnd = Build(a.Right);
                return And(leftAnd, rightAnd);

            case OrExpr o:
                var leftOr = Build(o.Left);
                var rightOr = Build(o.Right);
                return Or(leftOr, rightOr);

            default:
                throw new NotSupportedException($"Unknown expression type: {expr.GetType().Name}");
        }
    }

    private static Expression<Func<Question, bool>> HasTag(string tagName)
    {
        // Use NormalizedName for consistent case-insensitive lookup
        var normalized = tagName.Trim().ToUpperInvariant();
        return q => q.QuestionTags.Any(qt => qt.Tag != null && qt.Tag.NormalizedName == normalized);
    }

    private static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> expr)
    {
        var candidate = expr.Parameters[0];
        var body = Expression.Not(expr.Body);
        return Expression.Lambda<Func<T, bool>>(body, candidate);
    }

    private static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        return Combine(left, right, Expression.AndAlso);
    }

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        return Combine(left, right, Expression.OrElse);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, Expression> merge)
    {
        var parameter = Expression.Parameter(typeof(T), "q");

        var leftBody = ParameterRebinder.ReplaceParameters(left.Parameters.Select((p, i) => (p, parameter)).ToDictionary(x => x.p, x => x.parameter), left.Body);
        var rightBody = ParameterRebinder.ReplaceParameters(right.Parameters.Select((p, i) => (p, parameter)).ToDictionary(x => x.p, x => x.parameter), right.Body);

        var body = merge(leftBody, rightBody);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}

internal class ParameterRebinder : ExpressionVisitor
{
    private readonly Dictionary<ParameterExpression, ParameterExpression> _map;

    private ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression> map)
    {
        _map = map ?? new Dictionary<ParameterExpression, ParameterExpression>();
    }

    public static Expression ReplaceParameters(Dictionary<ParameterExpression, ParameterExpression> map, Expression exp)
    {
        return new ParameterRebinder(map).Visit(exp);
    }

    protected override Expression VisitParameter(ParameterExpression p)
    {
        if (_map.TryGetValue(p, out var replacement))
        {
            return replacement;
        }

        return base.VisitParameter(p);
    }
}
