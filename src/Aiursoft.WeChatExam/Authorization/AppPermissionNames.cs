namespace Aiursoft.WeChatExam.Authorization;

/// <summary>
/// Defines all permission keys as constants. This is the single source of truth.
/// </summary>
public static class AppPermissionNames
{
    // User Management
    public const string CanReadUsers = nameof(CanReadUsers);
    public const string CanDeleteUsers = nameof(CanDeleteUsers);
    public const string CanAddUsers = nameof(CanAddUsers);
    public const string CanEditUsers = nameof(CanEditUsers);
    public const string CanAssignRoleToUser = nameof(CanAssignRoleToUser);

    // Role Management
    public const string CanReadRoles = nameof(CanReadRoles);
    public const string CanDeleteRoles = nameof(CanDeleteRoles);
    public const string CanAddRoles = nameof(CanAddRoles);
    public const string CanEditRoles = nameof(CanEditRoles);

    // System Management
    public const string CanViewSystemContext = nameof(CanViewSystemContext);
    public const string CanRebootThisApp = nameof(CanRebootThisApp);

    // Category Management
    public const string CanDeleteAnyCategory = nameof(CanDeleteAnyCategory);
    public const string CanEditAnyCategory = nameof(CanEditAnyCategory);

    // Question Management
    public const string CanDeleteQuestions = nameof(CanDeleteQuestions);
    public const string CanAddQuestions = nameof(CanAddQuestions);
    public const string CanEditQuestions = nameof(CanEditQuestions);

    // KnowledgePoint Management
    public const string CanDeleteAnyKnowledgePoint = nameof(CanDeleteAnyKnowledgePoint);
    public const string CanEditAnyKnowledgePoint = nameof(CanEditAnyKnowledgePoint);
    public const string CanAddAnyKnowledgePoint = nameof(CanAddAnyKnowledgePoint);

    // Article Management
    public const string CanDeleteArticles = nameof(CanDeleteArticles);
    public const string CanAddArticles = nameof(CanAddArticles);
    public const string CanEditArticles = nameof(CanEditArticles);

    // Paper Management
    public const string CanDeletePapers = nameof(CanDeletePapers);
    public const string CanAddPapers = nameof(CanAddPapers);
    public const string CanEditPapers = nameof(CanEditPapers);
    
    // Exam Management
    public const string CanDeleteExams = nameof(CanDeleteExams);
    public const string CanAddExams = nameof(CanAddExams);
    public const string CanEditExams = nameof(CanEditExams);
    public const string CanReadExams = nameof(CanReadExams);

    // Background Jobs
    public const string CanViewBackgroundJobs = nameof(CanViewBackgroundJobs);

    // Tag Management
    public const string CanManageTags = nameof(CanManageTags);

    // Distribution Channel Management
    public const string CanDeleteDistributionChannels = nameof(CanDeleteDistributionChannels);
    public const string CanAddDistributionChannels = nameof(CanAddDistributionChannels);
    public const string CanEditDistributionChannels = nameof(CanEditDistributionChannels);
    public const string CanReadDistributionChannels = nameof(CanReadDistributionChannels);

}
