using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class CategoryPickerViewModel
{
    public List<Course> Courses { get; } = [];

    public CategoryPickerViewModel(IEnumerable<KnowledgeCategory> categories, IEnumerable<Guid> selectedIds)
    {
        var selected = selectedIds.ToHashSet();
        foreach (var group in categories.GroupBy(c => c.TotalCategoryId)
                     .OrderBy(g => g.First().TotalCategory?.OrderIndex)
                     .ThenBy(g => g.First().TotalCategory?.Title))
        {
            var ordered = group.OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ToList();
            var ids = ordered.Select(c => c.Id).ToHashSet();
            var children = ordered.Where(c => c.ParentKnowledgeCategoryId.HasValue)
                .ToLookup(c => c.ParentKnowledgeCategoryId!.Value);
            var visited = new HashSet<Guid>();
            Node Build(KnowledgeCategory category)
            {
                visited.Add(category.Id);
                var descendants = new List<Node>();
                foreach (var child in children[category.Id])
                    if (!visited.Contains(child.Id)) descendants.Add(Build(child));
                return new Node(category.Id, category.Title, selected.Contains(category.Id), descendants);
            }

            var roots = ordered.Where(c => c.ParentKnowledgeCategoryId == null || !ids.Contains(c.ParentKnowledgeCategoryId.Value))
                .Select(Build).ToList();
            // Keep malformed legacy branches selectable without rendering a cycle twice.
            foreach (var category in ordered)
                if (!visited.Contains(category.Id)) roots.Add(Build(category));
            Courses.Add(new Course(group.First().TotalCategory?.Title ?? string.Empty, roots));
        }
    }

    public record Course(string Title, List<Node> Nodes);
    public record Node(Guid Id, string Title, bool Selected, List<Node> Children)
    {
        public bool HasSelection => Selected || Children.Any(c => c.HasSelection);
    }
}
