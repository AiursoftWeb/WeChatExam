namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public sealed record PublicAppInfoDto(
    string LogoUrl,
    string OperatorName,
    string TechnicalProvider,
    string SupportContact,
    string SupportPhone,
    string SupportEmail,
    string SupportWechat);
