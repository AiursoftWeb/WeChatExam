using System.Net;
using System.Net.Http.Headers;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ExamsApiTests : TestBase
{
    private readonly User _owner = new() { Id = Guid.NewGuid().ToString(), UserName = "Exam owner", DisplayName = "Owner" };
    private readonly User _other = new() { Id = Guid.NewGuid().ToString(), UserName = "Other student", DisplayName = "Other" };
    private Guid _examId;
    private Guid _recordId;
    private Guid _snapshotId;
    private Guid _questionId;
    private readonly Guid _paperId = Guid.NewGuid();
    private readonly Guid _latestId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _otherCourseId = Guid.NewGuid();
    private readonly Guid _schoolId = Guid.NewGuid();
    private readonly Guid _otherSchoolId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        // A locked historical snapshot still works after its source template is retired.
        var snapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Locked exam version", Version = 1, TimeLimit = 60 };
        var question = new QuestionSnapshot
        {
            Id = Guid.NewGuid(), PaperSnapshot = snapshot, PaperSnapshotId = snapshot.Id,
            Content = "Select the correct option", StandardAnswer = "Confidential answer",
            GradingStrategy = GradingStrategy.ExactMatch, QuestionType = QuestionType.Choice, Score = 10,
            Metadata = "{\"options\":[\"A\",\"B\"]}"
        };
        var exam = new Exam
        {
            Id = Guid.NewGuid(), Title = "Scheduled exam boundary", PaperSnapshot = snapshot,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = DateTime.UtcNow.AddHours(2),
            DurationMinutes = 60, AllowedAttempts = 1, IsPublic = true, AllowReview = true,
            ShowAnswerAfter = DateTime.UtcNow.AddHours(3)
        };
        var record = new ExamRecord
        {
            Id = Guid.NewGuid(), Exam = exam, ExamId = exam.Id, User = _owner, UserId = _owner.Id,
            PaperSnapshot = snapshot, PaperSnapshotId = snapshot.Id, AttemptIndex = 1,
            TeacherComment = "Teacher reveals Confidential answer"
        };
        db.AddRange(_other, question, record);
        await db.SaveChangesAsync();
        _examId = exam.Id; _recordId = record.Id; _snapshotId = snapshot.Id; _questionId = question.Id;
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var records = db.ExamRecords.Where(r => r.ExamId == _examId);
            db.AnswerRecords.RemoveRange(await db.AnswerRecords.Where(a => records.Any(r => r.Id == a.ExamRecordId)).ToListAsync());
            db.ExamRecords.RemoveRange(await records.ToListAsync());
            db.Exams.RemoveRange(await db.Exams.Where(e => e.Id == _examId).ToListAsync());
            db.QuestionSnapshots.RemoveRange(await db.QuestionSnapshots.Where(q => q.PaperSnapshotId == _snapshotId || q.PaperSnapshotId == _latestId).ToListAsync());
            db.PaperSnapshots.RemoveRange(await db.PaperSnapshots.Where(p => p.Id == _snapshotId || p.Id == _latestId).ToListAsync());
            db.VipMemberships.RemoveRange(await db.VipMemberships.Where(m => m.VipProductId == _productId).ToListAsync());
            db.VipProducts.RemoveRange(await db.VipProducts.Where(p => p.Id == _productId).ToListAsync());
            db.Papers.RemoveRange(await db.Papers.Where(p => p.Id == _paperId).ToListAsync());
            db.MockPapers.RemoveRange(await db.MockPapers.Where(p => p.Id == _paperId).ToListAsync());
            db.RealPapers.RemoveRange(await db.RealPapers.Where(p => p.Id == _paperId).ToListAsync());
            db.TotalCategories.RemoveRange(await db.TotalCategories.Where(c => c.Id == _courseId || c.Id == _otherCourseId).ToListAsync());
            db.Institutions.RemoveRange(await db.Institutions.Where(i => i.Id == _schoolId || i.Id == _otherSchoolId).ToListAsync());
            db.Users.RemoveRange(await db.Users.Where(u => u.Id == _owner.Id || u.Id == _other.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
        await base.CleanServer();
    }

    private void Authenticate(User user)
    {
        var secret = GetService<IConfiguration>()["AppSettings:WeChat:AppSecret"]!;
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            WeChatTokenHelper.GenerateJwtToken(user, "test-exam-openid", secret));
    }

    private Task<HttpResponseMessage> Answer(Guid? question = null, string value = "My answer") =>
        Http.PostAsJsonAsync($"/api/Exams/record/{_recordId}/answer", new SubmitAnswerDto
        { QuestionSnapshotId = question ?? _questionId, Answer = value });

    private async Task BindPublishedPaper(bool real, bool free, string entitlement, int startDays, int endDays)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.TotalCategories.AddRange(new TotalCategory { Id = _courseId, Title = "Exam course" },
            new TotalCategory { Id = _otherCourseId, Title = "Other course" });
        db.Institutions.AddRange(new Institution { Id = _schoolId, Title = "Exam school" },
            new Institution { Id = _otherSchoolId, Title = "Other school" });
        var snapshot = await db.PaperSnapshots.SingleAsync(p => p.Id == _snapshotId);
        // Legacy IsFree must never make an institution paper free.
        db.Entry(snapshot).Property(p => p.IsFree).CurrentValue = true;
        if (real)
        {
            db.RealPapers.Add(new RealPaper { Id = _paperId, Title = "Current template", InstitutionId = _schoolId, Status = PaperStatus.Publishable });
            db.Entry(snapshot).Property(p => p.RealPaperId).CurrentValue = _paperId;
        }
        else
        {
            db.MockPapers.Add(new MockPaper { Id = _paperId, Title = "Current template", TotalCategoryId = _courseId,
                AllowViewAnonymousely = free, Status = PaperStatus.Publishable });
            db.Entry(snapshot).Property(p => p.MockPaperId).CurrentValue = _paperId;
        }
        if (entitlement != "none")
        {
            var type = entitlement.Contains("course") ? VipProductType.Category :
                entitlement.Contains("school") ? VipProductType.Institution : VipProductType.RealExam;
            var product = new VipProduct
            {
                Id = _productId, Name = "Renamed or discontinued membership", Type = type, IsEnabled = false,
                TotalCategoryId = type == VipProductType.Category ? (entitlement == "wrong-course" ? _otherCourseId : _courseId) : null,
                InstitutionId = type == VipProductType.Institution ? (entitlement == "wrong-school" ? _otherSchoolId : _schoolId) : null
            };
            db.VipMemberships.Add(new VipMembership { UserId = _other.Id, VipProductId = product.Id, VipProduct = product,
                StartTime = DateTime.UtcNow.AddDays(startDays), EndTime = DateTime.UtcNow.AddDays(endDays) });
        }
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task RetiredPaperBrowsingPreservesLockedExamsAndTheirAccessRules()
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Papers.Add(new Paper { Id = _paperId, Title = "Retained legacy source", IsRealExam = true });
            var snapshot = await db.PaperSnapshots.SingleAsync(p => p.Id == _snapshotId);
            db.Entry(snapshot).Property(p => p.PaperId).CurrentValue = _paperId;
            await db.SaveChangesAsync();
        }
        Authenticate(_owner);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/Papers/snapshots/{_snapshotId}")).StatusCode);
        var history = (await Http.GetFromJsonAsync<List<ExamRecordDto>>("/api/Exams/history"))!;
        Assert.AreEqual(_recordId, history.Single().Id);
        using var resumed = await Http.PostAsync($"/api/Exams/{_examId}/start", null);
        Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode);
        var session = (await resumed.Content.ReadFromJsonAsync<ExamSessionDto>())!;
        Assert.AreEqual(_snapshotId, session.PaperSnapshotId);
        Assert.AreEqual(_questionId, session.Questions.Single().Id);
        Assert.DoesNotContain("Confidential answer", await resumed.Content.ReadAsStringAsync());

        // Keeping an old exam does not let a new participant bypass its paid source.
        Authenticate(_other);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
        using var verify = Server.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(1, await context.ExamRecords.CountAsync(r => r.ExamId == _examId));
        Assert.AreEqual("Confidential answer", (await context.QuestionSnapshots.SingleAsync(q => q.Id == _questionId)).StandardAnswer);
    }

    [TestMethod]
    [DataRow(false, false, "none", -1, 1, false)]
    [DataRow(false, true, "none", -1, 1, true)]
    [DataRow(false, false, "course", -1, 1, true)]
    [DataRow(false, false, "wrong-course", -1, 1, false)]
    [DataRow(false, false, "school", -1, 1, false)]
    [DataRow(false, false, "global", -1, 1, false)]
    [DataRow(false, false, "course", -2, -1, false)]
    [DataRow(false, false, "course", 1, 2, false)]
    [DataRow(true, false, "none", -1, 1, false)]
    [DataRow(true, false, "course", -1, 1, false)]
    [DataRow(true, false, "wrong-school", -1, 1, false)]
    [DataRow(true, false, "school", -1, 1, true)]
    [DataRow(true, false, "global", -1, 1, true)]
    [DataRow(true, false, "school", -2, -1, false)]
    [DataRow(true, false, "school", 1, 2, false)]
    public async Task PublicScheduleRequiresWholePaperEntitlementBeforeCreatingAnAttempt(
        bool real, bool free, string entitlement, int startDays, int endDays, bool allowed)
    {
        await BindPublishedPaper(real, free, entitlement, startDays, endDays);
        Authenticate(_other);
        var response = await Http.PostAsync($"/api/Exams/{_examId}/start", null);
        Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Confidential answer", await response.Content.ReadAsStringAsync());
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(allowed ? 1 : 0, await db.ExamRecords.CountAsync(r => r.ExamId == _examId && r.UserId == _other.Id));
        if (allowed)
        {
            var session = (await response.Content.ReadFromJsonAsync<ExamSessionDto>())!;
            Assert.AreEqual(_snapshotId, session.PaperSnapshotId);
            Assert.AreEqual(_questionId, session.Questions.Single().Id);
            Assert.AreEqual(10, session.Questions.Single().Score);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionReturnsLockedContentAndResumesAfterMembershipExpires(bool real)
    {
        await BindPublishedPaper(real, false, real ? "school" : "course", -1, 1);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.PaperSnapshots.Add(new PaperSnapshot { Id = _latestId, Title = "Newer version", Version = 2,
                MockPaperId = real ? null : _paperId, RealPaperId = real ? _paperId : null });
            db.QuestionSnapshots.Add(new QuestionSnapshot { Id = Guid.NewGuid(), PaperSnapshotId = _latestId,
                Content = "Unrelated newer question", StandardAnswer = "New secret", Score = 99 });
            (await db.Exams.SingleAsync(e => e.Id == _examId)).EndTime = DateTime.UtcNow.AddMinutes(20);
            await db.SaveChangesAsync();
        }
        Authenticate(_other);
        var response = await Http.PostAsync($"/api/Exams/{_examId}/start", null);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<ExamSessionDto>())!;
        Assert.AreEqual("Locked exam version", session.PaperTitle);
        Assert.AreEqual(_questionId, session.Questions.Single().Id);
        Assert.Contains("options", session.Questions.Single().Metadata);
        Assert.IsTrue(session.EndTime < session.StartTime.AddMinutes(30));
        var saved = await Http.PostAsJsonAsync($"/api/Exams/record/{session.RecordId}/answer",
            new SubmitAnswerDto { QuestionSnapshotId = _questionId, Answer = "Saved response" });
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.VipMemberships.SingleAsync(m => m.VipProductId == _productId)).EndTime = DateTime.UtcNow.AddSeconds(-1);
            // Later schedule edits must not replace an already-started attempt's version.
            (await db.Exams.SingleAsync(e => e.Id == _examId)).PaperSnapshotId = _latestId;
            await db.SaveChangesAsync();
        }
        response = await Http.PostAsync($"/api/Exams/{_examId}/start", null);
        var resumed = (await response.Content.ReadFromJsonAsync<ExamSessionDto>())!;
        Assert.AreEqual(session.RecordId, resumed.RecordId);
        Assert.AreEqual(_snapshotId, resumed.PaperSnapshotId);
        Assert.AreEqual("Saved response", resumed.Answers[_questionId]);
        Assert.AreEqual(_questionId, resumed.Questions.Single().Id);
        Assert.DoesNotContain("Confidential answer", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Unrelated newer question", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WithdrawnTemplateBlocksNewAdmissionButKeepsAdmittedAttempt(bool real)
    {
        await BindPublishedPaper(real, false, real ? "school" : "course", -1, 1);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            if (real) (await db.RealPapers.SingleAsync(p => p.Id == _paperId)).Status = PaperStatus.Draft;
            else (await db.MockPapers.SingleAsync(p => p.Id == _paperId)).Status = PaperStatus.Draft;
            await db.SaveChangesAsync();
        }
        Authenticate(_other);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
        Authenticate(_owner);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        Assert.AreEqual(1, await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().ExamRecords.CountAsync(r => r.ExamId == _examId));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("missing-student")]
    public async Task AdmissionRequiresAnExistingStudent(string userId)
    {
        using var scope = Server!.Services.CreateScope();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scope.ServiceProvider.GetRequiredService<IExamService>().StartExamAsync(_examId, userId));
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().ExamRecords.CountAsync(r => r.ExamId == _examId));
    }

    [TestMethod]
    public async Task OrphanAndUnconfiguredExamsCannotCreateNewAttempts()
    {
        Authenticate(_other);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.Exams.SingleAsync(e => e.Id == _examId)).PaperSnapshotId = null;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        Assert.AreEqual(1, await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().ExamRecords.CountAsync(r => r.ExamId == _examId));
    }

    [TestMethod]
    public async Task AnotherStudentCannotChangeOrReadAnAttempt()
    {
        Authenticate(_other);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Answer()).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.PostAsync($"/api/Exams/record/{_recordId}/submit", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.GetAsync($"/api/Exams/record/{_recordId}")).StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var record = await db.ExamRecords.SingleAsync(r => r.Id == _recordId);
        Assert.AreEqual(ExamRecordStatus.InProgress, record.Status);
        Assert.IsNull(record.SubmitTime);
        Assert.IsFalse(await db.AnswerRecords.AnyAsync(a => a.ExamRecordId == _recordId));
    }

    [TestMethod]
    public async Task AnonymousAndManagementCookieCannotOperateStudentAttempts()
    {
        foreach (var loggedIn in new[] { false, true })
        {
            if (loggedIn) await LoginAsAdmin();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await Answer()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.PostAsync($"/api/Exams/record/{_recordId}/submit", null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync($"/api/Exams/record/{_recordId}")).StatusCode);
        }
    }

    [TestMethod]
    public async Task HiddenExamCannotBeStartedOrResumedById()
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.Exams.SingleAsync(e => e.Id == _examId)).IsPublic = false;
            await db.SaveChangesAsync();
        }
        foreach (var user in new[] { _owner, _other })
        {
            Authenticate(user);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/api/Exams/{_examId}/start", null)).StatusCode);
            var exams = (await Http.GetFromJsonAsync<List<ExamDto>>("/api/Exams"))!;
            Assert.IsFalse(exams.Any(e => e.Id == _examId));
        }
        using var verify = Server!.Services.CreateScope();
        Assert.AreEqual(1, await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().ExamRecords.CountAsync(r => r.ExamId == _examId));
    }

    [TestMethod]
    public async Task OwnerResumesLockedVersionAndSavesOnlyItsQuestions()
    {
        Authenticate(_owner);
        Assert.AreEqual(HttpStatusCode.OK, (await Answer(value: "First answer")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Answer(value: "Updated answer")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Answer(Guid.NewGuid())).StatusCode);
        var response = await Http.PostAsync($"/api/Exams/{_examId}/start", null);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<ExamSessionDto>())!;
        Assert.AreEqual(_recordId, session.RecordId);
        Assert.AreEqual(_snapshotId, session.PaperSnapshotId);
        Assert.AreEqual("Locked exam version", session.PaperTitle);
        Assert.AreEqual(_questionId, session.Questions.Single().Id);
        Assert.AreEqual("Select the correct option", session.Questions.Single().Content);
        Assert.DoesNotContain("Confidential answer", await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Updated answer", session.Answers[_questionId]);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().AnswerRecords.CountAsync(a => a.ExamRecordId == _recordId));
    }


    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OwnerCannotWriteAfterExamOrSessionDeadline(bool examExpired)
    {
        Authenticate(_owner);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            if (examExpired)
                (await db.Exams.SingleAsync(e => e.Id == _examId)).EndTime = DateTime.UtcNow.AddMinutes(-5);
            else
                db.Entry(await db.ExamRecords.SingleAsync(r => r.Id == _recordId))
                    .Property(r => r.StartTime).CurrentValue = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Answer()).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/api/Exams/record/{_recordId}/submit", null)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await context.AnswerRecords.AnyAsync(a => a.ExamRecordId == _recordId));
        Assert.IsNull((await context.ExamRecords.SingleAsync(r => r.Id == _recordId)).SubmitTime);
    }

    [TestMethod]
    public async Task ServiceRejectsForeignAndMissingIdentitiesBeforeWriting()
    {
        using var scope = Server!.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamService>();
        foreach (var userId in new[] { _other.Id, string.Empty })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.SubmitAnswerAsync(_recordId, userId, _questionId, "Forged answer"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FinishExamAsync(_recordId, userId));
        }
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await db.AnswerRecords.AnyAsync(a => a.ExamRecordId == _recordId));
        Assert.AreEqual(ExamRecordStatus.InProgress, (await db.ExamRecords.SingleAsync(r => r.Id == _recordId)).Status);
    }

    [TestMethod]
    public async Task OwnerCanSubmitOnceAndCannotChangeAnswersAfterSubmission()
    {
        Authenticate(_owner);
        Assert.AreEqual(HttpStatusCode.OK, (await Answer(value: "Confidential answer")).StatusCode);
        var response = await Http.PostAsync($"/api/Exams/record/{_recordId}/submit", null);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Answer(value: "Changed after submit")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/api/Exams/record/{_recordId}/submit", null)).StatusCode);
        // The real queue grades this exact-match answer; no AI service is involved.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var scope = Server!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var record = await db.ExamRecords.SingleAsync(r => r.Id == _recordId);
            Assert.AreEqual(ExamRecordStatus.Submitted, record.Status);
            Assert.IsNotNull(record.SubmitTime);
            var answer = await db.AnswerRecords.SingleAsync(a => a.ExamRecordId == _recordId);
            Assert.AreEqual("Confidential answer", answer.UserAnswer);
            if (answer.IsMarked && record.TotalScore == 10) return;
            await Task.Delay(50);
        }
        Assert.Fail("The submitted exact-match answer was not graded.");
    }

    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, null)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task ReviewAndAnswerReleaseApplyToEveryFeedbackField(bool submitted, bool allowReview, bool? released)
    {
        Authenticate(_owner);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var exam = await db.Exams.SingleAsync(e => e.Id == _examId);
            exam.AllowReview = allowReview;
            exam.ShowAnswerAfter = released.HasValue ? (released.Value ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1)) : null;
            var record = await db.ExamRecords.SingleAsync(r => r.Id == _recordId);
            record.Status = submitted ? ExamRecordStatus.Submitted : ExamRecordStatus.InProgress;
            db.AnswerRecords.Add(new AnswerRecord
            {
                Id = Guid.NewGuid(), ExamRecordId = _recordId, QuestionSnapshotId = _questionId,
                UserAnswer = "Student response", Score = 5, IsMarked = true,
                GradingResult = "{\"Comment\":\"Confidential answer\"}"
            });
            await db.SaveChangesAsync();
        }
        var history = (await Http.GetFromJsonAsync<List<ExamRecordDto>>("/api/Exams/history"))!;
        Assert.AreEqual(submitted && allowReview, history.Single(r => r.Id == _recordId).HasReview);
        var response = await Http.GetAsync($"/api/Exams/record/{_recordId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        if (submitted && allowReview)
        {
            var details = (await response.Content.ReadFromJsonAsync<ExamRecordDetailDto>())!;
            Assert.AreEqual("Student response", details.Answers.Single().UserAnswer);
            Assert.AreEqual(_snapshotId, details.PaperSnapshotId);
            Assert.AreEqual("Locked exam version", details.PaperTitle);
            Assert.AreEqual(_questionId, details.Questions.Single().Id);
            Assert.AreEqual("Select the correct option", details.Questions.Single().Content);
            if (released == true)
            {
                Assert.AreEqual("Confidential answer", details.Answers.Single().StandardAnswer);
                Assert.Contains("Confidential answer", details.Answers.Single().GradingResult);
                Assert.Contains("Confidential answer", details.TeacherComment);
            }
            else
            {
                Assert.IsNull(details.Answers.Single().StandardAnswer);
                Assert.AreEqual(string.Empty, details.Answers.Single().GradingResult);
                Assert.AreEqual(string.Empty, details.TeacherComment);
            }
        }
        if (!(submitted && allowReview)) Assert.DoesNotContain("Select the correct option", json);
        if (!(submitted && allowReview && released == true)) Assert.DoesNotContain("Confidential answer", json);
    }
}
