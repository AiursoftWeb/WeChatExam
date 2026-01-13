using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<KnowledgePoint> KnowledgePoints => Set<KnowledgePoint>();
    public DbSet<UserPracticeHistory> UserPracticeHistories => Set<UserPracticeHistory>();
    public DbSet<CategoryKnowledgePoint> CategoryKnowledgePoints => Set<CategoryKnowledgePoint>();
    public DbSet<KnowledgePointQuestion> KnowledgePointQuestions => Set<KnowledgePointQuestion>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<QuestionTag> QuestionTags => Set<QuestionTag>();
    public DbSet<Paper> Papers => Set<Paper>();
    public DbSet<PaperQuestion> PaperQuestions => Set<PaperQuestion>();
    public DbSet<PaperSnapshot> PaperSnapshots => Set<PaperSnapshot>();
    public DbSet<QuestionSnapshot> QuestionSnapshots => Set<QuestionSnapshot>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamRecord> ExamRecords => Set<ExamRecord>();
    public DbSet<AnswerRecord> AnswerRecords => Set<AnswerRecord>();
    public DbSet<DistributionChannel> DistributionChannels => Set<DistributionChannel>();
    public DbSet<UserDistributionChannel> UserDistributionChannels => Set<UserDistributionChannel>();
}
