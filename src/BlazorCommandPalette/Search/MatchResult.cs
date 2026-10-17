namespace BlazorCommandPalette.Search;

/// <summary>Result of matching a query against one text field.</summary>
public readonly record struct FieldMatch(bool Matched, double Score, int[] Positions)
{
    public static FieldMatch None { get; } = new(false, double.NegativeInfinity, []);

    public static FieldMatch Empty { get; } = new(true, 0, []);
}

/// <summary>Result of matching a query against a <see cref="Command"/> across its searchable fields.</summary>
public sealed record CommandMatch(Command Command, double Score, int[] TitlePositions, int[] SubtitlePositions, string? MatchedKeyword)
{
    /// <summary>Final rank after priority and frecency adjustments. Set by the ranker.</summary>
    public double Rank { get; init; }

    /// <summary>Frecency contribution included in <see cref="Rank"/>.</summary>
    public double Frecency { get; init; }

    /// <summary>True when the command came from an async <see cref="ICommandSource"/>.</summary>
    public bool FromSource { get; init; }

    public string? SourceName { get; init; }
}
