namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for interacting with Ollama AI
/// </summary>
public interface IOllamaService
{
    /// <summary>
    /// Ask a question to Ollama and get the response
    /// </summary>
    /// <param name="question">The question to ask</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI-generated response</returns>
    Task<string> AskQuestion(string question, CancellationToken cancellationToken = default);
}
