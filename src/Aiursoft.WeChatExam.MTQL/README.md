# MTQL (Music Tag Query Language)

MTQL is a lightweight, expression-based query language designed to filter questions based on their tags efficiently. It supports boolean logic (`AND`, `OR`, `NOT`) and grouping with parentheses.

## 🚀 Features

*   **Simple Syntax**: Natural boolean logic.
*   **Case Insensitive**: `not`, `NOT`, `Not` all work.
*   **Secure**: Compiles to `Expression<Func<Question, bool>>` for safe execution by EF Core. SQL Injection is impossible by design.
*   **Flexible**: Supports arbitrary logic depth.

## 📖 Syntax

### Tags
Any word that isn't a reserved keyword (`not`) or operator (`&&`, `||`) is treated as a **Tag**.
*   Example: `rock`, `classic`, `四川音乐学院`

### Operators

| Operator | Type | Priority | Description |
| :--- | :--- | :--- | :--- |
| `not` | Unary | High | Negates the following expression. |
| `&&` | Binary | Medium | Logical AND. Both sides must be true. |
| `||` | Binary | Low | Logical OR. At least one side must be true. |

### Grouping
Use parentheses `( )` to override default precedence.

## 💡 Examples

| Query | Description |
| :--- | :--- |
| `rock` | Questions with tag "rock". |
| `rock && metal` | Questions with **both** "rock" and "metal". |
| `rock || pop` | Questions with **either** "rock" or "pop" (or both). |
| `not jazz` | Questions **without** tag "jazz". |
| `rock && not metal` | Questions with "rock" but **no** "metal". |
| `(rock || jazz) && metal` | Questions with "metal", AND either "rock" or "jazz". |

## 🛠 Usage in API

Pass the query string to the `mtql` parameter in the `GetQuestions` API.

```http
GET /api/Questions?mtql=rock%20%26%26%20not%20metal
```
