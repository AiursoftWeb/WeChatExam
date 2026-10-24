using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>A student-facing paper directory. Membership never grants access to paper content.</summary>
public class PaperCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(255)] public required string Name { get; set; }
    public int OrderIndex { get; set; }
    public bool IsVisible { get; set; }
    public bool IsArchived { get; set; }
    [ConcurrencyCheck] public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<PaperCollectionPaper> Papers { get; set; } = new List<PaperCollectionPaper>();
}

public class PaperCollectionPaper
{
    public Guid CollectionId { get; set; }
    public PaperCollection Collection { get; set; } = null!;
    public Guid PaperId { get; set; }
    public Paper Paper { get; set; } = null!;
}
