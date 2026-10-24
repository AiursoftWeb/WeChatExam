using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

// These compatibility records can be retired independently from papers, tags and snapshots.
public class LegacyPaperCollectionCatalog
{
    // The single catalog is selected explicitly by an administrator, never inferred from a name or ID.
    [Key] public int Id { get; set; } = 1;
    public int TaxonomyId { get; set; }
    public Taxonomy Taxonomy { get; set; } = null!;
}

public class LegacyPaperCollectionBinding
{
    [Key] public Guid CollectionId { get; set; }
    public PaperCollection Collection { get; set; } = null!;
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public class LegacyPaperCollectionAlias
{
    [Key, MaxLength(255)] public required string NormalizedName { get; set; }
    public Guid CollectionId { get; set; }
    public PaperCollection Collection { get; set; } = null!;
}
