using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Taxonomy";
    }

    public Taxonomy Taxonomy { get; set; } = null!;
    public int TagCount { get; set; }
}
