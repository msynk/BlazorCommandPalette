namespace BlazorCommandPalette.Search;

/// <summary>Scores how well a query matches a text. Implement to plug in a custom algorithm.</summary>
public interface ICommandMatcher
{
    /// <summary>
    /// Match <paramref name="query"/> against <paramref name="text"/>.
    /// Returns <see cref="FieldMatch.None"/> when the text does not match.
    /// Positions are indexes into <paramref name="text"/> to highlight.
    /// </summary>
    FieldMatch Match(string query, string text);
}

/// <summary>Field weights used when combining per-field scores into a command score.</summary>
public sealed class MatchWeights
{
    public double Title { get; set; } = 1.0;
    public double Keyword { get; set; } = 0.9;
    public double Subtitle { get; set; } = 0.7;
    public double Group { get; set; } = 0.5;
    public double Description { get; set; } = 0.4;
    public double Tag { get; set; } = 0.3;
}

/// <summary>Matches a query (possibly multi-term) against a whole command.</summary>
public static class CommandMatcherExtensions
{
    /// <summary>
    /// Matches every whitespace-separated term of <paramref name="query"/> against the command's fields.
    /// All terms must match (AND). Each term's score is the best weighted field score.
    /// Supports fzf-style operators: <c>'exact</c>, <c>^prefix</c>, <c>suffix$</c>, <c>!negate</c>.
    /// </summary>
    public static CommandMatch? Match(this ICommandMatcher matcher, Command command, string query, MatchWeights? weights = null)
    {
        weights ??= DefaultWeights;
        if (string.IsNullOrWhiteSpace(query))
        {
            return new CommandMatch(command, 0, [], [], null);
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        double total = 0;
        HashSet<int>? titlePos = null;
        HashSet<int>? subtitlePos = null;
        string? matchedKeyword = null;

        foreach (var rawTerm in terms)
        {
            var (term, op) = ParseOperator(rawTerm);
            if (term.Length == 0)
            {
                continue;
            }

            if (op == TermOperator.Negate)
            {
                if (AnyFieldContains(command, term))
                {
                    return null;
                }

                continue;
            }

            double best = double.NegativeInfinity;
            int[]? bestTitle = null;
            int[]? bestSubtitle = null;
            string? bestKeyword = null;

            void Consider(string? text, double weight, Action<int[]> onPositions, string? keyword = null)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var m = op == TermOperator.Fuzzy ? matcher.Match(term, text) : ExactMatch(term, text, op);
                if (!m.Matched)
                {
                    return;
                }

                var s = m.Score * weight;
                if (s > best)
                {
                    best = s;
                    bestTitle = null;
                    bestSubtitle = null;
                    bestKeyword = keyword;
                    onPositions(m.Positions);
                }
            }

            Consider(command.Title, weights.Title, p => bestTitle = p);
            foreach (var kw in command.Keywords)
            {
                Consider(kw, weights.Keyword, _ => { }, kw);
            }

            Consider(command.Subtitle, weights.Subtitle, p => bestSubtitle = p);
            Consider(command.Group, weights.Group, _ => { });
            Consider(command.Description, weights.Description, _ => { });
            foreach (var tag in command.Tags)
            {
                Consider(tag, weights.Tag, _ => { });
            }

            if (double.IsNegativeInfinity(best))
            {
                return null;
            }

            total += best;
            if (bestTitle is { Length: > 0 })
            {
                (titlePos ??= []).UnionWith(bestTitle);
            }

            if (bestSubtitle is { Length: > 0 })
            {
                (subtitlePos ??= []).UnionWith(bestSubtitle);
            }

            matchedKeyword ??= bestKeyword;
        }

        return new CommandMatch(
            command,
            total,
            titlePos is null ? [] : [.. titlePos.Order()],
            subtitlePos is null ? [] : [.. subtitlePos.Order()],
            matchedKeyword);
    }

    private static readonly MatchWeights DefaultWeights = new();

    private enum TermOperator { Fuzzy, Exact, Prefix, Suffix, Negate }

    private static (string Term, TermOperator Op) ParseOperator(string raw)
    {
        if (raw.Length > 1 && raw[0] == '!')
        {
            return (raw[1..], TermOperator.Negate);
        }

        if (raw.Length > 1 && raw[0] == '\'')
        {
            return (raw[1..], TermOperator.Exact);
        }

        if (raw.Length > 1 && raw[0] == '^')
        {
            return (raw[1..], TermOperator.Prefix);
        }

        if (raw.Length > 1 && raw[^1] == '$')
        {
            return (raw[..^1], TermOperator.Suffix);
        }

        return (raw, TermOperator.Fuzzy);
    }

    private static bool AnyFieldContains(Command c, string term)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        return c.Title.Contains(term, cmp)
            || (c.Subtitle?.Contains(term, cmp) ?? false)
            || (c.Group?.Contains(term, cmp) ?? false)
            || c.Keywords.Any(k => k.Contains(term, cmp));
    }

    private static FieldMatch ExactMatch(string term, string text, TermOperator op)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        int idx = op switch
        {
            TermOperator.Prefix => text.StartsWith(term, cmp) ? 0 : -1,
            TermOperator.Suffix => text.EndsWith(term, cmp) ? text.Length - term.Length : -1,
            _ => text.IndexOf(term, cmp),
        };

        if (idx < 0)
        {
            return FieldMatch.None;
        }

        // Exact matches score like a perfect consecutive fuzzy run plus a boundary bonus.
        var score = term.Length * (FuzzyMatcher.ScoreMatch + FuzzyMatcher.BonusConsecutive) + (idx == 0 ? FuzzyMatcher.BonusBoundary * 2 : 0);
        return new FieldMatch(true, score, Enumerable.Range(idx, term.Length).ToArray());
    }
}
