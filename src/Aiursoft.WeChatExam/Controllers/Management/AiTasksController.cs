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
            OldExplanation = q.Explanation,
            Status = AiTaskStatus.Pending
        }).ToList();

        var aiTask = aiTaskService.CreateTask(taskItems);

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
                            item.NewExplanation = response.Trim();
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
                i.OldExplanation,
                i.NewExplanation,
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
        if (task == null || !task.Items.TryGetValue(questionId, out _))
        {
            return NotFound();
        }

        var question = await dbContext.Questions.FindAsync(questionId);
        if (question != null && task.Items.TryGetValue(questionId, out var item) && !string.IsNullOrWhiteSpace(item.NewExplanation))
        {
            question.Explanation = item.NewExplanation;
            await dbContext.SaveChangesAsync();
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
    public async Task<IActionResult> Edit(Guid taskId, Guid questionId, [FromBody] string newExplanation)
    {
        var task = aiTaskService.GetTask(taskId);
        if (task == null || !task.Items.TryGetValue(questionId, out _))
        {
            return NotFound();
        }

        var question = await dbContext.Questions.FindAsync(questionId);
        if (question != null)
        {
            question.Explanation = newExplanation;
            await dbContext.SaveChangesAsync();
        }

        task.Items.TryRemove(questionId, out _);
        return Ok();
    }
}
