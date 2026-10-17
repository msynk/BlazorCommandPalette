using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace BlazorCommandPalette.History;

/// <summary>Persists usage in the browser's <c>localStorage</c> so recents survive reloads.</summary>
public sealed class BrowserStorageCommandHistoryStore : ICommandHistoryStore
{
    private readonly IJSRuntime _js;
    private readonly string _key;

    public BrowserStorageCommandHistoryStore(IJSRuntime js, CommandPaletteOptions options)
    {
        _js = js;
        _key = options.HistoryStorageKey;
    }

    public async ValueTask<IDictionary<string, CommandUsage>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, _key).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                return new Dictionary<string, CommandUsage>(StringComparer.Ordinal);
            }

            var data = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.DictionaryStringCommandUsage);
            return data ?? new Dictionary<string, CommandUsage>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException or JSDisconnectedException)
        {
            // Prerendering, disabled storage, or corrupt data: start empty.
            return new Dictionary<string, CommandUsage>(StringComparer.Ordinal);
        }
    }

    public async ValueTask SaveAsync(IReadOnlyDictionary<string, CommandUsage> usage, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, CommandUsage>(usage), HistoryJsonContext.Default.DictionaryStringCommandUsage);
            await _js.InvokeVoidAsync("localStorage.setItem", cancellationToken, _key, json).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // Ignore: history is best-effort.
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, CommandUsage>))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}
