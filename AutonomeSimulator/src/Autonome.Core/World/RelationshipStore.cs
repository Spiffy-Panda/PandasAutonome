using Autonome.Core.Model;

namespace Autonome.Core.World;

/// <summary>
/// Stores all relationships, queryable by source, target, and tags.
/// </summary>
public class RelationshipStore
{
    private readonly List<Relationship> _all = [];
    private readonly Dictionary<string, List<Relationship>> _bySource = new();
    private readonly Dictionary<string, List<Relationship>> _byTarget = new();

    public void Add(Relationship rel)
    {
        _all.Add(rel);

        if (!_bySource.TryGetValue(rel.Source, out var sourceList))
        {
            sourceList = [];
            _bySource[rel.Source] = sourceList;
        }
        sourceList.Add(rel);

        if (!_byTarget.TryGetValue(rel.Target, out var targetList))
        {
            targetList = [];
            _byTarget[rel.Target] = targetList;
        }
        targetList.Add(rel);
    }

    public IReadOnlyList<Relationship> All() => _all;

    public Relationship? Get(string source, string target)
    {
        if (!_bySource.TryGetValue(source, out var list)) return null;
        return list.FirstOrDefault(r => r.Target == target);
    }

    public List<Relationship> GetBySource(string source) =>
        _bySource.TryGetValue(source, out var list) ? list : [];

    public List<Relationship> GetByTarget(string target) =>
        _byTarget.TryGetValue(target, out var list) ? list : [];

    public List<Relationship> GetByTag(string tag) =>
        _all.Where(r => r.Tags.Contains(tag)).ToList();

    public void ModifyProperty(string source, string target, string propertyId, float amount)
    {
        var rel = Get(source, target);
        if (rel == null)
        {
            // Create relationship if it doesn't exist
            rel = new Relationship
            {
                Source = source,
                Target = target,
                Tags = new HashSet<string>(),
                Properties = new Dictionary<string, PropertyState>()
            };
            Add(rel);
        }

        if (!rel.Properties.TryGetValue(propertyId, out var prop))
        {
            prop = new PropertyState(new PropertyDefinition(propertyId, 0.5f));
            rel.Properties[propertyId] = prop;
        }

        prop.Value = Math.Clamp(prop.Value + amount, prop.Min, prop.Max);
    }
}
