namespace Aiursoft.WeChatExam.Services;

public interface IGlobalSettingsService
{
    Task<string> GetSettingValueAsync(string key);
    Task<bool> GetBoolSettingAsync(string key);
    Task<int> GetIntSettingAsync(string key);
    bool IsOverriddenByConfig(string key);
    Task UpdateSettingAsync(string key, string value);
    Task SeedSettingsAsync();
}
