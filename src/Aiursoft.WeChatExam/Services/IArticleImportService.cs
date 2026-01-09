using Aiursoft.WeChatExam.Models.DTOs;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for importing articles and extracting questions using AI
/// </summary>
public interface IArticleImportService
{
    /// <summary>
    /// Import an article and extract questions synchronously
    /// </summary>
    /// <param name="importDto">Article import data</param>
    /// <param name="authorId">ID of the user importing the article</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with extracted questions</returns>
    Task<ArticleImportResultDto> ImportArticleAsync(
        ArticleImportDto importDto, 
        string authorId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import an article and extract questions asynchronously using background job
    /// </summary>
    /// <param name="importDto">Article import data</param>
    /// <param name="authorId">ID of the user importing the article</param>
    /// <returns>Background job ID for tracking</returns>
    Task<Guid> ImportArticleAsync(
        ArticleImportDto importDto, 
        string authorId);

    /// <summary>
    /// Extract questions from article content using AI
    /// </summary>
    /// <param name="articleContent">Content to extract questions from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted questions</returns>
    Task<List<ExtractedQuestionDto>> ExtractQuestionsFromContentAsync(
        string articleContent, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract tags from article content using AI
    /// </summary>
    /// <param name="articleContent">Content to extract tags from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted tags</returns>
    Task<List<string>> ExtractTagsFromContentAsync(
        string articleContent, 
        CancellationToken cancellationToken = default);
}