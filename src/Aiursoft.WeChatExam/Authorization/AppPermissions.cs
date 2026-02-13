namespace Aiursoft.WeChatExam.Authorization;

/// <summary>
/// A fake localizer that returns the input string as is.
/// This is used to trick auto scanning tools to detect these strings for localization.
/// </summary>
public class FakeLocalizer
{
    public string this[string name] => name;
}

/// <summary>
/// A static class that provides all application permissions.
/// It uses a fake localizer to ensure permission names and descriptions are picked up by localization tools.
/// This class serves as the single source of truth for all permissions in the application.
/// </summary>
public class AppPermissions
{
    public const string Type = "Permission";

    public static List<PermissionDescriptor> GetAllPermissions()
    {
        // Make a fake localizer. This returns as is.
        // This trick is to make auto scanning tools to detect these strings for localization.
        var localizer = new FakeLocalizer();
        List<PermissionDescriptor> allPermission =
        [
            new(AppPermissionNames.CanReadUsers,
                localizer["Read Users"],
                localizer["Allows viewing the list of all users."]),
            new(AppPermissionNames.CanDeleteUsers,
                localizer["Delete Users"],
                localizer["Allows the permanent deletion of user accounts."]),
            new(AppPermissionNames.CanAddUsers,
                localizer["Add New Users"],
                    localizer["Grants permission to create new user accounts."]),
            new(AppPermissionNames.CanEditUsers,
                localizer["Edit User Information"],
                    localizer["Allows modification of user details like email and roles, and can also reset user passwords."]),
            new(AppPermissionNames.CanReadRoles,
                localizer["Read Roles"],
                    localizer["Allows viewing the list of roles and their assigned permissions."]),
            new(AppPermissionNames.CanDeleteRoles,
                localizer["Delete Roles"],
                localizer["Allows the permanent deletion of roles."]),
            new(AppPermissionNames.CanAddRoles,
                localizer["Add New Roles"],
                localizer["Grants permission to create new roles."]),
            new(AppPermissionNames.CanEditRoles,
                localizer["Edit Role Information"],
                localizer["Allows modification of role names and their assigned permissions."]),
            new(AppPermissionNames.CanAssignRoleToUser,
                localizer["Assign Roles to Users"],
                localizer["Allows assigning or removing roles for any user."]),
            new(AppPermissionNames.CanViewSystemContext,
                localizer["View System Context"],
                localizer["Allows viewing system-level information and settings."]),
            new(AppPermissionNames.CanRebootThisApp,
                localizer["Reboot This App"],
                localizer["Grants permission to restart the application instance. May cause availability interruptions but all settings and cache will be reloaded."]),

            new(AppPermissionNames.CanDeleteAnyCategory,
                localizer["Delete Any Category"],
                localizer["Allows deletion of any category, regardless of ownership."]),
            new(AppPermissionNames.CanEditAnyCategory,
                localizer["Edit Any Category"],
                localizer["Allows editing of any category, regardless of ownership."]),

            new(AppPermissionNames.CanDeleteQuestions,
                localizer["Delete Questions"],
                localizer["Allows deletion of questions, regardless of ownership."]),
            new(AppPermissionNames.CanEditQuestions,
                localizer["Edit Questions"],
                localizer["Allows editing of questions, regardless of ownership."]),
            new(AppPermissionNames.CanAddQuestions,
                localizer["Add Questions"],
                localizer["Allows adding of questions, regardless of ownership."]),

            new(AppPermissionNames.CanReadQuestions,
                localizer["Read Questions"],
                localizer["Allows reading of questions, regardless of ownership."]),

            new(AppPermissionNames.CanDeleteAnyKnowledgePoint,
                localizer["Delete Any KnowledgePoint"],
                localizer["Allows deletion of any knowledgePoint, regardless of ownership."]),
            new(AppPermissionNames.CanEditAnyKnowledgePoint,
                localizer["Edit Any KnowledgePoint"],
                localizer["Allows editing of any knowledgePoint, regardless of ownership."]),
            new(AppPermissionNames.CanAddAnyKnowledgePoint,
                localizer["Add Any KnowledgePoint"],
                localizer["Allows adding of any knowledgePoint, regardless of ownership."]),

            new(AppPermissionNames.CanDeleteArticles,
                localizer["Delete Articles"],
                localizer["Allows deletion of articles, regardless of ownership."]),
            new(AppPermissionNames.CanEditArticles,
                localizer["Edit Articles"],
                localizer["Allows editing of articles, regardless of ownership."]),
            new(AppPermissionNames.CanAddArticles,
                localizer["Add Articles"],
                localizer["Allows adding of articles, regardless of ownership."]),

            new(AppPermissionNames.CanDeletePapers,
                localizer["Delete Papers"],
                localizer["Allows deletion of papers, regardless of ownership."]),
            new(AppPermissionNames.CanEditPapers,
                localizer["Edit Papers"],
                localizer["Allows editing of papers, regardless of ownership."]),
            new(AppPermissionNames.CanAddPapers,
                localizer["Add Papers"],
                localizer["Allows adding of papers, regardless of ownership."]),

            new(AppPermissionNames.CanDeleteExams,
                localizer["Delete Exams"],
                localizer["Allows deletion of exams, regardless of ownership."]),
            new(AppPermissionNames.CanEditExams,
                localizer["Edit Exams"],
                localizer["Allows editing of exams, regardless of ownership."]),
            new(AppPermissionNames.CanAddExams,
                localizer["Add Exams"],
                localizer["Allows adding of exams, regardless of ownership."]),
            new(AppPermissionNames.CanReadExams,
                localizer["Read Exams"],
                localizer["Allows Reading of exams, regardless of ownership."]),

            new(AppPermissionNames.CanViewBackgroundJobs,
                localizer["View Background Jobs"],
                localizer["Allows viewing and managing background job queues and their execution status."]),

            new(AppPermissionNames.CanManageTags,
                localizer["Manage Tags"],
                localizer["Allows deleting tags from the system. Note that deleting a tag will not delete associated questions."]),

            new(AppPermissionNames.CanDeleteDistributionChannels,
                localizer["Delete Distribution Channels"],
                localizer["Allows the permanent deletion of distribution channels."]),
            new(AppPermissionNames.CanAddDistributionChannels,
                localizer["Add Distribution Channels"],
                localizer["Grants permission to create new distribution channels."]),
            new(AppPermissionNames.CanEditDistributionChannels,
                localizer["Edit Distribution Channels"],
                localizer["Allows modification of distribution channel details and enabling/disabling channels."]),
            new(AppPermissionNames.CanReadDistributionChannels,
                localizer["Read Distribution Channels"],
                localizer["Allows viewing the list of distribution channels and their statistics."]),

            new(AppPermissionNames.CanUseAIExtractor,
                localizer["Use AI Extractor"],
                localizer["Allows extracting knowledge points, questions, tags from articles using AI."]),

            new(AppPermissionNames.CanManageGlobalSettings,
                localizer["Manage Global Settings"],
                localizer["Allows modifying global system settings."]),
        ];
        return allPermission;
    }
}
