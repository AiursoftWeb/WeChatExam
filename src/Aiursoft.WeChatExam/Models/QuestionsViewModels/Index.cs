using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Questions";
    }

    public List<Question> Questions { get; set; } = new();

    public Guid? SelectedCategoryId { get; set; }

    public Category? SelectedCategory { get; set; }

    public List<Category> Categories { get; set; } = new();

    // Pagination
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    // Filters
    public QuestionType? FilterQuestionType { get; set; }
    public DateTime? FilterStartDate { get; set; }
    public DateTime? FilterEndDate { get; set; }
    public string? FilterTag { get; set; }
    public string? FilterMtql { get; set; }

    // Sorting (default: CreatedAt desc)
    public string SortBy { get; set; } = "CreatedAt";
    public string SortOrder { get; set; } = "Desc";

    // Dropdown options
    public IEnumerable<SelectListItem> QuestionTypeOptions { get; set; } = new List<SelectListItem>();
    public IEnumerable<SelectListItem> PageSizeOptions { get; set; } = new List<SelectListItem>();
}
