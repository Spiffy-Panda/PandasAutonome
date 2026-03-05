using Autonome.Core.World;

namespace Autonome.Core.Graph;

/// <summary>
/// Maintains the DAG of authority relationships.
/// Built from "authority"-tagged relationships, cached for fast traversal.
/// </summary>
public class AuthorityGraph
{
    private readonly Dictionary<string, List<string>> _subordinates = new();
    private readonly Dictionary<string, List<string>> _superiors = new();

    public void Build(RelationshipStore relationships)
    {
        _subordinates.Clear();
        _superiors.Clear();

        foreach (var rel in relationships.All())
        {
            if (!rel.Tags.Contains("authority")) continue;

            if (!_subordinates.TryGetValue(rel.Source, out var subList))
            {
                subList = [];
                _subordinates[rel.Source] = subList;
            }
            subList.Add(rel.Target);

            if (!_superiors.TryGetValue(rel.Target, out var supList))
            {
                supList = [];
                _superiors[rel.Target] = supList;
            }
            supList.Add(rel.Source);
        }
    }

    /// <summary>
    /// Validates that the authority graph is acyclic using Kahn's algorithm.
    /// Throws if a cycle is detected.
    /// </summary>
    public void ValidateAcyclic()
    {
        var inDegree = new Dictionary<string, int>();
        var allNodes = new HashSet<string>();

        foreach (var (parent, children) in _subordinates)
        {
            allNodes.Add(parent);
            foreach (var child in children)
            {
                allNodes.Add(child);
                inDegree[child] = inDegree.GetValueOrDefault(child, 0) + 1;
            }
        }

        var queue = new Queue<string>();
        foreach (var node in allNodes)
        {
            if (!inDegree.ContainsKey(node) || inDegree[node] == 0)
                queue.Enqueue(node);
        }

        int visited = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            visited++;

            if (_subordinates.TryGetValue(node, out var children))
            {
                foreach (var child in children)
                {
                    inDegree[child]--;
                    if (inDegree[child] == 0)
                        queue.Enqueue(child);
                }
            }
        }

        if (visited != allNodes.Count)
        {
            throw new InvalidOperationException(
                $"Authority graph contains a cycle. Visited {visited} of {allNodes.Count} nodes.");
        }
    }

    /// <summary>
    /// BFS traversal following authority edges downward.
    /// </summary>
    public List<string> GetSubordinates(string id, int? maxDepth = null, Func<string, bool>? filter = null)
    {
        var results = new List<string>();
        var visited = new HashSet<string> { id };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((id, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (maxDepth.HasValue && depth >= maxDepth.Value) continue;

            if (!_subordinates.TryGetValue(current, out var children)) continue;

            foreach (var child in children)
            {
                if (!visited.Add(child)) continue;

                if (filter == null || filter(child))
                    results.Add(child);

                queue.Enqueue((child, depth + 1));
            }
        }

        return results;
    }

    public List<string> GetSuperiors(string id)
    {
        return _superiors.TryGetValue(id, out var list) ? new List<string>(list) : [];
    }

    /// <summary>
    /// All Autonomes sharing at least one authority parent with the given id.
    /// </summary>
    public List<string> GetPeers(string id)
    {
        var superiors = GetSuperiors(id);
        var peers = new HashSet<string>();

        foreach (var parent in superiors)
        {
            if (_subordinates.TryGetValue(parent, out var children))
            {
                foreach (var child in children)
                {
                    if (child != id) peers.Add(child);
                }
            }
        }

        return peers.ToList();
    }

    public bool HasSubordinates(string id) =>
        _subordinates.TryGetValue(id, out var list) && list.Count > 0;
}
