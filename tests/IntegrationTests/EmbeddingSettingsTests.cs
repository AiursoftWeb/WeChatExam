using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class EmbeddingSettingsTests : TestBase
{
    [TestMethod]
    public async Task EmbeddingsReuseExistingAiEndpointAndTokenUnlessOverridden()
    {
        var settings = GetService<IGlobalSettingsService>();
        var configuration = GetService<IConfiguration>();
        configuration["OpenAI:CompletionApiUrl"] = "https://existing-ai.example.com/v1/chat/completions";
        configuration["OpenAI:Token"] = "existing-ai-test-token";
        var completionUri = new Uri(configuration["OpenAI:CompletionApiUrl"]!);
        Assert.AreEqual(completionUri.GetLeftPart(UriPartial.Authority), await settings.GetEmbeddingEndpointAsync());
        Assert.AreEqual(configuration["OpenAI:Token"] ?? string.Empty, await settings.GetEmbeddingTokenAsync());
        Assert.IsFalse(await settings.IsAiSearchEnabledAsync());

        await settings.UpdateSettingAsync(SettingsMap.EnableEmbeddingBasedSearch, "True");
        Assert.IsTrue(await settings.IsAiSearchEnabledAsync());
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingOllamaInstance, "https://embedding.example.com/api/chat");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingApiToken, "dedicated-embedding-test-token");
        Assert.AreEqual("https://embedding.example.com", await settings.GetEmbeddingEndpointAsync());
        Assert.AreEqual("dedicated-embedding-test-token", await settings.GetEmbeddingTokenAsync());
        var stored = await GetService<WeChatExamDbContext>().GlobalSettings.AsNoTracking()
            .SingleAsync(s => s.Key == SettingsMap.EmbeddingApiToken);
        Assert.StartsWith("protected:v1:", stored.Value);

        await settings.UpdateSettingAsync(SettingsMap.EmbeddingOllamaInstance, "");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingApiToken, "");
        Assert.AreEqual(completionUri.GetLeftPart(UriPartial.Authority), await settings.GetEmbeddingEndpointAsync());
        Assert.AreEqual(configuration["OpenAI:Token"] ?? string.Empty, await settings.GetEmbeddingTokenAsync());
    }

    [TestMethod]
    public async Task InvalidEmbeddingEndpointDisablesGeneration()
    {
        var settings = GetService<IGlobalSettingsService>();
        await settings.UpdateSettingAsync(SettingsMap.EnableEmbeddingBasedSearch, "True");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingOllamaInstance, "invalid-url");
        Assert.IsFalse(await settings.IsAiSearchEnabledAsync());
    }
}
