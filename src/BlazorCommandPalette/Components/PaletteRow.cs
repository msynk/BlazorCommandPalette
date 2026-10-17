using BlazorCommandPalette.Search;

namespace BlazorCommandPalette.Components;

/// <summary>Kind of a rendered row.</summary>
public enum PaletteRowKind
{
    GroupHeader,
    Item,
    Separator,
}

/// <summary>One flattened row of the result list (header or item). Exposed to templates.</summary>
public sealed class PaletteRow
{
    internal PaletteRow(PaletteRowKind kind, string? group, CommandMatch? match, int itemIndex, string elementId)
    {
        Kind = kind;
        Group = group;
        Match = match;
        ItemIndex = itemIndex;
        ElementId = elementId;
    }

    public PaletteRowKind Kind { get; }

    /// <summary>Group heading text (for headers, and for items the group they belong to).</summary>
    public string? Group { get; }

    /// <summary>Match details for item rows.</summary>
    public CommandMatch? Match { get; }

    /// <summary>Zero-based index among item rows; -1 for headers.</summary>
    public int ItemIndex { get; }

    /// <summary>DOM id used for <c>aria-activedescendant</c>.</summary>
    public string ElementId { get; }

    public Command Command => Match?.Command ?? throw new InvalidOperationException("Row has no command.");
}

/// <summary>Context handed to <c>ItemTemplate</c>.</summary>
public sealed record PaletteItemContext(PaletteRow Row, bool IsActive, bool IsChecked, bool IsPinned, bool IsConfirming, bool IsMac);

/// <summary>Snapshot of palette state handed to header/footer templates.</summary>
public sealed record PaletteStateContext(
    string Query,
    string? ModePrefix,
    string? ModeName,
    IReadOnlyList<string> Breadcrumbs,
    int ResultCount,
    int ActiveIndex,
    Command? ActiveCommand,
    bool IsLoading,
    bool IsMultiSelect,
    int SelectedCount,
    string? Toast,
    bool IsMac);

/// <summary>A page on the navigation stack.</summary>
internal sealed class PageFrame
{
    public required string Title { get; init; }

    /// <summary>Command whose children are listed (null for the root page and ad-hoc pages).</summary>
    public Command? Command { get; init; }

    /// <summary>Explicit items for ad-hoc pages (results of <see cref="CommandResult.Page"/>, action panels, lazily loaded children).</summary>
    public IReadOnlyList<Command>? Items { get; set; }

    public bool MultiSelect { get; init; }

    public Func<CommandContext, Task<CommandResult>>? OnSubmit { get; init; }

    public string? Placeholder { get; init; }

    public CommandResult.PromptResult? Prompt { get; init; }

    public bool IsActions { get; init; }

    /// <summary>Query to restore when this page is popped.</summary>
    public string QueryBefore { get; init; } = string.Empty;

    public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);

    public bool IsRoot => Command is null && Items is null && Prompt is null;
}
