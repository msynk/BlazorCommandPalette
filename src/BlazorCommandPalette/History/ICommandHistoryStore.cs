namespace BlazorCommandPalette.History;

/// <summary>Usage statistics for one command, used for recents, pinning and frecency ranking.</summary>
public sealed record CommandUsage(int Count, DateTimeOffset LastUsed, bool Pinned);

/// <summary>Persists command usage. Implement to store in a database or user profile.</summary>
public interface ICommandHistoryStore
{
    ValueTask<IDictionary<string, CommandUsage>> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(IReadOnlyDictionary<string, CommandUsage> usage, CancellationToken cancellationToken = default);
}

/// <summary>Keeps usage in memory for the lifetime of the scope (circuit / tab).</summary>
public sealed class InMemoryCommandHistoryStore : ICommandHistoryStore
{
    private Dictionary<string, CommandUsage> _data = new(StringComparer.Ordinal);

    public ValueTask<IDictionary<string, CommandUsage>> LoadAsync(CancellationToken cancellationToken = default)
        => new(new Dictionary<string, CommandUsage>(_data, StringComparer.Ordinal));

    public ValueTask SaveAsync(IReadOnlyDictionary<string, CommandUsage> usage, CancellationToken cancellationToken = default)
    {
        _data = new Dictionary<string, CommandUsage>(usage, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }
}
