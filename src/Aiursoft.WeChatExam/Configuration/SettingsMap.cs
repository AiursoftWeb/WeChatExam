using Aiursoft.WeChatExam.Models;

namespace Aiursoft.WeChatExam.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string Icp = "Icp";

    public const string AiPromptExplanationBase = "Ai_Prompt_Explanation_Base";
    public const string AiPromptExplanationDeep = "Ai_Prompt_Explanation_Deep";
    public const string AiPromptAutoCategorize = "Ai_Prompt_Auto_Categorize";
    public const string AiPromptAutoTagging = "Ai_Prompt_Auto_Tagging";
    public const string AiPromptGenerateAnswerChoice = "Ai_Prompt_Generate_Answer_Choice";
    public const string AiPromptGenerateAnswerBlank = "Ai_Prompt_Generate_Answer_Blank";
    public const string AiPromptGenerateAnswerBool = "Ai_Prompt_Generate_Answer_Bool";
    public const string AiPromptGenerateAnswerSubjective = "Ai_Prompt_Generate_Answer_Subjective";
    public const string AiPromptExtractDefault = "Ai_Prompt_Extract_Default";
    public const string AiPromptGradingDefault = "Ai_Prompt_Grading_Default";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft WeChatExam"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name displayed in the footer."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer[" The link to the brand's home page."],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com/"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptExplanationBase,
            Name = Localizer["AI Prompt: Base Explanation"],
            Description = Localizer["The AI prompt for generating base explanations."],
            Type = SettingType.Text,
            DefaultValue = "指令: 这是一个基础题目。请提供 200 字以内的解析，详细解释该题目的背景知识、逻辑推理以及答案的正确性。直接输出解析内容，不要包含题目本身，不要输出多余的段落、前言或总结语。"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptExplanationDeep,
            Name = Localizer["AI Prompt: Deep Explanation"],
            Description = Localizer["The AI prompt for generating deep explanations."],
            Type = SettingType.Text,
            DefaultValue = "指令: 这是一个深度题目。请提供 1000 字左右的详细解析，深入探讨该题目涉及的背景材料、核心考点、答题思路以及逻辑框架。直接输出解析内容，不要包含题目本身，不要输出多余的段落、前言或总结语。"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptAutoCategorize,
            Name = Localizer["AI Prompt: Auto Categorize"],
            Description = Localizer["The AI prompt for auto categorizing questions. Use {0} for content, {1} for metadata, {2} for answer, {3} for tags, {4} for explanation, and {5} for categories list."],
            Type = SettingType.Text,
            DefaultValue = "\nQuestion Content: {0}\nMetadata: {1}\nStandard Answer: {2}\nTags: {3}\nExplanation: {4}\n\nAvailable Categories:\n{5}\n\nBased on the question content and available categories, please categorize this question into ONE of the categories above.\nReturn ONLY the ID of the category. Do not include any other text.\nIf none of the categories fit perfectly, choose the best available one.\n"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptAutoTagging,
            Name = Localizer["AI Prompt: Auto Tagging"],
            Description = Localizer["The AI prompt for auto tagging questions. Use {0} for type, {1} for category, {2} for content, {3} for answer, {4} for explanation, {5} for existing tags, and {6} for taxonomy instructions."],
            Type = SettingType.Text,
            DefaultValue = "你是一个专业的教育内容打标助手。你的任务是根据给定的题目信息，从多个指定的维度提取并打上合适的标签。\n\n【题目信息】\n类型: {0}\n分类: {1}\n内容: {2}\n标准答案: {3}\n现有解析: {4}\n现有标签: {5}\n\n【任务指令】\n1. 请分别为以下维度评估并打标：\n{6}\n\n2. 优先级：请**极度优先**从对应维度的现有标签库中选择完全符合的标签。\n3. 创建限制：只有在现有标签库完全无法概括题目时，才允许自创 1-2 个极其精炼、通用的新标签。限制新标签必须和数据库里主要标签语言相同。\n4. 如果该题目完全不属于某个维度，请在该维度下输出 none。\n5. 输出格式：请不要输出任何解释说明文字。只输出标签内容，每一行代表一个维度的结果，格式为“维度名称: <tag>标签A</tag><tag>标签B</tag>”。\n   例如：\n   维度名称1: <tag>标签A</tag><tag>标签B</tag>\n   维度名称2: <tag>标签C</tag>\n   维度名称3: none"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptGenerateAnswerChoice,
            Name = Localizer["AI Prompt: Generate Answer (Choice)"],
            Description = Localizer["The AI prompt for generating answers for choice questions."],
            Type = SettingType.Text,
            DefaultValue = "指令: 请从提供的选项中选出唯一正确的选项内容。直接输出选项文本，不要包含任何前缀（如“答：”、“选项A：”等）或解释。"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptGenerateAnswerBlank,
            Name = Localizer["AI Prompt: Generate Answer (Blank)"],
            Description = Localizer["The AI prompt for generating answers for blank-filling questions."],
            Type = SettingType.Text,
            DefaultValue = "指令: 请根据题目内容给出填空题的正确答案。如果题目中有多个空，请按顺序给出答案，并用英文逗号“,”分隔。直接输出答案内容，不要有任何多余文字。"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptGenerateAnswerBool,
            Name = Localizer["AI Prompt: Generate Answer (Bool)"],
            Description = Localizer["The AI prompt for generating answers for true/false questions."],
            Type = SettingType.Text,
            DefaultValue = "指令: 请判断该题目。如果正确请输出“true”，如果错误请输出“false”。直接输出这两个单词之一，不要有任何多余文字。"
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptGenerateAnswerSubjective,
            Name = Localizer["AI Prompt: Generate Answer (Subjective)"],
            Description = Localizer["The AI prompt for generating answers for subjective questions (Short Answer, Essay, Noun Explanation)."],
            Type = SettingType.Text,
            DefaultValue = "指令: 请给出该题目的参考答案和评分点。格式要求：\n得分点：\n①...\n②...\n示例答案：..."
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptExtractDefault,
            Name = Localizer["AI Prompt: Default Extraction"],
            Description = Localizer["The default system prompt for extracting knowledge from material."],
            Type = SettingType.Text,
            DefaultValue = "You are an assistant that extracts knowledge points and questions from the provided material. " +
                           "Output a JSON array where each element contains 'KnowledgeTitle', 'KnowledgeContent', " +
                           "and a list of 'Questions'. Each question should have 'QuestionContent', 'QuestionType' " +
                           "(0=Choice, 1=Blank, 2=Bool, 3=ShortAnswer, 4=Essay, 5=NounExplanation), 'Metadata' (array of strings for choices, empty otherwise), " +
                           "'StandardAnswer', 'Explanation', and 'Tags' (array of strings). Do NOT wrap the JSON in Markdown. Output raw JSON only."
        },
        new GlobalSettingDefinition
        {
            Key = AiPromptGradingDefault,
            Name = Localizer["AI Prompt: Default Grading"],
            Description = Localizer["The AI prompt for grading questions. Use {0} for content, {1} for standard answer, {2} for explanation, {3} for student answer, and {4} for max score."],
            Type = SettingType.Text,
            DefaultValue = "You are an exam grader. Grade the following student's answer based on the standard answer and the question content.\n\nQuestion: {0}\nStandard Answer: {1}\nExplanation: {2}\nStudent Answer: {3}\n\nProvide the score (0-{4}) and a short comment. \nOutput JSON format: {{ \"Score\": 10, \"Comment\": \"...\", \"IsCorrect\": true }}\nIMPORTANT: Return ONLY the raw JSON string. Do not use markdown code blocks or any other formatting."
        }
    };
}
