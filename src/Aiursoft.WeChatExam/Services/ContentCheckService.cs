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
        var directories = await context.KnowledgeCategories.AsNoTracking().ToListAsync(cancellationToken);
        var courses = await context.TotalCategories.AsNoTracking().ToListAsync(cancellationToken);
        var periods = await context.QuestionCategories.AsNoTracking().ToListAsync(cancellationToken);
        var tags = await context.Tags.AsNoTracking().Include(t => t.Taxonomy)
            .ThenInclude(t => t!.CategoryTaxonomies).ToListAsync(cancellationToken);
        var questions = await context.Questions.AsNoTracking().Select(q => new Question
        {
            Id = q.Id, Content = q.Content, QuestionCategoryId = q.QuestionCategoryId
        }).ToListAsync(cancellationToken);
        var questionTags = await context.QuestionTags.AsNoTracking().ToListAsync(cancellationToken);
        var knowledge = await context.KnowledgePoints.AsNoTracking().Select(k => new KnowledgePoint
        {
            Id = k.Id, Title = k.Title, Content = ""
        }).ToListAsync(cancellationToken);
        var knowledgeQuestions = await context.KnowledgePointQuestions.AsNoTracking().ToListAsync(cancellationToken);
        var knowledgeCategories = await context.KnowledgeCategoryKnowledgePoints.AsNoTracking().ToListAsync(cancellationToken);
        var products = await context.VipProducts.AsNoTracking().Include(p => p.TotalCategory).ToListAsync(cancellationToken);
        var papers = await context.Papers.AsNoTracking().Include(p => p.PaperCategories)
            .Include(p => p.PaperSnapshots).AsSplitQuery().ToListAsync(cancellationToken);
        // Only IDs and counts are needed. A deleted or modified original question does not
        // invalidate the independent content retained in a question snapshot.
        var snapshots = await context.QuestionSnapshots.AsNoTracking()
            .Select(s => new { s.PaperSnapshotId, s.OriginalQuestionId }).ToListAsync(cancellationToken);
        var taxonomyCount = await context.Taxonomies.CountAsync(cancellationToken);

        var tagIndex = tags.ToDictionary(t => t.Id);
        var tagsByQuestion = questionTags.ToLookup(t => t.QuestionId);
        var periodIndex = periods.ToDictionary(p => p.Id);
        var questionIndex = questions.ToDictionary(q => q.Id, q => new Question
        {
            Id = q.Id, Content = q.Content, QuestionCategoryId = q.QuestionCategoryId,
            QuestionCategory = periodIndex[q.QuestionCategoryId],
            QuestionTags = tagsByQuestion[q.Id].Where(t => tagIndex.ContainsKey(t.TagId)).Select(t => new QuestionTag
            {
                QuestionId = q.Id, TagId = t.TagId, Tag = tagIndex[t.TagId]
            }).ToList()
        });
        var knowledgeIndex = knowledge.ToDictionary(k => k.Id);
        var directoryIndex = directories.ToDictionary(c => c.Id);
        var courseIndex = courses.ToDictionary(c => c.Id);
        var linkedKnowledge = knowledgeQuestions.ToLookup(k => k.QuestionId);
        var categoriesByKnowledge = knowledgeCategories.ToLookup(k => k.KnowledgePointId, k => k.KnowledgeCategoryId);
        var questionsByTag = questionTags.ToLookup(t => t.TagId, t => t.QuestionId);
        var snapshotQuestions = snapshots.ToLookup(s => s.PaperSnapshotId);
        var availableSubjects = products.Where(p => p.IsEnabled && p.Type == VipProductType.Category && p.TotalCategoryId.HasValue)
            .Select(p => p.TotalCategoryId!.Value).ToHashSet();
        var purchasable = new PaperAccessStatus
        {
            ActiveCategoryVips = availableSubjects,
            HasRealExamVip = products.Any(p => p.IsEnabled && p.Type == VipProductType.RealExam)
        };
        report.Categories = courses.OrderBy(c => c.Title).ToList();
        report.KnowledgePointCount = knowledge.Count;
        report.QuestionCount = questions.Count;
        report.PaperCount = papers.Count;
        report.Counts[localizer["Knowledge points"]] = knowledge.Count;
        report.Counts[localizer["Questions"]] = questions.Count;
        report.Counts[localizer["Tags"]] = tags.Count;
        report.Counts[localizer["Taxonomies"]] = taxonomyCount;
        report.Counts[localizer["Subjects"]] = courses.Count;
        report.Counts[localizer["Knowledge Categories"]] = directories.Count;
        report.Counts[localizer["Periods"]] = periods.Count;
        report.Counts[localizer["VIP products"]] = products.Count;
        report.Counts[localizer["Papers"]] = papers.Count;
        report.Counts[localizer["Paper snapshots"]] = papers.Sum(p => p.PaperSnapshots.Count());

        // VIPs belong to total categories, never tree directories or historical tag mappings.
        // Include courses without products: sales availability does not revoke existing VIPs.
        var allSubjects = courses.Select(c => c.Id).ToHashSet();
        var visitor = new AccessScenario(localizer["Anonymous"], new());
        var signedIn = new AccessScenario(localizer["Signed in without active VIP"], new());
        var courseScenarios = allSubjects.OrderBy(id => id).Select(subject => new AccessScenario(
            string.Format(localizer["Subject VIP: {0}"], courseIndex.GetValueOrDefault(subject)?.Title ?? subject.ToString()),
            new PaperAccessStatus { ActiveCategoryVips = [subject] })).ToList();
        var all = new PaperAccessStatus { ActiveCategoryVips = allSubjects, HasRealExamVip = true };
        var allVips = new AccessScenario(localizer["All relevant VIPs"], all);
        var custom = new AccessScenario(localizer["Custom VIP selection"],
            new() { ActiveCategoryVips = subjects.Where(allSubjects.Contains).ToHashSet(), HasRealExamVip = realExam });
        report.Scenarios.AddRange([visitor, signedIn]);
        report.Scenarios.AddRange(courseScenarios);
        report.Scenarios.Add(new(localizer["Real-exam VIP only"], new() { HasRealExamVip = true }));
        report.Scenarios.AddRange([allVips, custom]);

        ContentReference QuestionRef(Question q) => new("Questions", q.Id.ToString(), q.Content, AppPermissionNames.CanEditQuestions,
            q.QuestionCategory!.IsFree ? ContentPricing.Public : ContentPricing.Paid);
        ContentReference TagRef(Tag t) => new("Tags", t.Id.ToString(), t.DisplayName, AppPermissionNames.CanManageTags);
        ContentReference PaperRef(Paper p) => new("Papers", p.Id.ToString(), p.Title, AppPermissionNames.CanEditPapers,
            p.IsFree ? ContentPricing.Public : ContentPricing.Paid, p.Status == PaperStatus.Frozen ? "Details" : "Edit");
        ContentReference CourseRef(TotalCategory c) => new("TotalCategories", c.Id.ToString(), c.Title, AppPermissionNames.CanEditAnyCategory);
        ContentReference DirectoryRef(KnowledgeCategory c) => new("KnowledgeCategories", c.Id.ToString(), c.Title,
            AppPermissionNames.CanEditAnyCategory, c.IsFree ? ContentPricing.Public : ContentPricing.Paid);
        ContentReference PeriodRef(QuestionCategory c) => new("QuestionCategories", c.Id.ToString(), c.Title,
            AppPermissionNames.CanEditAnyCategory, c.IsFree ? ContentPricing.Public : ContentPricing.Paid);
        List<KnowledgeCategory> Placements(Guid id) => categoriesByKnowledge[id]
            .Where(directoryIndex.ContainsKey).Select(c => directoryIndex[c]).ToList();
        ContentReference ProductRef(VipProduct p) => new("VipProducts", p.Id.ToString(), p.Name,
            AppPermissionNames.CanEditVipProducts, Enabled: p.IsEnabled);
        string KnowledgePath(KnowledgePoint point)
        {
            // Content belongs to the category directory; knowledge points do not form a second tree.
            var paths = new List<string>();
            foreach (var categoryId in categoriesByKnowledge[point.Id])
            {
                var path = new List<string> { point.Title };
                var seen = new HashSet<Guid>();
                for (Guid? id = categoryId; id.HasValue && seen.Add(id.Value) && directoryIndex.TryGetValue(id.Value, out var category); id = category.ParentKnowledgeCategoryId)
                    path.Add(category.Title);
                if (directoryIndex.TryGetValue(categoryId, out var directory) && courseIndex.TryGetValue(directory.TotalCategoryId, out var course))
                    path.Add(course.Title);
                path.Reverse();
                paths.Add(string.Join(" → ", path));
            }
            return paths.Count == 0 ? point.Title : string.Join(" | ", paths.Order());
        }
        ContentReference KnowledgeRef(KnowledgePoint point) => new("KnowledgePoints", point.Id.ToString(),
            KnowledgePath(point), AppPermissionNames.CanEditAnyKnowledgePoint,
            Placements(point.Id).Any(c => c.IsFree) ? ContentPricing.Public :
            Placements(point.Id).Count > 0 ? ContentPricing.Paid : ContentPricing.Unspecified);

        foreach (var tag in tags)
        {
            var mapped = tag.Taxonomy?.CategoryTaxonomies.Select(c => c.CategoryId).ToHashSet() ?? [];
            report.TagMappings[tag.Id] = new(tag.Taxonomy is { } taxonomy
                ? new("Taxonomies", taxonomy.Id.ToString(), taxonomy.Name, AppPermissionNames.CanManageTags) : null,
                courses.Where(c => mapped.Contains(c.Id)).Select(CourseRef).ToList());
        }

        foreach (var q in questionIndex.Values)
        {
            var points = linkedKnowledge[q.Id].Where(k => knowledgeIndex.ContainsKey(k.KnowledgePointId))
                .Select(k => knowledgeIndex[k.KnowledgePointId]).DistinctBy(k => k.Id).ToList();
            var placements = points.SelectMany(k => Placements(k.Id)).DistinctBy(c => c.Id).ToList();
            var relevantSubjects = placements.Select(c => c.TotalCategoryId).Append(q.QuestionCategory!.TotalCategoryId).ToHashSet();
            var settings = q.QuestionTags.Select(t => TagRef(t.Tag)).ToList();
            settings.AddRange(q.QuestionTags.Where(t => t.Tag.Taxonomy != null).Select(t =>
                new ContentReference("Taxonomies", t.Tag.Taxonomy!.Id.ToString(), t.Tag.Taxonomy.Name, AppPermissionNames.CanManageTags)));
            settings.AddRange(placements.Select(DirectoryRef));
            settings.Add(PeriodRef(q.QuestionCategory!));
            settings.AddRange(courses.Where(c => relevantSubjects.Contains(c.Id)).Select(CourseRef));
            settings.AddRange(products.Where(p => p.Type == VipProductType.Category && p.TotalCategoryId.HasValue &&
                relevantSubjects.Contains(p.TotalCategoryId.Value)).Select(ProductRef));
            var scenarios = new[] { visitor, signedIn }.Concat(courseScenarios
                .Where(s => s.Status.ActiveCategoryVips.Overlaps(relevantSubjects)));
            if (subjects.Length > 0 || realExam) scenarios = scenarios.Append(custom);
            report.Exercises[q.Id] = new(QuestionRef(q), points.Select(KnowledgeRef).ToList(),
                CourseRef(courseIndex[q.QuestionCategory!.TotalCategoryId]), PeriodRef(q.QuestionCategory),
                settings.DistinctBy(s => (s.Controller, s.Id)).ToList(),
                scenarios.Select(s => new ExerciseAccess(s.Name, QuestionAccess.CanPractice(q.QuestionCategory, s.Status),
                    points.Select(k => new KnowledgeContentAccess(KnowledgeRef(k),
                        Placements(k.Id).Any(c => KnowledgeAccess.CanRead(c, s.Status)))).ToList())).ToList());
        }
        void AttachQuestion(ContentFinding finding, Question q)
        {
            finding.Objects.Add(QuestionRef(q));
            finding.Objects.Add(PeriodRef(q.QuestionCategory!));
            finding.CategoryIds.Add(q.QuestionCategory!.TotalCategoryId);
            foreach (var link in linkedKnowledge[q.Id])
            {
                if (!knowledgeIndex.TryGetValue(link.KnowledgePointId, out var point)) continue;
                finding.Objects.Add(KnowledgeRef(point));
                finding.Objects.AddRange(Placements(point.Id).Select(DirectoryRef));
                finding.CategoryIds.UnionWith(Placements(point.Id).Select(c => c.TotalCategoryId));
            }
        }
        void ObserveQuestion(ContentFinding finding, Question q)
        {
            foreach (var scenario in report.Scenarios)
            {
                var allowed = QuestionAccess.CanPractice(q.QuestionCategory!, scenario.Status);
                finding.Observations.Add(new(scenario.Name, $"GET /api/Questions/{q.Id}", allowed,
                    allowed ? localizer["Question content is returned (200)."] : localizer["The question's subject VIP is required (403)."]));
                foreach (var pointId in linkedKnowledge[q.Id].Select(k => k.KnowledgePointId).Distinct())
                    finding.Observations.Add(new(scenario.Name, $"GET /api/KnowledgePoints/{pointId}",
                        Placements(pointId).Any(c => KnowledgeAccess.CanRead(c, scenario.Status)),
                        localizer["A lesson can be read through any directly assigned directory that grants access. Exercise access is checked separately."]));
            }
        }
        ContentFinding Add(string rule, CheckSeverity severity, string title, string reason, string suggestion, bool engineering = false)
        {
            var finding = new ContentFinding { Rule = rule, Severity = severity, Title = title, Reason = reason, Suggestion = suggestion, NeedsEngineering = engineering };
            report.Findings.Add(finding);
            return finding;
        }

        foreach (var tag in tags.Where(t => !t.IsFree && t.Taxonomy != null))
        {
            var mapped = tag.Taxonomy?.CategoryTaxonomies.Select(c => c.CategoryId).ToArray() ?? [];
            ContentFinding? finding = null;
            if (!QuestionAccess.CanUseTag(tag, all))
                finding = Add("TAG001", CheckSeverity.Error, localizer["Paid tag has no unlockable subject"],
                    localizer["The legacy taxonomy API reports this tag as locked for every course VIP. This does not control current question or knowledge access."],
                    localizer["Ask engineering to review remaining legacy navigation consumers. Do not change period or directory pricing to fix legacy tag metadata."], true);
            else if (!QuestionAccess.CanUseTag(tag, purchasable))
                finding = Add("VIP001", CheckSeverity.Warning, localizer["No enabled VIP product unlocks this tag"],
                    localizer["New customers have no current purchase route. Existing active memberships may still grant access."],
                    localizer["Ask engineering to review remaining legacy navigation consumers. Do not change period or directory pricing to fix legacy tag metadata."], true);
            if (finding == null) continue;
            finding.Objects.Add(TagRef(tag));
            if (tag.Taxonomy is { } taxonomy)
                finding.Objects.Add(new("Taxonomies", taxonomy.Id.ToString(), taxonomy.Name, AppPermissionNames.CanManageTags));
            finding.CategoryIds.UnionWith(mapped.Where(allSubjects.Contains));
            foreach (var id in questionsByTag[tag.Id].Distinct())
                if (questionIndex.TryGetValue(id, out var q)) AttachQuestion(finding, q);
            foreach (var scenario in report.Scenarios)
                finding.Observations.Add(new(scenario.Name, $"GET /api/miniprogramapi/taxonomies/{tag.TaxonomyId}/tags",
                    QuestionAccess.CanUseTag(tag, scenario.Status), localizer["Legacy tag metadata only: HasAccess is reported without changing question access."]));
        }

        // Shared impact is indexed once per tag, not copied into every question finding.
        // This keeps scans linear in associations when hundreds of questions share a tag.
        foreach (var tag in tags)
        {
            var impact = new List<ContentReference>();
            foreach (var id in questionsByTag[tag.Id].Distinct())
            {
                if (!questionIndex.TryGetValue(id, out var question)) continue;
                impact.Add(QuestionRef(question));
                impact.Add(CourseRef(courseIndex[question.QuestionCategory!.TotalCategoryId]));
                foreach (var link in linkedKnowledge[id])
                {
                    if (knowledgeIndex.TryGetValue(link.KnowledgePointId, out var point))
                        impact.Add(KnowledgeRef(point));
                    foreach (var categoryId in categoriesByKnowledge[link.KnowledgePointId])
                        if (directoryIndex.TryGetValue(categoryId, out var linkedCategory)) impact.Add(DirectoryRef(linkedCategory));
                }
            }
            report.TagImpacts[tag.Id] = impact.DistinctBy(o => (o.Controller, o.Id)).ToList();
        }

        foreach (var period in periods)
            report.PeriodImpacts[period.Id] = questionIndex.Values.Where(q => q.QuestionCategoryId == period.Id)
                .SelectMany(q => new[] { QuestionRef(q) }.Concat(report.Exercises[q.Id].KnowledgePoints))
                .DistinctBy(o => (o.Controller, o.Id)).ToList();

        foreach (var q in questionIndex.Values.Where(q => !q.QuestionCategory!.IsFree))
        {
            void Detail(ContentFinding finding)
            {
                finding.QuestionSubjectId = q.QuestionCategory!.TotalCategoryId;
                AttachQuestion(finding, q);
                ObserveQuestion(finding, q);
            }
            var placements = linkedKnowledge[q.Id].SelectMany(k => Placements(k.KnowledgePointId)).ToList();
            if (placements.Any(c => c.IsFree))
                Detail(Add("KP001", CheckSeverity.Error, localizer["Free knowledge directory links to a paid exercise"],
                    localizer["Students can read a lesson in a free directory, but its linked exercise belongs to a paid period. This conflicts with the free-lesson rule."],
                    localizer["Choose a free exercise for this lesson, or review its directory and period pricing. Changing a period affects every question in it."]));
            if (placements.Any(c => !c.IsFree && c.TotalCategoryId != q.QuestionCategory!.TotalCategoryId))
                Detail(Add("KP002", CheckSeverity.Warning, localizer["Knowledge point and exercise reference different subjects"],
                    localizer["A membership for a linked knowledge subject does not necessarily unlock the exercise's own subject."],
                    localizer["Confirm whether this is intentional cross-subject teaching; otherwise correct the question period or the knowledge association."]));
            if (!QuestionAccess.CanPractice(q.QuestionCategory!, purchasable))
                Detail(Add("VIP002", CheckSeverity.Warning, localizer["No enabled product combination unlocks this question"],
                    localizer["Existing active VIPs can still work, but buying currently enabled products cannot unlock this question."],
                    localizer["Review the course and available VIP products. Disabled products do not invalidate existing active memberships."]));
        }
        foreach (var point in knowledge.Where(k => Placements(k.Id).Count == 0))
        {
            var finding = Add("KP003", CheckSeverity.Error, localizer["Knowledge point has no directory"],
                localizer["This lesson has no directory granting access, so even a course VIP cannot read it."],
                localizer["Assign the lesson to its intended knowledge directory."]);
            finding.Objects.Add(KnowledgeRef(point));
        }
        foreach (var product in products.Where(p => p.Type == VipProductType.Category &&
                     (!p.TotalCategoryId.HasValue || p.TotalCategory == null)))
        {
            var finding = Add("VIP003", CheckSeverity.Error, localizer["Subject VIP product has no valid subject"],
                localizer["This product cannot grant the intended subject membership."],
                localizer["Assign the intended subject before offering this product for sale. Review existing memberships separately."]);
            finding.Objects.Add(ProductRef(product));
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
                    .Cast<Question>().Where(q => !QuestionAccess.CanPractice(q.QuestionCategory!, new())).DistinctBy(q => q.Id).ToList();
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
        // Deduplicate shared impact references. Counts describe the whole finding, not the
        // first example or the number of links, and are unaffected by report pagination.
        foreach (var finding in report.Findings)
        {
            foreach (var id in finding.CategoryIds)
                if (courseIndex.TryGetValue(id, out var course)) finding.Objects.Add(CourseRef(course));
            var distinct = finding.Objects.DistinctBy(o => (o.Controller, o.Id)).ToList();
            finding.Objects.Clear();
            finding.Objects.AddRange(distinct);
        }
        report.CompletedAt = DateTime.UtcNow;
        return report;
    }
}
