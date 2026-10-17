using BlazorCommandPalette.History;

namespace BlazorCommandPalette.Search;

/// <summary>Combines match score, priority and frecency into a final rank and sorts results.</summary>
public sealed class CommandRanker
{
    private static readonly StringComparer TitleComparer = StringComparer.CurrentCultureIgnoreCase;

    private readonly CommandPaletteOptions _options;

    public CommandRanker(CommandPaletteOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Ranks matches. With a non-empty query: match score dominates, frecency and priority nudge.
    /// With an empty query: priority, then frecency, then original order / title.
    /// </summary>
    public List<CommandMatch> Rank(IEnumerable<CommandMatch> matches, bool hasQuery, Func<string, double> frecencyOf)
        => Rank(matches, hasQuery, frecencyOf, int.MaxValue);

    /// <summary>
    /// Same ordering as <see cref="Rank(IEnumerable{CommandMatch}, bool, Func{string, double})"/>, keeping only the best
    /// <paramref name="limit"/> results: only those are sorted and copied, which keeps big registries cheap per keystroke.
    /// </summary>
    internal List<CommandMatch> Rank(IEnumerable<CommandMatch> matches, bool hasQuery, Func<string, double> frecencyOf, int limit)
    {
        var items = matches as IReadOnlyList<CommandMatch> ?? matches.ToList();
        var n = items.Count;
        limit = Math.Max(0, limit);
        var ranks = new double[n];
        var frecencies = new double[n];
        for (var i = 0; i < n; i++)
        {
            var m = items[i];
            var frec = frecencyOf(m.Command.Id);
            ranks[i] = hasQuery
                ? m.Score + m.Command.Priority * _options.PriorityWeight + Math.Log(1 + frec) * _options.FrecencyWeight
                : m.Command.Priority * 1000 + frec;
            frecencies[i] = frec;
        }

        // Rank descending, then title, then original position (a stable sort).
        int Compare(int a, int b)
        {
            var c = ranks[b].CompareTo(ranks[a]);
            if (c == 0)
            {
                c = TitleComparer.Compare(items[a].Command.Title, items[b].Command.Title);
            }

            return c != 0 ? c : a.CompareTo(b);
        }

        int[] order;
        if (limit >= n)
        {
            order = new int[n];
            for (var i = 0; i < n; i++)
            {
                order[i] = i;
            }
        }
        else
        {
            // Keep the best `limit` indexes in a heap whose root is the worst one kept.
            var heap = new PriorityQueue<int, int>(limit, Comparer<int>.Create((a, b) => Compare(b, a)));
            for (var i = 0; i < n; i++)
            {
                if (heap.Count < limit)
                {
                    heap.Enqueue(i, i);
                }
                else if (limit > 0 && Compare(i, heap.Peek()) < 0)
                {
                    heap.DequeueEnqueue(i, i);
                }
            }

            order = new int[heap.Count];
            for (var i = 0; heap.TryDequeue(out var index, out _); i++)
            {
                order[i] = index;
            }
        }

        Array.Sort(order, Compare);
        var ranked = new List<CommandMatch>(order.Length);
        foreach (var i in order)
        {
            ranked.Add(items[i] with { Rank = ranks[i], Frecency = frecencies[i] });
        }

        return ranked;
    }
}
