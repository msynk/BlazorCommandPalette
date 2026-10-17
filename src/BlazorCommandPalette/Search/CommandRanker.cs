using BlazorCommandPalette.History;

namespace BlazorCommandPalette.Search;

/// <summary>Combines match score, priority and frecency into a final rank and sorts results.</summary>
public sealed class CommandRanker
{
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
    {
        var ranked = matches.Select(m =>
        {
            var frec = frecencyOf(m.Command.Id);
            double rank;
            if (hasQuery)
            {
                rank = m.Score
                    + m.Command.Priority * _options.PriorityWeight
                    + Math.Log(1 + frec) * _options.FrecencyWeight;
            }
            else
            {
                rank = m.Command.Priority * 1000 + frec;
            }

            return m with { Rank = rank, Frecency = frec };
        });

        return ranked
            .OrderByDescending(m => m.Rank)
            .ThenBy(m => m.Command.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
