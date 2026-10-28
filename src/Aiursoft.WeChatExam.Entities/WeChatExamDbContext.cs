using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

public abstract class WeChatExamDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        InvalidateChangedEmbeddings();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        InvalidateChangedEmbeddings();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void InvalidateChangedEmbeddings()
    {
        // Keep imports, manual edits and AI edits on the same content-update path.
        foreach (var entry in ChangeTracker.Entries<Question>().Where(e =>
                     e.State == EntityState.Added || e.State == EntityState.Modified &&
                     (e.Property(q => q.Content).IsModified ||
                      e.Property(q => q.QuestionType).IsModified ||
                      e.Property(q => q.Metadata).IsModified ||
                      e.Property(q => q.StandardAnswer).IsModified ||
                      e.Property(q => q.Explanation).IsModified)))
        {
            entry.Entity.LastUpdatedAt = DateTime.UtcNow;
            entry.Entity.Embedding = null;
        }

        foreach (var entry in ChangeTracker.Entries<KnowledgePoint>().Where(e =>
                     e.State == EntityState.Added || e.State == EntityState.Modified &&
                     (e.Property(k => k.Title).IsModified || e.Property(k => k.Content).IsModified)))
        {
            entry.Entity.LastUpdatedAt = DateTime.UtcNow;
            entry.Entity.Embedding = null;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PaperSnapshot>().HasOne(s => s.MockPaper).WithMany()
            .HasForeignKey(s => s.MockPaperId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaperSnapshot>().HasIndex(s => new { s.MockPaperId, s.Version }).IsUnique();
        builder.Entity<PaperSnapshot>().ToTable(t => t.HasCheckConstraint(
            "CK_PaperSnapshot_SingleSource", "MockPaperId IS NULL OR PaperId IS NULL"));

        builder.Entity<MockPaperQuestion>().HasKey(q => new { q.MockPaperId, q.QuestionId });
        builder.Entity<MockPaperQuestion>()
            .HasOne(q => q.MockPaper).WithMany().HasForeignKey(q => q.MockPaperId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MockPaperQuestion>()
            .HasOne(q => q.Question).WithMany().HasForeignKey(q => q.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MockPaper>()
            .HasOne(p => p.TotalCategory).WithMany().HasForeignKey(p => p.TotalCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VipProduct>()
            .HasOne(p => p.TotalCategory)
            .WithMany()
            .HasForeignKey(p => p.TotalCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuestionCategory>()
            .HasOne(c => c.TotalCategory)
            .WithMany()
            .HasForeignKey(c => c.TotalCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Question>()
            .HasOne(q => q.QuestionCategory)
            .WithMany()
            .HasForeignKey(q => q.QuestionCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<KnowledgeCategory>()
            .HasOne(c => c.TotalCategory).WithMany().HasForeignKey(c => c.TotalCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<KnowledgeCategory>()
            .HasOne(c => c.ParentKnowledgeCategory).WithMany(c => c.Children)
            .HasPrincipalKey(c => new { c.Id, c.TotalCategoryId })
            .HasForeignKey(c => new { c.ParentKnowledgeCategoryId, c.TotalCategoryId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<KnowledgeCategoryKnowledgePoint>()
            .HasOne(c => c.KnowledgeCategory).WithMany(c => c.KnowledgeCategoryKnowledgePoints)
            .HasForeignKey(c => c.KnowledgeCategoryId).OnDelete(DeleteBehavior.Restrict);

        // Protect financial records from being cascade-deleted when a user deletes their account.
        builder.Entity<PaymentOrder>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VipMembership>()
            .HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public DbSet<MockPaperQuestion> MockPaperQuestions => Set<MockPaperQuestion>();
    public DbSet<MockPaper> MockPapers => Set<MockPaper>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<TotalCategory> TotalCategories => Set<TotalCategory>();
    public DbSet<QuestionCategory> QuestionCategories => Set<QuestionCategory>();
    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryTaxonomy> CategoryTaxonomies => Set<CategoryTaxonomy>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<KnowledgePoint> KnowledgePoints => Set<KnowledgePoint>();
    public DbSet<UserPracticeHistory> UserPracticeHistories => Set<UserPracticeHistory>();
    public DbSet<KnowledgeCategoryKnowledgePoint> KnowledgeCategoryKnowledgePoints => Set<KnowledgeCategoryKnowledgePoint>();
    public DbSet<KnowledgePointQuestion> KnowledgePointQuestions => Set<KnowledgePointQuestion>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Taxonomy> Taxonomies => Set<Taxonomy>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<QuestionTag> QuestionTags => Set<QuestionTag>();
    public DbSet<PaperTag> PaperTags => Set<PaperTag>();
    public DbSet<Paper> Papers => Set<Paper>();
    public DbSet<PaperQuestion> PaperQuestions => Set<PaperQuestion>();
    public DbSet<PaperCategory> PaperCategories => Set<PaperCategory>();
    public DbSet<PaperSnapshot> PaperSnapshots => Set<PaperSnapshot>();
    public DbSet<QuestionSnapshot> QuestionSnapshots => Set<QuestionSnapshot>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamRecord> ExamRecords => Set<ExamRecord>();
    public DbSet<AnswerRecord> AnswerRecords => Set<AnswerRecord>();
    public DbSet<DistributionChannel> DistributionChannels => Set<DistributionChannel>();
    public DbSet<UserDistributionChannel> UserDistributionChannels => Set<UserDistributionChannel>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<CouponVipProduct> CouponVipProducts => Set<CouponVipProduct>();
    public DbSet<UserClaimedCoupon> UserClaimedCoupons => Set<UserClaimedCoupon>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentLog> PaymentLogs => Set<PaymentLog>();
    public DbSet<VipMembership> VipMemberships => Set<VipMembership>();
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<VipProduct> VipProducts => Set<VipProduct>();
}
