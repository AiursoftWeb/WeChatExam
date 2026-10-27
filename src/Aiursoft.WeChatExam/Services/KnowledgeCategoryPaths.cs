using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public static class KnowledgeCategoryPaths
{
    public static Dictionary<Guid, string> Build(IEnumerable<KnowledgeCategory> categories)
    {
        var index = categories.ToDictionary(c => c.Id);
        return index.Values.ToDictionary(c => c.Id, category =>
        {
            var titles = new List<string>();
            var seen = new HashSet<Guid>();
            KnowledgeCategory? current = category;
            while (current != null && seen.Add(current.Id))
            {
                titles.Add(current.Title);
                current = current.ParentKnowledgeCategoryId is { } parent ? index.GetValueOrDefault(parent) : null;
            }
            if (category.TotalCategory != null) titles.Add(category.TotalCategory.Title);
            titles.Reverse();
            return string.Join(" / ", titles);
        });
    }
}
