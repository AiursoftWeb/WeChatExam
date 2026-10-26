using Aiursoft.WeChatExam.MySql;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// This test class ensures that the Entity Framework migrations are up-to-date for all supported database providers.
/// If you change the database model (entities), you must create a new migration for both SQLite and MySQL.
/// </summary>
[TestClass]
public class MigrationTests
{
    [TestMethod]
    public async Task RemovingKnowledgeHierarchyPreservesContentAndCategoryOrder()
    {
        var options = new DbContextOptionsBuilder<SqliteContext>().UseSqlite("DataSource=:memory:").Options;
        await using var context = new SqliteContext(options);
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261026090703_AddQuestionPeriodAssignment");
        var course = new Category { Id = Guid.NewGuid(), Title = "History", ParentId = null };
        var chapter = new Category { Id = Guid.NewGuid(), Title = "Early music", ParentId = course.Id };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Overview", Content = "Preserved lesson", AudioUrl = "/lesson.mp3" };
        var question = new Question { Id = Guid.NewGuid(), Content = "Preserved exercise" };
        context.AddRange(course, chapter, point, question,
            new CategoryKnowledgePoint { CategoryId = chapter.Id, KnowledgePointId = point.Id, OrderIndex = 7 },
            new KnowledgePointQuestion { KnowledgePointId = point.Id, QuestionId = question.Id });
        await context.SaveChangesAsync();
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        var saved = await context.KnowledgePoints.Include(k => k.CategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions).SingleAsync();
        Assert.AreEqual(point.Id, saved.Id);
        Assert.AreEqual("Preserved lesson", saved.Content);
        Assert.AreEqual("/lesson.mp3", saved.AudioUrl);
        Assert.AreEqual(7, saved.CategoryKnowledgePoints.Single().OrderIndex);
        Assert.AreEqual(chapter.Id, saved.CategoryKnowledgePoints.Single().CategoryId);
        Assert.AreEqual(question.Id, saved.KnowledgePointQuestions.Single().QuestionId);
        Assert.AreEqual(course.Id, (await context.Categories.SingleAsync(c => c.Id == chapter.Id)).ParentId);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('KnowledgePoints') WHERE name = 'ParentId'";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync());
    }

    [TestMethod]
    public void TestSqliteMigrations()
    {
        var options = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new SqliteContext(options);
        var hasPendingChanges = context.Database.HasPendingModelChanges();
        Assert.IsFalse(hasPendingChanges, "There are pending model changes for Sqlite. Please run 'dotnet ef migrations add' for Sqlite.");
    }

    [TestMethod]
    public void TestMySqlMigrations()
    {
        var options = new DbContextOptionsBuilder<MySqlContext>()
            .UseMySql("Server=localhost;Database=test;Uid=root;Pwd=password;", new MySqlServerVersion(new Version(8, 0, 31)))
            .Options;
        using var context = new MySqlContext(options);
        var hasPendingChanges = context.Database.HasPendingModelChanges();
        Assert.IsFalse(hasPendingChanges, "There are pending model changes for MySql. Please run 'dotnet ef migrations add' for MySql.");
    }
}
