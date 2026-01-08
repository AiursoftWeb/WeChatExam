using Aiursoft.GptClient.Services;
using Aiursoft.WeChatExam.Configuration;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for interacting with Ollama AI using GptClient
/// </summary>
public class OllamaService : IOllamaService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAIConfiguration _configuration;

    public OllamaService(ChatClient chatClient, IOptions<OpenAIConfiguration> configuration)
    {
        _chatClient = chatClient;
        _configuration = configuration.Value;
    }

    /// <inheritdoc/>
    public async Task<string> AskQuestion(string question, CancellationToken cancellationToken = default)
    {
        var response = await _chatClient.AskString(
            modelType: _configuration.Model,
            completionApiUrl: _configuration.CompletionApiUrl,
            token: _configuration.Token,
            content: [question],
            cancellationToken: cancellationToken);

        return response.GetAnswerPart();
    }
}
