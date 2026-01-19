# MTQL Technical Design & Developer Guide

## Architecture Overview

MTQL implements a classic compiler pipeline pattern to transform string expressions into executable Code.

```text
MTQL String  →  Tokenizer  →  [Tokens]  →  Parser  →  [RPN]  →  AstBuilder  →  [AST]  →  PredicateBuilder  →  Expression<Func<T, bool>>
```

### 1. Tokenizer (`Services/Tokenizer.cs`)
*   **Responsibility**: lexing the input string.
*   **Output**: `List<Token>`.
*   **Handling**:
    *   Skips whitespace.
    *   Recognizes `(`, `)`, `&&`, `||` as operators.
    *   Recognizes `not` (case-insensitive) as a Keyword.
    *   Everything else is a `Tag`.

### 2. Parser (`Services/Parser.cs`)
*   **Responsibility**: Parsing tokens into Reverse Polish Notation (RPN).
*   **Algorithm**: **Shunting-yard algorithm**.
*   **Precedence Rules** (Highest to Lowest):
    1.  `not` (Right Associative)
    2.  `&&` (Left Associative)
    3.  `||` (Left Associative)

### 3. AST Builder (`Services/AstBuilder.cs`)
*   **Responsibility**: Converts RPN list into a tree structure.
*   **Nodes** (`Ast/Expr.cs`):
    *   `TagExpr`: Leaf node, represents a tag name.
    *   `NotExpr`: Unary node.
    *   `AndExpr`: Binary node.
    *   `OrExpr`: Binary node.

### 4. Predicate Builder (`Services/PredicateBuilder.cs`)
*   **Responsibility**: Compiles AST into LINQ Expression Tree.
*   **Key Logic**:
    *   Uses `Expression.AndAlso` and `Expression.OrElse` for short-circuiting logic.
    *   Uses `ExpressionVisitor` inside `ParameterRebinder` to merge two lambda expressions (`expr1` and `expr2`) into one by rewriting their parameters to share the same `ParameterExpression`.
    *   **Translation**:
        *   `TagExpr t` -> `q => q.QuestionTags.Any(qt => qt.Tag.NormalizedName == t.Tag)`

## Extensibility

To add new features (e.g., `startswith`):

1.  **Token**: Add `TokenType.StartsWith`.
2.  **Tokenizer**: Recognize `startswith(prefix)` pattern.
3.  **Parser**: Handle operator/function precedence.
4.  **AST**: Add `StartsWithExpr`.
5.  **PredicateBuilder**: Implement `q => q.QuestionTags.Any(t => t.TagName.StartsWith(...))`.

## Maintenance

*   **Testing**: Run `Aiursoft.WeChatExam.MTQL.Tests`.
*   **Core Logic**: Located in `src/Aiursoft.WeChatExam.MTQL`.
*   **Entities**: `Aiursoft.WeChatExam.Entities` (referenced for `Question` model).

## Safety

*   **No `Eval`**: We do not use Dynamic LINQ or string concatenation.
*   **Type Safety**: All expressions are strongly typed against the `Question` entity.
