namespace BlazorCommandPalette.Search;

/// <summary>
/// Provides commands asynchronously for a query (e.g. search an API for documents, users, issues).
/// Sources are queried with debouncing and cancellation; results are shown under <see cref="Name"/>.
/// </summary>
public interface ICommandSource
{
    /// <summary>Group heading for results of this source.</summary>
    string Name { get; }

    /// <summary>Minimum query length before the source is asked. 0 queries on an empty box too.</summary>
    int MinQueryLength => 1;

    /// <summary>Upper bound of results taken from this source.</summary>
    int MaxResults => 20;

    /// <summary>
    /// When true, returned commands are re-scored and interleaved with registry results by the matcher.
    /// When false (default), the source's own order is kept and shown as a block.
    /// </summary>
    bool ApplyMatcher => false;

    /// <summary>Search for commands matching <paramref name="query"/>.</summary>
    Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken);
}

/// <summary>Extra information available to sources.</summary>
public sealed record CommandSourceContext(string? ModePrefix, string? ParentCommandId, IServiceProvider Services);

/// <summary>Convenience <see cref="ICommandSource"/> built from a delegate.</summary>
public sealed class DelegateCommandSource : ICommandSource
{
    private readonly Func<string, CommandSourceContext, CancellationToken, Task<IReadOnlyList<Command>>> _search;

    public DelegateCommandSource(string name, Func<string, CancellationToken, Task<IReadOnlyList<Command>>> search, int minQueryLength = 1, int maxResults = 20, bool applyMatcher = false)
        : this(name, (q, _, ct) => search(q, ct), minQueryLength, maxResults, applyMatcher)
    {
    }

    public DelegateCommandSource(string name, Func<string, CommandSourceContext, CancellationToken, Task<IReadOnlyList<Command>>> search, int minQueryLength = 1, int maxResults = 20, bool applyMatcher = false)
    {
        Name = name;
        _search = search;
        MinQueryLength = minQueryLength;
        MaxResults = maxResults;
        ApplyMatcher = applyMatcher;
    }

    public string Name { get; }

    public int MinQueryLength { get; }

    public int MaxResults { get; }

    public bool ApplyMatcher { get; }

    public Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken)
        => _search(query, context, cancellationToken);
}

/// <summary>A source backed by an in-memory list, filtered with the palette's matcher (useful for large static datasets).</summary>
public sealed class StaticCommandSource : ICommandSource
{
    private readonly Func<IEnumerable<Command>> _items;

    public StaticCommandSource(string name, IEnumerable<Command> items, int maxResults = 50)
        : this(name, () => items, maxResults)
    {
    }

    public StaticCommandSource(string name, Func<IEnumerable<Command>> items, int maxResults = 50)
    {
        Name = name;
        _items = items;
        MaxResults = maxResults;
    }

    public string Name { get; }

    public int MinQueryLength => 1;

    public int MaxResults { get; }

    public bool ApplyMatcher => true;

    public Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Command>>(_items().ToList());
}
