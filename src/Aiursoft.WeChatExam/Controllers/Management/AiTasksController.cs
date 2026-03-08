using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Aiursoft.WeChatExam.Configuration;
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
                    if (aiTask.IsCanceled || DateTime.UtcNow - aiTask.LastAlive > TimeSpan.FromSeconds(50))
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = "Task canceled or abandoned.";
                        return;
                    }

                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        var settingsService = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
                        
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
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptExplanationBase));
                        }
                        else
                        {
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptExplanationDeep));
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
                    if (aiTask.IsCanceled || DateTime.UtcNow - aiTask.LastAlive > TimeSpan.FromSeconds(50))
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = "Task canceled or abandoned.";
                        return;
                    }

                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        var settingsService = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
                        
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

                        var promptTemplate = await settingsService.GetSettingValueAsync(SettingsMap.AiPromptAutoCategorize);
                        var prompt = string.Format(promptTemplate, 
                            question.Content, 
                            question.Metadata, 
                            question.StandardAnswer, 
                            tags, 
                            question.Explanation, 
                            categoriesText);

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
            .Include(t => t.Tags.Take(50))
            .ToListAsync();

        // Start background processing
        foreach (var item in aiTask.Items.Values)
        {
            backgroundJobQueue.QueueWithDependency<IServiceProvider>(
                queueName: "AI-Tagging",
                jobName: $"Tag Question {item.QuestionId}",
                job: async (serviceProvider) =>
                {
                    if (aiTask.IsCanceled || DateTime.UtcNow - aiTask.LastAlive > TimeSpan.FromSeconds(50))
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = "Task canceled or abandoned.";
                        return;
                    }

                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        var settingsService = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();

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

                        var taxonomyInstructions = string.Join("\n\n", allTaxonomies.Select(t => $@"维度：{t.Name}
现有标签库：[{string.Join("、", t.Tags.Select(tag => tag.DisplayName))}]"));

                        var promptTemplate = await settingsService.GetSettingValueAsync(SettingsMap.AiPromptAutoTagging);
                        var prompt = string.Format(promptTemplate,
                            question.QuestionType.GetDisplayName(),
                            question.Category?.Title ?? "未分类",
                            question.Content,
                            question.StandardAnswer,
                            question.Explanation,
                            string.Join(", ", question.QuestionTags.Select(qt => qt.Tag.DisplayName)),
                            taxonomyInstructions);

                        var response = await ollamaService.AskQuestion(prompt);

                        var resultString = new StringBuilder();
                        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Split(':', 2);
                            if (parts.Length == 2)
                            {
                                var taxonomyName = parts[0].Trim();
                                var tagString = parts[1].Trim();

                                var matches = System.Text.RegularExpressions.Regex.Matches(tagString, @"<tag>(.*?)</tag>");
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
                                    resultString.Append($"{taxonomyName}: {string.Join(", ", taxonomyTags.Distinct())}");
                                }
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateAnswers([FromBody] Guid[] questionIds)
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
            OldValue = q.StandardAnswer,
            Status = AiTaskStatus.Pending
        }).ToList();

        var aiTask = aiTaskService.CreateTask(taskItems, AiTaskType.GenerateAnswer);

        // Start background processing
        foreach (var item in aiTask.Items.Values)
        {
            backgroundJobQueue.QueueWithDependency<IServiceProvider>(
                queueName: "AI-Answer-Generation",
                jobName: $"Generate Answer for Question {item.QuestionId}",
                job: async (serviceProvider) =>
                {
                    if (aiTask.IsCanceled || DateTime.UtcNow - aiTask.LastAlive > TimeSpan.FromSeconds(50))
                    {
                        item.Status = AiTaskStatus.Failed;
                        item.Error = "Task canceled or abandoned.";
                        return;
                    }

                    item.Status = AiTaskStatus.Processing;
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var scopedDbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                        var ollamaService = scope.ServiceProvider.GetRequiredService<IOllamaService>();
                        var settingsService = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
                        
                        var question = await scopedDbContext.Questions
                            .Include(q => q.Category)
                            .FirstOrDefaultAsync(q => q.Id == item.QuestionId);
                        if (question == null) 
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "Question not found in database.";
                            return;
                        }

                        var category = question.Category?.Title ?? "未分类";
                        var type = question.QuestionType.GetDisplayName();
                        
                        var promptBuilder = new StringBuilder();
                        promptBuilder.AppendLine($"题目类型: {type}");
                        promptBuilder.AppendLine($"题目分类: {category}");
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
                            }
                            catch
                            {
                                // Ignore metadata if not JSON or doesn't have options
                            }
                        }
                        
                        promptBuilder.AppendLine();

                        if (question.QuestionType == QuestionType.Choice)
                        {
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptGenerateAnswerChoice));
                        }
                        else if (question.QuestionType == QuestionType.Blank)
                        {
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptGenerateAnswerBlank));
                        }
                        else if (question.QuestionType == QuestionType.Bool)
                        {
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptGenerateAnswerBool));
                        }
                        else if (question.QuestionType == QuestionType.ShortAnswer || 
                                 question.QuestionType == QuestionType.Essay || 
                                 question.QuestionType == QuestionType.NounExplanation)
                        {
                            promptBuilder.AppendLine(await settingsService.GetSettingValueAsync(SettingsMap.AiPromptGenerateAnswerSubjective));
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
        else if (task.Type == AiTaskType.GenerateAnswer)
        {
            if (!string.IsNullOrWhiteSpace(item.NewValue))
            {
                question.StandardAnswer = item.NewValue;
                await dbContext.SaveChangesAsync();
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
        else if (task.Type == AiTaskType.GenerateAnswer)
        {
             if (!string.IsNullOrWhiteSpace(newValue))
             {
                 question.StandardAnswer = newValue;
                 await dbContext.SaveChangesAsync();
             }
        }

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult KeepAlive(Guid taskId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task != null)
        {
            task.LastAlive = DateTime.UtcNow;
            return Ok();
        }
        return NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelAll(Guid taskId)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task != null)
        {
            task.IsCanceled = true;
            foreach (var item in task.Items.Values)
            {
                if (item.Status == AiTaskStatus.Pending)
                {
                    item.Status = AiTaskStatus.Failed;
                    item.Error = "Task canceled by user.";
                }
            }
            return Ok();
        }
        return NotFound();
    }
}