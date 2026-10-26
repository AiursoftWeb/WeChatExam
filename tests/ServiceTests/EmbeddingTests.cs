using System.Net;
using System.Text.Json;
using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Tests.TestServices;
using Aiursoft.WeChatExam.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class EmbeddingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BackfillAndContentEditsRegenerateBothKindsOfContent(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var question = NewQuestion();
        var point = NewKnowledgePoint();
        db.AddRange(question, point);
        await db.SaveChangesAsync();
        var handler = new EmbeddingHandler();
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };

        await RunJobs(db, settings, handler);
        db.ChangeTracker.Clear();
        question = await db.Questions.SingleAsync();
        point = await db.KnowledgePoints.SingleAsync();
        Assert.AreEqual(4096, question.Embedding!.Length);
        Assert.AreEqual(4096, point.Embedding!.Length);
        Assert.AreEqual(question.LastUpdatedAt, question.LastEmbeddedAt);
        Assert.AreEqual(point.LastUpdatedAt, point.LastEmbeddedAt);
        Assert.AreEqual(1f, EmbeddingHelper.Deserialize(question.Embedding)![0]);
        Assert.IsTrue(handler.Inputs.Any(i => i.Contains(question.StandardAnswer) && i.Contains(question.Metadata)));
        Assert.IsTrue(handler.Inputs.Any(i => i.Contains(point.Title) && i.Contains(point.Content)));

        await RunJobs(db, settings, handler);
        Assert.HasCount(2, handler.Inputs, "Unchanged content must not call the model again.");

        question.Explanation = "An updated explanation about the guqin.";
        point.Title = "Guqin music and its history";
        await db.SaveChangesAsync();
        Assert.IsNull(question.Embedding);
        Assert.IsNull(point.Embedding);
        await RunJobs(db, settings, handler);
        db.ChangeTracker.Clear();
        Assert.IsNotNull((await db.Questions.SingleAsync()).Embedding);
        Assert.IsNotNull((await db.KnowledgePoints.SingleAsync()).Embedding);
        Assert.HasCount(4, handler.Inputs);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task EditsOrDeletionDuringGenerationDiscardStaleResults(bool relational, bool delete)
    {
        await using var db = await CreateDatabase(relational);
        db.AddRange(NewQuestion(), NewKnowledgePoint());
        await db.SaveChangesAsync();
        var editorOptions = relational
            ? new DbContextOptionsBuilder<SqliteContext>().UseSqlite(db.Database.GetDbConnection()).Options
            : (DbContextOptions)db.GetService<IDbContextOptions>();
        var handler = new EmbeddingHandler
        {
            BeforeResponse = async input =>
            {
                // A second context represents an editor saving while inference is in progress.
                await using var editor = CreateMatchingContext(editorOptions);
                if (input.Contains("Choice"))
                {
                    var question = await editor.Questions.SingleAsync();
                    if (delete) editor.Remove(question);
                    else question.StandardAnswer = "An edited answer";
                }
                else
                {
                    var point = await editor.KnowledgePoints.SingleAsync();
                    if (delete) editor.Remove(point);
                    else point.Content = "An edited knowledge point";
                }
                await editor.SaveChangesAsync();
            }
        };

        await RunJobs(db, new StubGlobalSettingsService { EmbeddingEnabled = true }, handler);
        db.ChangeTracker.Clear();
        if (delete)
        {
            Assert.AreEqual(0, await db.Questions.CountAsync());
            Assert.AreEqual(0, await db.KnowledgePoints.CountAsync());
        }
        else
        {
            var question = await db.Questions.SingleAsync();
            var point = await db.KnowledgePoints.SingleAsync();
            Assert.AreEqual("An edited answer", question.StandardAnswer);
            Assert.AreEqual("An edited knowledge point", point.Content);
            Assert.IsNull(question.Embedding);
            Assert.IsNull(point.Embedding);
        }
    }

    [TestMethod]
    public async Task DisabledGenerationLeavesContentPending()
    {
        await using var db = await CreateDatabase(false);
        db.AddRange(NewQuestion(), NewKnowledgePoint());
        await db.SaveChangesAsync();
        var handler = new EmbeddingHandler();
        await RunJobs(db, new StubGlobalSettingsService(), handler);
        Assert.HasCount(0, handler.Inputs);
        Assert.IsNull((await db.Questions.SingleAsync()).Embedding);
        Assert.IsNull((await db.KnowledgePoints.SingleAsync()).Embedding);
    }

    [TestMethod]
    public async Task ReorderingAndAssociationsDoNotRegenerateContent()
    {
        await using var db = await CreateDatabase(false);
        var question = NewQuestion();
        var point = NewKnowledgePoint();
        db.AddRange(question, point);
        await db.SaveChangesAsync();
        var handler = new EmbeddingHandler();
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        await RunJobs(db, settings, handler);

        question.OrderIndex = 10;
        point.AudioUrl = "https://example.com/audio.mp3";
        db.KnowledgePointQuestions.Add(new KnowledgePointQuestion
        {
            KnowledgePointId = point.Id, QuestionId = question.Id
        });
        db.SaveChanges();
        await RunJobs(db, settings, handler);
        Assert.HasCount(2, handler.Inputs);

        question.Content = "A changed question saved synchronously";
        point.Content = "A changed knowledge point saved synchronously";
        db.SaveChanges();
        Assert.IsNull(question.Embedding);
        Assert.IsNull(point.Embedding);
    }

    [TestMethod]
    public async Task FailedDocumentDoesNotPreventOtherDocumentsAndCanRecover()
    {
        await using var db = await CreateDatabase(false);
        db.AddRange(NewQuestion(), NewKnowledgePoint());
        await db.SaveChangesAsync();
        var handler = new EmbeddingHandler { FailQuestions = true };
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        await RunJobs(db, settings, handler);
        Assert.IsNull((await db.Questions.SingleAsync()).Embedding);
        Assert.IsNotNull((await db.KnowledgePoints.SingleAsync()).Embedding);
        handler.FailQuestions = false;
        await RunJobs(db, settings, handler);
        Assert.IsNotNull((await db.Questions.SingleAsync()).Embedding);
    }

    [TestMethod]
    public async Task InvalidModelOutputIsNotStored()
    {
        await using var db = await CreateDatabase(false);
        db.AddRange(NewQuestion(), NewKnowledgePoint());
        await db.SaveChangesAsync();
        var handler = new EmbeddingHandler { Dimensions = 3 };
        await RunJobs(db, new StubGlobalSettingsService { EmbeddingEnabled = true }, handler);
        Assert.IsNull((await db.Questions.SingleAsync()).Embedding);
        Assert.IsNull((await db.KnowledgePoints.SingleAsync()).Embedding);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "context length exceeded")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "{\"error\":\"the input length exceeds the context length\"}")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "CONTEXT WINDOW EXCEEDED")]
    public async Task LongContentRetriesWithSmallerInputAndKeepsTheConclusion(HttpStatusCode status, string error)
    {
        var handler = new EmbeddingHandler
        {
            ContextLimit = 4000, ContextErrorStatusCode = status, ContextErrorMessage = error
        };
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        var service = new EmbeddingService(settings, handler, NullLogger<EmbeddingService>.Instance);
        var vector = await service.GenerateAsync("Introduction " + new string('a', 10000) + " Conclusion");
        Assert.HasCount(1024, vector);
        Assert.HasCount(2, handler.Inputs);
        Assert.AreEqual(8000, handler.Inputs[0].Length);
        Assert.AreEqual(4000, handler.Inputs[1].Length);
        Assert.StartsWith("Introduction", handler.Inputs[1]);
        Assert.EndsWith("Conclusion", handler.Inputs[1]);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request entity too large")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Context service unavailable")]
    [DataRow(HttpStatusCode.BadRequest, "Unknown model")]
    [DataRow(HttpStatusCode.BadRequest, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.BadRequest, "Context service unavailable")]
    [DataRow(HttpStatusCode.InternalServerError, "context length exceeded")]
    public async Task UnrelatedErrorsDoNotRetryWithTruncatedContent(HttpStatusCode status, string error)
    {
        var handler = new EmbeddingHandler
        {
            ContextLimit = 0, ContextErrorStatusCode = status, ContextErrorMessage = error
        };
        var service = new EmbeddingService(new StubGlobalSettingsService { EmbeddingEnabled = true },
            handler, NullLogger<EmbeddingService>.Instance);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            service.GenerateAsync(new string('a', 10000)));

        Assert.AreEqual(status, exception.StatusCode);
        Assert.HasCount(1, handler.Inputs);
        Assert.AreEqual(8000, handler.Inputs[0].Length);
    }

    [TestMethod]
    public async Task ContextErrorsStopAtTheMinimumInputBudget()
    {
        var handler = new EmbeddingHandler
        {
            ContextLimit = 0, ContextErrorStatusCode = HttpStatusCode.RequestEntityTooLarge,
            ContextErrorMessage = "the input length exceeds the context length"
        };
        var service = new EmbeddingService(new StubGlobalSettingsService { EmbeddingEnabled = true },
            handler, NullLogger<EmbeddingService>.Instance);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            service.GenerateAsync(new string('a', 10000)));

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, exception.StatusCode);
        CollectionAssert.AreEqual(new[] { 8000, 4000, 2000, 1000, 500 },
            handler.Inputs.Select(input => input.Length).ToArray());
    }

    private static Question NewQuestion() => new()
    {
        Id = Guid.NewGuid(), Content = "Who played Flowing Water?", QuestionType = QuestionType.Choice,
        Metadata = "[\"Boya\",\"Other\"]", StandardAnswer = "Boya", Explanation = "A guqin composition."
    };

    private static KnowledgePoint NewKnowledgePoint() => new()
    {
        Id = Guid.NewGuid(), Title = "Flowing Water", Content = "A guqin composition associated with Boya.", ParentId = null
    };

    private static async Task<WeChatExamDbContext> CreateDatabase(bool relational)
    {
        WeChatExamDbContext db;
        if (relational)
        {
            db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
                .UseSqlite("DataSource=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.MigrateAsync();
        }
        else
        {
            db = new InMemoryContext(new DbContextOptionsBuilder<InMemoryContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        }
        return db;
    }

    private static WeChatExamDbContext CreateMatchingContext(DbContextOptions options)
    {
        return options is DbContextOptions<SqliteContext> sqlite
            ? new SqliteContext(sqlite)
            : new InMemoryContext((DbContextOptions<InMemoryContext>)options);
    }

    private static async Task RunJobs(WeChatExamDbContext db, IGlobalSettingsService settings, EmbeddingHandler handler)
    {
        var service = new EmbeddingService(settings, handler, NullLogger<EmbeddingService>.Instance);
        var retry = new RetryEngine(NullLogger<RetryEngine>.Instance);
        IBackgroundJob[] jobs =
        [
            new GenerateQuestionEmbeddingsJob(db, settings, service, retry, NullLogger<GenerateQuestionEmbeddingsJob>.Instance),
            new GenerateKnowledgePointEmbeddingsJob(db, settings, service, retry, NullLogger<GenerateKnowledgePointEmbeddingsJob>.Instance)
        ];
        foreach (var job in jobs) await job.ExecuteAsync();
    }

    private sealed class EmbeddingHandler : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Inputs { get; } = [];
        public Func<string, Task>? BeforeResponse { get; init; }
        public bool FailQuestions { get; set; }
        public int Dimensions { get; init; } = 1024;
        public int ContextLimit { get; init; } = int.MaxValue;
        public HttpStatusCode ContextErrorStatusCode { get; init; } = HttpStatusCode.BadRequest;
        public string ContextErrorMessage { get; init; } = "context length exceeded";

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("/api/embed", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("bge-m3:latest", body.RootElement.GetProperty("model").GetString());
            var input = body.RootElement.GetProperty("input").GetString()!;
            Inputs.Add(input);
            if (BeforeResponse != null) await BeforeResponse(input);
            if (FailQuestions && input.Contains("Choice"))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (input.Length > ContextLimit)
                return new HttpResponseMessage(ContextErrorStatusCode) { Content = new StringContent(ContextErrorMessage) };
            var vector = new float[Dimensions];
            vector[0] = 2;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings = new[] { vector } }) };
        }
    }
}
