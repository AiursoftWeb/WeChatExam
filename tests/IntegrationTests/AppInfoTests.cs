using System.Net;
using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class AppInfoTests : TestBase
{
    [TestMethod]
    public async Task AnonymousVisitorsSeeOnlyExplicitPublicFields()
    {
        var configuration = GetService<IConfiguration>();
        configuration[$"GlobalSettings:{SettingsMap.BrandName}"] = "Not a legal operator";
        configuration[$"GlobalSettings:{SettingsMap.EmbeddingApiToken}"] = "synthetic-private-value";
        using var response = await Http.GetAsync("/api/app-info");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[]
        {
            "logoUrl", "operatorName", "technicalProvider", "supportContact", "supportPhone", "supportEmail", "supportWechat"
        }, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(string.Empty, document.RootElement.GetProperty("operatorName").GetString());
        Assert.DoesNotContain("synthetic-private-value", json);
        Assert.DoesNotContain("Not a legal operator", json);
    }

    [TestMethod]
    public async Task PublicInformationReflectsEditsAndCanBeCleared()
    {
        var settings = GetService<IGlobalSettingsService>();
        var values = new Dictionary<string, string>
        {
            [SettingsMap.PublicOperatorName] = "  Example Education Ltd  ",
            [SettingsMap.PublicTechnicalProvider] = "Example Software Ltd",
            [SettingsMap.PublicSupportContact] = "Support team",
            [SettingsMap.PublicSupportPhone] = "+86 12345678",
            [SettingsMap.PublicSupportEmail] = "support@example.com",
            [SettingsMap.PublicSupportWechat] = "example-support"
        };
        foreach (var (key, value) in values) await settings.UpdateSettingAsync(key, value);
        var info = await Http.GetFromJsonAsync<PublicAppInfoDto>("/api/app-info");
        Assert.IsNotNull(info);
        Assert.AreEqual("Example Education Ltd", info.OperatorName);
        Assert.AreEqual("Example Software Ltd", info.TechnicalProvider);
        Assert.AreEqual("Support team", info.SupportContact);
        Assert.AreEqual(values[SettingsMap.PublicSupportPhone], info.SupportPhone);
        Assert.AreEqual(values[SettingsMap.PublicSupportEmail], info.SupportEmail);
        Assert.AreEqual(values[SettingsMap.PublicSupportWechat], info.SupportWechat);
        await settings.UpdateSettingAsync(SettingsMap.PublicSupportEmail, "");
        Assert.AreEqual(string.Empty, (await Http.GetFromJsonAsync<PublicAppInfoDto>("/api/app-info"))!.SupportEmail);
    }

    [TestMethod]
    public async Task PublicFieldsRemainEditableOnlyByAdministrators()
    {
        using var denied = await Http.GetAsync("/GlobalSettings");
        Assert.AreEqual(HttpStatusCode.Found, denied.StatusCode);
        await LoginAsAdmin();
        var html = await Http.GetStringAsync("/GlobalSettings");
        Assert.Contains(SettingsMap.PublicOperatorName, html);
        Assert.Contains(SettingsMap.PublicSupportContact, html);
        using var saved = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            ["Key"] = SettingsMap.PublicSupportContact,
            ["Value"] = "Updated support"
        }, "/GlobalSettings");
        AssertRedirect(saved, "/GlobalSettings");
        Assert.AreEqual("Updated support", (await Http.GetFromJsonAsync<PublicAppInfoDto>("/api/app-info"))!.SupportContact);
    }
}
