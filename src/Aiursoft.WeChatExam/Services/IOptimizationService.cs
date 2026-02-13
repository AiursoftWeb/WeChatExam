namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for optimizing question answers and explanations using AI.
/// </summary>
public interface IOptimizationService
{
    /// <summary>
    /// Optimize noun explanation questions using AI.
    /// </summary>
    /// <param name="token">Cancellation token</param>
    Task OptimizeNounExplanations(CancellationToken token = default);

    /// <summary>
    /// Regenerate explanations for all questions using AI.
    /// </summary>
    /// <param name="token">Cancellation token</param>
    Task RegenerateExplanations(CancellationToken token = default);
}
