using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanEditQuestions)]
[LimitPerMin]
public class AiTasksController(
    AiTaskService aiTaskService,
    BackgroundJobQueue backgroundJobQueue,
    WeChatExamDbContext dbContext,
    ITagService tagService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateExplanations([FromBody] Guid[] questionIds)
    {
        if (!questionIds.Any())
        {
            return BadRequest("No questions selected.");
        }

        var questions = await dbContext.Questions
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync();

        var taskItems = questions.Select(q => new AiTaskItem
        {
            QuestionId = q.Id,
            QuestionContent = q.Content,
            QuestionStandardAnswer = q.StandardAnswer,
            OldValue = q.Explanation,
            Status = AiTaskStatus.Pending
        }).ToList();

        var aiTask = aiTaskService.CreateTask(taskItems, AiTaskType.GenerateExplanation);

        // Start background processing
        foreach (var item in aiTask.Items.Values)
        {
            backgroundJobQueue.QueueWithDependency<IServiceProvider>(
                queueName: "AI-Explanation-Generation",
                jobName: $"Generate Explanation for Question {item.QuestionId}",
                job: async (serviceProvider) =>
                {
                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        
                        var question = await scopedDbContext.Questions
                            .Include(q => q.Category)
                            .Include(q => q.QuestionTags)
                            .ThenInclude(qt => qt.Tag)
                            .FirstOrDefaultAsync(q => q.Id == item.QuestionId);
                        if (question == null) 
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "Question not found in database.";
                            return;
                        }

                        var tags = string.Join(", ", question.QuestionTags.Select(qt => qt.Tag.DisplayName));
                        var category = question.Category?.Title ?? "未分类";
                        var type = question.QuestionType.GetDisplayName();
                        
                        var promptBuilder = new StringBuilder();
                        promptBuilder.AppendLine($"题目类型: {type}");
                        promptBuilder.AppendLine($"题目分类: {category}");
                        if (!string.IsNullOrEmpty(tags))
                        {
                            promptBuilder.AppendLine($"题目标签: {tags}");
                        }
                        promptBuilder.AppendLine($"题目内容: {question.Content}");
                        
                        if (!string.IsNullOrWhiteSpace(question.Metadata))
                        {
                            try 
                            {
                                var metadataObj = JsonConvert.DeserializeObject<dynamic>(question.Metadata);
                                if (metadataObj?.options != null)
                                {
                                    promptBuilder.AppendLine("选项:");
                                    foreach (var option in metadataObj.options)
                                    {
                                        promptBuilder.AppendLine($"- {option}");
                                    }
                                }
                                else 
                                {
                                    promptBuilder.AppendLine($"元数据: {question.Metadata}");
                                }
                            }
                            catch
                            {
                                promptBuilder.AppendLine($"元数据: {question.Metadata}");
                            }
                        }
                        
                        promptBuilder.AppendLine($"标准答案: {question.StandardAnswer}");
                        promptBuilder.AppendLine();

                        if (question.QuestionType == QuestionType.Choice || 
                            question.QuestionType == QuestionType.Blank || 
                            question.QuestionType == QuestionType.Bool ||
                            question.QuestionType == QuestionType.NounExplanation)
                        {
                            promptBuilder.AppendLine("指令: 这是一个基础题目。请提供 200 字以内的解析，详细解释该题目的背景知识、逻辑推理以及答案的正确性。直接输出解析内容，不要包含题目本身，不要输出多余的段落、前言或总结语。");
                        }
                        else
                        {
                            promptBuilder.AppendLine("指令: 这是一个深度题目。请提供 1000 字左右的详细解析，深入探讨该题目涉及的背景材料、核心考点、答题思路以及逻辑框架。直接输出解析内容，不要包含题目本身，不要输出多余的段落、前言或总结语。");
                        }

                        var prompt = promptBuilder.ToString();

                        var response = await ollamaService.AskQuestion(prompt);
                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            item.NewValue = response.Trim();
                            item.Status = AiTaskStatus.Completed;
                        }
                        else
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "AI returned an empty response.";
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = ex.Message;
                    }
                });
        }

        return Json(new { taskId = aiTask.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoCategorize([FromBody] Guid[] questionIds)
    {
        if (!questionIds.Any())
        {
            return BadRequest("No questions selected.");
        }

        var questions = await dbContext.Questions
            .Include(q => q.Category)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync();
            
        var taskItems = questions.Select(q => new AiTaskItem
        {
            QuestionId = q.Id,
            QuestionContent = q.Content,
            QuestionStandardAnswer = q.StandardAnswer,
            OldValue = q.Category?.Title ?? "Uncategorized",
            Status = AiTaskStatus.Pending
        }).ToList();

        var aiTask = aiTaskService.CreateTask(taskItems, AiTaskType.AutoCategorize);

        // Start background processing
        foreach (var item in aiTask.Items.Values)
        {
            backgroundJobQueue.QueueWithDependency<IServiceProvider>(
                queueName: "AI-Categorization",
                jobName: $"Categorize Question {item.QuestionId}",
                job: async (serviceProvider) =>
                {
                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        
                        var question = await scopedDbContext.Questions
                            .Include(q => q.QuestionTags)
                            .ThenInclude(qt => qt.Tag)
                            .FirstOrDefaultAsync(q => q.Id == item.QuestionId);
                            
                        var allCategories = await scopedDbContext.Categories
                            .Select(c => new { c.Id, c.Title })
                            .ToListAsync();
                        
                        if (question == null) 
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "Question not found.";
                            return;
                        }

                        var categoriesText = string.Join("\n", allCategories.Select(c => $"- {c.Title} (ID: {c.Id})"));
                        var tags = string.Join(", ", question.QuestionTags.Select(t => t.Tag.DisplayName));

                        var prompt = $@"
Question Content: {question.Content}
Metadata: {question.Metadata}
Standard Answer: {question.StandardAnswer}
Tags: {tags}
Explanation: {question.Explanation}

Available Categories:
{categoriesText}

Based on the question content and available categories, please categorize this question into ONE of the categories above.
Return ONLY the ID of the category. Do not include any other text.
If none of the categories fit perfectly, choose the best available one.
";

                        var response = await ollamaService.AskQuestion(prompt);
                        response = response.Trim();
                        
                        if (Guid.TryParse(response, out var categoryId))
                        {
                            var category = allCategories.FirstOrDefault(c => c.Id == categoryId);
                            if (category != null)
                            {
                                item.NewValue = category.Title;
                                item.NewEntityId = category.Id;
                                item.Status = AiTaskStatus.Completed;
                            }
                            else
                            {
                                item.Status = AiTaskStatus.Failed;
                                item.Error = $"AI returned an invalid category ID: {response}";
                            }
                        }
                        else
                        {
                             // Attempt to match by name if ID fails (AI might return name)
                            var category = allCategories.FirstOrDefault(c => c.Title.Equals(response, StringComparison.OrdinalIgnoreCase));
                            if (category != null)
                            {
                                item.NewValue = category.Title;
                                item.NewEntityId = category.Id;
                                item.Status = AiTaskStatus.Completed;
                            }
                            else
                            {
                                item.Status = AiTaskStatus.Failed;
                                item.Error = $"AI returned an invalid response: {response}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = ex.Message;
                    }
                });
        }

        return Json(new { taskId = aiTask.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoTagging([FromBody] Guid[] questionIds)
    {
        if (!questionIds.Any())
        {
            return BadRequest("No questions selected.");
        }

        var questions = await dbContext.Questions
            .Include(q => q.QuestionTags)
            .ThenInclude(qt => qt.Tag)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync();

        var taskItems = questions.Select(q => new AiTaskItem
        {
            QuestionId = q.Id,
            QuestionContent = q.Content,
            QuestionStandardAnswer = q.StandardAnswer,
            OldValue = string.Join(", ", q.QuestionTags.Select(qt => qt.Tag.DisplayName)),
            Status = AiTaskStatus.Pending
        }).ToList();

        var aiTask = aiTaskService.CreateTask(taskItems, AiTaskType.AutoTagging);

        var allTaxonomies = await dbContext.Taxonomies
            .Include(t => t.Tags)
            .ToListAsync();

        // Start background processing
        foreach (var item in aiTask.Items.Values)
        {
            backgroundJobQueue.QueueWithDependency<IServiceProvider>(
                queueName: "AI-Tagging",
                jobName: $"Tag Question {item.QuestionId}",
                job: async (serviceProvider) =>
                {
                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        
                        var question = await scopedDbContext.Questions
                            .Include(q => q.QuestionTags)
                            .ThenInclude(qt => qt.Tag)
                            .Include(q => q.Category)
                            .FirstOrDefaultAsync(q => q.Id == item.QuestionId);
                        
                        if (question == null) 
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "Question not found.";
                            return;
                        }

                        var resultString = new StringBuilder();
                        var questionContext = $@"
题目类型: {question.QuestionType.GetDisplayName()}
题目分类: {question.Category?.Title ?? "未分类"}
题目内容: {question.Content}
元数据: {question.Metadata}
标准答案: {question.StandardAnswer}
现有解析: {question.Explanation}
现有标签: {string.Join(", ", question.QuestionTags.Select(qt => qt.Tag.DisplayName))}
";

                        foreach (var taxonomy in allTaxonomies)
                        {
                            var existingTags = string.Join("、", taxonomy.Tags.Select(t => t.DisplayName));
                            var prompt = $@"{questionContext}

现在我要对上面的题目打标签，我打算从 {taxonomy.Name} {{当前正在讨论的分类体系}} 角度入手，例如 {existingTags} {{这里你补充具体的标签}}，你也可以自己创建新标签。如果无法从这个角度打标签，请直接返回none。打的标签应该尊重语法 <tag>tag1</tag> <tag>tag2</tag> 的语法";

                            var response = await ollamaService.AskQuestion(prompt);
                            var matches = System.Text.RegularExpressions.Regex.Matches(response, @"<tag>(.*?)</tag>");
                            var taxonomyTags = new List<string>();
                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                var tagName = match.Groups[1].Value.Trim();
                                if (!string.IsNullOrWhiteSpace(tagName) && !tagName.Equals("none", StringComparison.OrdinalIgnoreCase))
                                {
                                    taxonomyTags.Add(tagName);
                                }
                            }

                            if (taxonomyTags.Any())
                            {
                                if (resultString.Length > 0) resultString.AppendLine();
                                resultString.Append($"{taxonomy.Name}: {string.Join(", ", taxonomyTags.Distinct())}");
                            }
                        }

                        item.NewValue = resultString.Length > 0 ? resultString.ToString() : "none";
                        item.Status = AiTaskStatus.Completed;
                    }
                    catch (Exception ex)
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = ex.Message;
                    }
                });
        }

        return Json(new { taskId = aiTask.Id });
    }

    public IActionResult Preview(Guid taskId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null)
        {
            return NotFound("AI task not found or expired.");
        }
        
        if (task.Type == AiTaskType.AutoCategorize)
        {
             ViewBag.Categories = dbContext.Categories
                 .AsNoTracking()
                 .ToList();
        }

        return this.StackView(task);
    }

    [HttpGet]
    public IActionResult GetStatus(Guid taskId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null)
        {
            return NotFound();
        }

        return Json(new
        {
            task.Id,
            task.IsCompleted,
            items = task.Items.Values.Select(i => new
            {
                i.QuestionId,
                i.QuestionContent,
                i.QuestionStandardAnswer,
                i.OldValue,
                i.NewValue,
                i.NewEntityId,
                i.Status,
                StatusText = i.Status.ToString(),
                i.Error
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adopt(Guid taskId, Guid questionId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null || !task.Items.TryGetValue(questionId, out var item))
        {
            return NotFound();
        }

        var question = await dbContext.Questions.FindAsync(questionId);
        if (question == null) return NotFound();

        if (task.Type == AiTaskType.GenerateExplanation)
        {
            if (!string.IsNullOrWhiteSpace(item.NewValue))
            {
                question.Explanation = item.NewValue;
                await dbContext.SaveChangesAsync();
            }
        }
        else if (task.Type == AiTaskType.AutoCategorize)
        {
            if (item.NewEntityId.HasValue)
            {
                question.CategoryId = item.NewEntityId.Value;
                await dbContext.SaveChangesAsync();
            }
        }
        else if (task.Type == AiTaskType.AutoTagging)
        {
            if (!string.IsNullOrWhiteSpace(item.NewValue) && item.NewValue != "none")
            {
                await ApplyTagsToQuestion(question.Id, item.NewValue);
            }
        }

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }

    private async Task ApplyTagsToQuestion(Guid questionId, string newValue)
    {
        var lines = newValue.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                var taxonomyName = parts[0].Trim();
                var tagNames = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);

                var taxonomy = await dbContext.Taxonomies.FirstOrDefaultAsync(t => t.Name == taxonomyName);
                int? taxonomyId = taxonomy?.Id;

                foreach (var tagName in tagNames)
                {
                    var tag = await tagService.GetOrCreateTagAsync(tagName.Trim(), taxonomyId);
                    await tagService.AddTagToQuestionAsync(questionId, tag.Id);
                }
            }
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Abandon(Guid taskId, Guid questionId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null)
        {
            return NotFound();
        }

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid taskId, Guid questionId, [FromBody] string newValue)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null || !task.Items.TryGetValue(questionId, out _))
        {
            return NotFound();
        }

        var question = await dbContext.Questions.FindAsync(questionId);
        if (question == null) return NotFound();

        if (task.Type == AiTaskType.GenerateExplanation)
        {
             question.Explanation = newValue;
             await dbContext.SaveChangesAsync();
        }
        else if (task.Type == AiTaskType.AutoCategorize)
        {
             if (Guid.TryParse(newValue, out var newCategoryId))
             {
                 question.CategoryId = newCategoryId;
                 await dbContext.SaveChangesAsync();
             }
        }
        else if (task.Type == AiTaskType.AutoTagging)
        {
             if (!string.IsNullOrWhiteSpace(newValue) && newValue != "none")
             {
                 await ApplyTagsToQuestion(question.Id, newValue);
             }
        }

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }
}