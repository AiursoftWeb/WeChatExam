namespace Aiursoft.WeChatExam.Services;

/// <summary>A failed grading attempt is not an incorrect student answer.</summary>
public sealed class GradingUnavailableException(Exception innerException)
    : Exception("Grading is temporarily unavailable. Please try again.", innerException);
