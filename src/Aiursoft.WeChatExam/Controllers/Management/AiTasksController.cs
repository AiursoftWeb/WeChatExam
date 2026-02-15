using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanEditQuestions)]
[LimitPerMin]
public class AiTasksController(
    AiTaskService aiTaskService,
    BackgroundJobQueue backgroundJobQueue,
    WeChatExamDbContext dbContext) : Controller
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
                        
                        var question = await scopedDbContext.Questions.FindAsync(item.QuestionId);
                        if (question == null) 
                        {
                            item.Status = AiTaskStatus.Failed;
                            item.Error = "Question not found in database.";
                            return;
                        }

                        var prompt = $@"{question.Content} + {question.Metadata} + {question.StandardAnswer}

上面这句话太笼统了，对于简单的选择判断填空，你给我扩展一下200字以内的材料，详细解释一下这个题目的背景和逻辑。对于简答题，小作文，给我扩展一下1000字的材料，详细解释这个简答题牵扯的背景、材料、答题思路。直接输出完整的解析即可。";

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
                        response = response?.Trim();
                        
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

        task.Items.TryRemove(questionId, out _);
        return Ok();
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
        if (task == null || !task.Items.TryGetValue(questionId, out var item))
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

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }
}