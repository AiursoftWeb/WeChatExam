using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ContentChecksViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Read-only diagnostics over one bounded batch of content queries. No user records,
/// payment calls, practice sessions or grading are needed to evaluate synthetic VIPs.
/// The scan is a time window, not a transactionally consistent database snapshot.
/// </summary>
public class ContentCheckService(
    WeChatExamDbContext context,
    IStringLocalizer<ContentCheckService> localizer)
{
    public async Task<ContentCheckReport> ScanAsync(Guid[] subjects, bool realExam, CancellationToken cancellationToken = default)
    {
        var report = new ContentCheckReport();
        var categories = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var tags = await context.Tags.AsNoTracking().Include(t => t.Taxonomy)
            .ThenInclude(t => t!.CategoryTaxonomies).ToListAsync(cancellationToken);
        var questions = await context.Questions.AsNoTracking().Select(q => new Question
        {
            Id = q.Id, Content = q.Content, CategoryId = q.CategoryId
        }).ToListAsync(cancellationToken);
        var questionTags = await context.QuestionTags.AsNoTracking().ToListAsync(cancellationToken);
        var knowledge = await context.KnowledgePoints.AsNoTracking().Select(k => new KnowledgePoint
        {
            Id = k.Id, Title = k.Title, Content = "", ParentId = k.ParentId
        }).ToListAsync(cancellationToken);
        var knowledgeQuestions = await context.KnowledgePointQuestions.AsNoTracking().ToListAsync(cancellationToken);
        var knowledgeCategories = await context.CategoryKnowledgePoints.AsNoTracking().ToListAsync(cancellationToken);
        var products = await context.VipProducts.AsNoTracking().ToListAsync(cancellationToken);
        var papers = await context.Papers.AsNoTracking().Include(p => p.PaperCategories)
            .Include(p => p.PaperSnapshots).AsSplitQuery().ToListAsync(cancellationToken);
        // Only IDs and counts are needed. A deleted or modified original question does not
        // invalidate the independent content retained in a question snapshot.
        var snapshots = await context.QuestionSnapshots.AsNoTracking()
            .Select(s => new { s.PaperSnapshotId, s.OriginalQuestionId }).ToListAsync(cancellationToken);
        var taxonomyCount = await context.Taxonomies.CountAsync(cancellationToken);

        var tagIndex = tags.ToDictionary(t => t.Id);
        var tagsByQuestion = questionTags.ToLookup(t => t.QuestionId);
        var questionIndex = questions.ToDictionary(q => q.Id, q => new Question
        {
            Id = q.Id, Content = q.Content, CategoryId = q.CategoryId,
            QuestionTags = tagsByQuestion[q.Id].Where(t => tagIndex.ContainsKey(t.TagId)).Select(t => new QuestionTag
            {
                QuestionId = q.Id, TagId = t.TagId, Tag = tagIndex[t.TagId]
            }).ToList()
        });
        var knowledgeIndex = knowledge.ToDictionary(k => k.Id);
        var linkedKnowledge = knowledgeQuestions.ToLookup(k => k.QuestionId);
        var categoriesByKnowledge = knowledgeCategories.ToLookup(k => k.KnowledgePointId, k => k.CategoryId);
        var questionsByTag = questionTags.ToLookup(t => t.TagId, t => t.QuestionId);
        var snapshotQuestions = snapshots.ToLookup(s => s.PaperSnapshotId);
        var availableSubjects = products.Where(p => p.IsEnabled && p.Type == VipProductType.Category && p.CategoryId.HasValue)
            .Select(p => p.CategoryId!.Value).ToHashSet();
        var purchasable = new PaperAccessStatus
        {
            ActiveCategoryVips = availableSubjects,
            HasRealExamVip = products.Any(p => p.IsEnabled && p.Type == VipProductType.RealExam)
        };
        report.Categories = categories.OrderBy(c => c.Title).ToList();
        report.KnowledgePointCount = knowledge.Count;
        report.QuestionCount = questions.Count;
        report.PaperCount = papers.Count;
        report.Counts[localizer["Knowledge points"]] = knowledge.Count;
        report.Counts[localizer["Questions"]] = questions.Count;
        report.Counts[localizer["Tags"]] = tags.Count;
        report.Counts[localizer["Taxonomies"]] = taxonomyCount;
        report.Counts[localizer["Subjects"]] = categories.Count;
        report.Counts[localizer["VIP products"]] = products.Count;
        report.Counts[localizer["Papers"]] = papers.Count;
        report.Counts[localizer["Paper snapshots"]] = papers.Sum(p => p.PaperSnapshots.Count());

        // Include referenced subjects even without a sellable product. Otherwise an absent
        // purchase route would incorrectly look like an impossible authorization rule.
        var allSubjects = categories.Select(c => c.Id)
            .Concat(questions.Where(q => q.CategoryId.HasValue).Select(q => q.CategoryId!.Value))
            .Concat(tags.SelectMany(t => t.Taxonomy?.CategoryTaxonomies.Select(c => c.CategoryId) ?? []))
            .Concat(papers.SelectMany(p => p.PaperCategories.Select(c => c.CategoryId))).ToHashSet();
        report.Scenarios.Add(new(localizer["Anonymous"], false, new()));
        report.Scenarios.Add(new(localizer["Signed in without active VIP"], true, new()));
        foreach (var subject in allSubjects.OrderBy(id => id))
            report.Scenarios.Add(new(string.Format(localizer["Subject VIP: {0}"], categories.FirstOrDefault(c => c.Id == subject)?.Title ?? subject.ToString()), true,
                new PaperAccessStatus { ActiveCategoryVips = [subject] }));
        report.Scenarios.Add(new(localizer["Real-exam VIP only"], true, new() { HasRealExamVip = true }));
        var all = new PaperAccessStatus { ActiveCategoryVips = allSubjects, HasRealExamVip = true };
        report.Scenarios.Add(new(localizer["All relevant VIPs"], true, all));
        report.Scenarios.Add(new(localizer["Custom VIP selection"], true,
            new() { ActiveCategoryVips = subjects.Where(allSubjects.Contains).ToHashSet(), HasRealExamVip = realExam }));

        ContentReference QuestionRef(Question q) => new("Questions", q.Id.ToString(), q.Content, AppPermissionNames.CanEditQuestions);
        ContentReference TagRef(Tag t) => new("Tags", t.Id.ToString(), t.DisplayName, AppPermissionNames.CanManageTags);
        ContentReference PaperRef(Paper p) => new("Papers", p.Id.ToString(), p.Title, AppPermissionNames.CanEditPapers);
        ContentReference CategoryRef(Category c) => new("Categories", c.Id.ToString(), c.Title, AppPermissionNames.CanEditAnyCategory);
        string KnowledgePath(KnowledgePoint point)
        {
            var path = new List<string>();
            var seen = new HashSet<Guid>();
            for (var current = point; seen.Add(current.Id);)
            {
                path.Add(current.Title);
                if (current.ParentId is not { } parent || !knowledgeIndex.TryGetValue(parent, out current)) break;
            }
            path.Reverse();
            return string.Join(" → ", path);
        }
        void AttachQuestion(ContentFinding finding, Question q)
        {
            finding.Objects.Add(QuestionRef(q));
            if (q.CategoryId is { } category) finding.CategoryIds.Add(category);
            foreach (var link in linkedKnowledge[q.Id])
            {
                if (!knowledgeIndex.TryGetValue(link.KnowledgePointId, out var point)) continue;
                finding.Objects.Add(new("KnowledgePoints", point.Id.ToString(), KnowledgePath(point), AppPermissionNames.CanEditAnyKnowledgePoint));
                finding.CategoryIds.UnionWith(categoriesByKnowledge[point.Id]);
            }
        }
        void ObserveQuestion(ContentFinding finding, Question q)
        {
            foreach (var scenario in report.Scenarios)
            {
                var decision = QuestionAccess.Evaluate(q, scenario.Authenticated, scenario.Status);
                finding.Observations.Add(new(scenario.Name, $"GET /api/Questions/{q.Id}", decision.Allowed,
                    decision.Denial switch
                    {
                        QuestionAccessDenial.None => localizer["Question content is returned (200)."],
                        QuestionAccessDenial.Anonymous => localizer["Paid question requires sign-in (403)."],
                        QuestionAccessDenial.Subject => localizer["The question's subject VIP is required (403)."],
                        _ => string.Format(localizer["Paid tag denies access (403): {0}"], decision.BlockingTag!.DisplayName)
                    }));
            }
        }
        ContentFinding Add(string rule, CheckSeverity severity, string title, string reason, string suggestion, bool engineering = false)
        {
            var finding = new ContentFinding { Rule = rule, Severity = severity, Title = title, Reason = reason, Suggestion = suggestion, NeedsEngineering = engineering };
            report.Findings.Add(finding);
            return finding;
        }

        foreach (var tag in tags.Where(t => !t.IsFree))
        {
            var mapped = tag.Taxonomy?.CategoryTaxonomies.Select(c => c.CategoryId).ToArray() ?? [];
            ContentFinding? finding = null;
            if (!QuestionAccess.CanUseTag(tag, all))
                finding = Add("TAG001", CheckSeverity.Error, localizer["Paid tag has no unlockable subject"],
                    localizer["No VIP can unlock this tag entry. A question with its own subject may still be accessible through question details."],
                    localizer["If this is a descriptive shared tag, consider making it free. If it sells access, map its taxonomy to the intended subject. Review the affected content before changing a shared tag."]);
            else if (!QuestionAccess.CanUseTag(tag, purchasable))
                finding = Add("VIP001", CheckSeverity.Warning, localizer["No enabled VIP product unlocks this tag"],
                    localizer["New customers have no current purchase route. Existing active memberships may still grant access."],
                    localizer["Confirm whether sales are intentionally paused; otherwise enable a product for one of the required subjects."]);
            if (finding == null) continue;
            finding.Objects.Add(TagRef(tag));
            if (tag.Taxonomy is { } taxonomy)
                finding.Objects.Add(new("Taxonomies", taxonomy.Id.ToString(), taxonomy.Name, AppPermissionNames.CanManageTags));
            finding.CategoryIds.UnionWith(mapped);
            foreach (var id in questionsByTag[tag.Id].Distinct())
                if (questionIndex.TryGetValue(id, out var q)) AttachQuestion(finding, q);
            foreach (var scenario in report.Scenarios)
                finding.Observations.Add(new(scenario.Name, $"GET /api/Questions?tagName={Uri.EscapeDataString(tag.DisplayName)}",
                    QuestionAccess.CanUseTag(tag, scenario.Status), localizer["Tag gate only; anonymous lists also exclude every question with a paid tag."]));
        }

        // Shared impact is indexed once per tag, not copied into every question finding.
        // This keeps scans linear in associations when hundreds of questions share a tag.
        foreach (var tag in tags.Where(t => !t.IsFree))
        {
            var impact = new List<ContentReference>();
            foreach (var id in questionsByTag[tag.Id].Distinct())
            {
                if (!questionIndex.TryGetValue(id, out var question)) continue;
                impact.Add(QuestionRef(question));
                if (categories.FirstOrDefault(c => c.Id == question.CategoryId) is { } category) impact.Add(CategoryRef(category));
                foreach (var link in linkedKnowledge[id])
                {
                    if (knowledgeIndex.TryGetValue(link.KnowledgePointId, out var point))
                        impact.Add(new("KnowledgePoints", point.Id.ToString(), KnowledgePath(point), AppPermissionNames.CanEditAnyKnowledgePoint));
                    foreach (var categoryId in categoriesByKnowledge[link.KnowledgePointId])
                        if (categories.FirstOrDefault(c => c.Id == categoryId) is { } linkedCategory) impact.Add(CategoryRef(linkedCategory));
                }
            }
            report.TagImpacts[tag.Id] = impact.DistinctBy(o => (o.Controller, o.Id)).ToList();
        }

        ContentFinding? listDifference = null;
        foreach (var q in questionIndex.Values)
        {
            var paidTags = q.QuestionTags.Select(t => t.Tag).Where(t => !t.IsFree).ToList();
            if (paidTags.Count == 0) continue;
            void Detail(ContentFinding finding)
            {
                finding.QuestionSubjectId = q.CategoryId;
                AttachQuestion(finding, q);
                finding.Objects.AddRange(paidTags.Select(TagRef));
                foreach (var tag in paidTags)
                {
                    if (tag.Taxonomy is { } taxonomy)
                        finding.Objects.Add(new("Taxonomies", taxonomy.Id.ToString(), taxonomy.Name, AppPermissionNames.CanManageTags));
                }
                ObserveQuestion(finding, q);
            }
            if (linkedKnowledge[q.Id].Any())
                Detail(Add("KP001", CheckSeverity.Information, localizer["Public knowledge point links to a paid exercise"],
                    localizer["Visitors can read the knowledge content, but this exercise returns 403 anonymously. Paid exercises are allowed by the current business rules."],
                    localizer["Keep this configuration if the exercise is intentionally paid. If it should be public, review all paid tags and their shared impact first."]));
            var knowledgeSubjects = linkedKnowledge[q.Id].SelectMany(k => categoriesByKnowledge[k.KnowledgePointId]).ToHashSet();
            if (q.CategoryId is { } own && knowledgeSubjects.Any(id => id != own))
                Detail(Add("KP002", CheckSeverity.Warning, localizer["Knowledge point and exercise reference different subjects"],
                    localizer["A membership for a linked knowledge subject does not necessarily unlock the exercise's own subject."],
                    localizer["Confirm whether this is intentional cross-subject teaching; otherwise correct the question subject or the knowledge association."]));
            if (!QuestionAccess.Evaluate(q, true, all).Allowed)
                Detail(Add("Q001", CheckSeverity.Error, localizer["Paid question cannot be unlocked even with all VIPs"],
                    localizer["This uncategorized question requires every paid tag; at least one tag has no subject mapping."],
                    localizer["Assign the intended question subject, or correct its paid tags after reviewing their shared impact."]));
            else if (!QuestionAccess.Evaluate(q, true, purchasable).Allowed)
                Detail(Add("VIP002", CheckSeverity.Warning, localizer["No enabled product combination unlocks this question"],
                    localizer["Existing active VIPs can still work, but buying currently enabled products cannot unlock this question."],
                    localizer["Review the subject and available VIP products. Disabled products do not invalidate existing active memberships."]));
            if (q.CategoryId is { } subjectId && paidTags.Any(t => !QuestionAccess.CanUseTag(t,
                    new PaperAccessStatus { ActiveCategoryVips = [subjectId] })))
                Detail(Add("Q002", CheckSeverity.Warning, localizer["Subject VIP and paid tag entry disagree"],
                    localizer["The question's subject VIP unlocks its details, but at least one paid tag entry rejects that same VIP."],
                    localizer["Check the tag taxonomy mappings and question subject. If both are intentional, ask engineering to review entry-point behavior."]));
            // This is existing endpoint behavior, not a data edit recommendation. Keep it
            // visible separately rather than silently changing authorization during a scan.
            listDifference ??= Add("API001", CheckSeverity.Warning, localizer["Signed-in list and question details use different access rules"],
                localizer["A list query without a tag filter can return this paid question to a signed-in user without VIP; its detail endpoint returns 403."],
                localizer["Engineering must review the endpoint rules. Do not change shared content pricing merely to suppress this diagnostic."], true);
            AttachQuestion(listDifference, q);
            if (listDifference.Observations.Count == 0)
            {
                listDifference.Observations.Add(new(localizer["Signed in without active VIP"], "GET /api/Questions?size=50", true,
                localizer["Eligible for the list; pagination and other query filters still apply."]));
                listDifference.Observations.Add(new(localizer["Signed in without active VIP"], "GET /api/Questions/{id}", false,
                    localizer["Question details return 403."]));
            }
        }

        foreach (var product in products.Where(p => p.Type == VipProductType.Category &&
                     (!p.CategoryId.HasValue || !categories.Any(c => c.Id == p.CategoryId.Value))))
        {
            var finding = Add("VIP003", CheckSeverity.Error, localizer["Subject VIP product has no valid subject"],
                localizer["This product cannot grant the intended subject membership."],
                localizer["Assign the intended subject before offering this product for sale. Review existing memberships separately."]);
            finding.Objects.Add(new("VipProducts", product.Id.ToString(), product.Name, AppPermissionNames.CanEditVipProducts));
        }
        foreach (var paper in papers.Where(p => p.Status is PaperStatus.Publishable or PaperStatus.Frozen))
        {
            void Detail(ContentFinding finding, Guid? snapshotId = null)
            {
                finding.Objects.Add(PaperRef(paper));
                finding.CategoryIds.UnionWith(paper.PaperCategories.Select(c => c.CategoryId));
                foreach (var scenario in report.Scenarios)
                    finding.Observations.Add(new(scenario.Name, $"GET /api/Papers/snapshots/{snapshotId?.ToString() ?? "{snapshotId}"}", PaperAccessService.CanAccess(paper, scenario.Status),
                        localizer["Authorization only: a denied snapshot returns 200 with HasAccess=false and no questions. A usable snapshot is also required."]));
            }
            var latest = paper.PaperSnapshots.OrderByDescending(s => s.Version).FirstOrDefault();
            if (latest == null)
                Detail(Add("PAPER001", CheckSeverity.Error, localizer["Publishable paper has no snapshot"],
                    localizer["The public paper list excludes this paper because no snapshot exists."],
                    localizer["Review its questions and create a snapshot through the existing paper management workflow."]));
            foreach (var snapshot in paper.PaperSnapshots.Where(s => !snapshotQuestions[s.Id].Any()))
                Detail(Add("PAPER002", CheckSeverity.Error, localizer["Paper snapshot is empty"],
                    string.Format(localizer["Snapshot version {0} contains no exercises. Old snapshot links can still be requested."], snapshot.Version),
                    localizer["Review the paper questions and publish a corrected snapshot. Changing original questions does not update existing snapshots."]), snapshot.Id);
            if (!PaperAccessService.CanAccess(paper, all))
                Detail(Add("PAPER003", CheckSeverity.Error, localizer["Paid paper has no unlockable subject"],
                    localizer["Even all relevant VIPs cannot unlock this paper."],
                    localizer["Review its subject associations, real-exam setting and free setting."]));
            else if (!PaperAccessService.CanAccess(paper, purchasable))
                Detail(Add("VIP004", CheckSeverity.Warning, localizer["No enabled product unlocks this paper"],
                    localizer["Existing active memberships may work, but new customers have no purchase route."],
                    localizer["Confirm whether sales are intentionally paused; otherwise enable the required subject or real-exam product."]));
            if (paper.IsFree && latest != null)
            {
                var originals = snapshotQuestions[latest.Id].Where(s => s.OriginalQuestionId.HasValue)
                    .Select(s => questionIndex.GetValueOrDefault(s.OriginalQuestionId!.Value)).Where(q => q != null)
                    .Cast<Question>().Where(q => !QuestionAccess.Evaluate(q, false, new()).Allowed).DistinctBy(q => q.Id).ToList();
                if (originals.Count > 0)
                {
                    var finding = Add("PAPER004", CheckSeverity.Information, localizer["Free paper contains snapshots of currently paid questions"],
                        localizer["Paper access is independent of original question access. The retained snapshot remains usable even if an original changes or is deleted."],
                        localizer["Keep this if the free paper is intentional. Review the paper's own pricing if its snapshot should be restricted."]);
                    Detail(finding);
                    foreach (var original in originals) AttachQuestion(finding, original);
                }
            }
        }
        if (categories.Any(c => !c.IsFree))
        {
            var finding = Add("CLIENT001", CheckSeverity.Warning, localizer["Client entry checks require engineering verification"],
                localizer["Category free settings do not restrict the public knowledge API. Some Mini Program entry checks use product names rather than the server's subject IDs."],
                localizer["Verify the deployed Mini Program separately. This server scan does not simulate client navigation or prove that a customer can enter every screen."], true);
            foreach (var category in categories.Where(c => !c.IsFree))
            {
                finding.Objects.Add(CategoryRef(category));
                finding.CategoryIds.Add(category.Id);
            }
        }
        // Deduplicate shared impact references. Counts describe the whole finding, not the
        // first example or the number of links, and are unaffected by report pagination.
        foreach (var finding in report.Findings)
        {
            foreach (var id in finding.CategoryIds)
                if (categories.FirstOrDefault(c => c.Id == id) is { } category) finding.Objects.Add(CategoryRef(category));
            var distinct = finding.Objects.DistinctBy(o => (o.Controller, o.Id)).ToList();
            finding.Objects.Clear();
            finding.Objects.AddRange(distinct);
        }
        report.CompletedAt = DateTime.UtcNow;
        return report;
    }
}
