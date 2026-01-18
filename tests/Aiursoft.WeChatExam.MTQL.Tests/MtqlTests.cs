using Microsoft.VisualStudio.TestTools.UnitTesting;
using Aiursoft.WeChatExam.MTQL.Services;
using Aiursoft.WeChatExam.MTQL.Tokens;
using Aiursoft.WeChatExam.MTQL.Ast;
using Aiursoft.WeChatExam.Entities;
using System.Linq.Expressions;

namespace Aiursoft.WeChatExam.MTQL.Tests;

[TestClass]
public class MtqlTests
{
    [TestMethod]
    public void TestTokenizer()
    {
        var input = "(a || b) && not c";
        var tokens = Tokenizer.Tokenize(input);

        Assert.AreEqual(8, tokens.Count);
        Assert.AreEqual(TokenType.LParen, tokens[0].Type);
        Assert.AreEqual("a", tokens[1].Value);
        Assert.AreEqual(TokenType.Or, tokens[2].Type);
        Assert.AreEqual("b", tokens[3].Value);
        Assert.AreEqual(TokenType.RParen, tokens[4].Type);
        Assert.AreEqual(TokenType.And, tokens[5].Type);
        Assert.AreEqual(TokenType.Not, tokens[6].Type);
        Assert.AreEqual("c", tokens[7].Value);
    }

    [TestMethod]
    public void TestTokenizerRecursive()
    {
         var input = "(a || b) && not c";
         // Expected: (, a, ||, b, ), &&, not, c
         var tokens = Tokenizer.Tokenize(input);
         Assert.AreEqual(8, tokens.Count);
         Assert.AreEqual("c", tokens[7].Value);
    }

    [TestMethod]
    public void TestParserPrecedence()
    {
        // a || b && c  =>  a || (b && c)
        // RPN: a b c && ||
        var tokens = Tokenizer.Tokenize("a || b && c");
        var rpn = Parser.ToRpn(tokens);

        Assert.AreEqual(5, rpn.Count);
        Assert.AreEqual("a", rpn[0].Value);
        Assert.AreEqual("b", rpn[1].Value);
        Assert.AreEqual("c", rpn[2].Value);
        Assert.AreEqual(TokenType.And, rpn[3].Type);
        Assert.AreEqual(TokenType.Or, rpn[4].Type);
    }

    [TestMethod]
    public void TestParserParentheses()
    {
        // (a || b) && c => a b || c &&
        var tokens = Tokenizer.Tokenize("(a || b) && c");
        var rpn = Parser.ToRpn(tokens);

        Assert.AreEqual(5, rpn.Count);
        Assert.AreEqual("a", rpn[0].Value);
        Assert.AreEqual("b", rpn[1].Value);
        Assert.AreEqual(TokenType.Or, rpn[2].Type);
        Assert.AreEqual("c", rpn[3].Value);
        Assert.AreEqual(TokenType.And, rpn[4].Type);
    }

    [TestMethod]
    public void TestAstBuilder()
    {
        // not a && b  =>  And(Not(a), b)
        // RPN: a not b &&
        var tokens = Tokenizer.Tokenize("not a && b");
        var rpn = Parser.ToRpn(tokens);
        var ast = AstBuilder.Build(rpn);

        Assert.IsInstanceOfType(ast, typeof(AndExpr));
        var andExpr = (AndExpr)ast;
        Assert.IsInstanceOfType(andExpr.Left, typeof(NotExpr));
        Assert.IsInstanceOfType(andExpr.Right, typeof(TagExpr));
        
        var notExpr = (NotExpr)andExpr.Left;
        Assert.IsInstanceOfType(notExpr.Inner, typeof(TagExpr));
        Assert.AreEqual("a", ((TagExpr)notExpr.Inner).Tag);
        Assert.AreEqual("b", ((TagExpr)andExpr.Right).Tag);
    }

    [TestMethod]
    public void TestPredicateBuilder()
    {
        var questions = new List<Question>
        {
            MakeQuestion("q1", "rock", "metal"),
            MakeQuestion("q2", "pop"),
            MakeQuestion("q3", "rock"),
            MakeQuestion("q4", "jazz", "metal")
        }.AsQueryable();

        // Query: rock && not metal
        // Expected: q3 only (q1 has metal)
        var mtql = "rock && not metal";
        var tokens = Tokenizer.Tokenize(mtql);
        var rpn = Parser.ToRpn(tokens);
        var ast = AstBuilder.Build(rpn);
        var predicate = PredicateBuilder.Build(ast);

        var compiled = predicate.Compile();
        var results = questions.Where(compiled).ToList();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("q1_content_q3", results[0].Content); // Helper makes content somewhat unique
    }

    private Question MakeQuestion(string idSuffix, params string[] tags)
    {
        return new Question
        {
            Id = Guid.NewGuid(),
            Content = $"q1_content_{idSuffix}",
            QuestionType = QuestionType.Choice,
            GradingStrategy = GradingStrategy.ExactMatch,
            CategoryId = Guid.NewGuid(),
            QuestionTags = tags.Select(t => new QuestionTag 
            { 
               Tag = new Tag { DisplayName = t, NormalizedName = t.ToUpper() },
               TagId = 1,
               QuestionId = Guid.NewGuid()
            }).ToList()
        };
    }
}
