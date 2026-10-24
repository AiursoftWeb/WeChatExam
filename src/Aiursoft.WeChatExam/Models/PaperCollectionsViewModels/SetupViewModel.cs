using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.PaperCollectionsViewModels;

public class SetupViewModel : UiStackLayoutViewModel
{
    public SetupViewModel() { PageTitle = "Connect Existing Paper Directory"; }
    public int? TaxonomyId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public List<Taxonomy> Taxonomies { get; set; } = [];
    public CollectionImportPreview? Preview { get; set; }
}
