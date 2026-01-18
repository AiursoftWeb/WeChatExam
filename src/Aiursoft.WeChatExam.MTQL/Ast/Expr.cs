namespace Aiursoft.WeChatExam.MTQL.Ast;

public abstract record Expr;

public record TagExpr(string Tag) : Expr;

public record NotExpr(Expr Inner) : Expr;

public record AndExpr(Expr Left, Expr Right) : Expr;

public record OrExpr(Expr Left, Expr Right) : Expr;
