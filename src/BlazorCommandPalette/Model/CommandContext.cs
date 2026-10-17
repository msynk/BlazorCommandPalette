using Microsoft.AspNetCore.Components;

namespace BlazorCommandPalette;

/// <summary>Information passed to a command handler.</summary>
public sealed class CommandContext
{
    internal CommandContext(
        Command command,
        string query,
        string? modePrefix,
        string? promptValue,
        IReadOnlyList<Command> selected,
        IServiceProvider services,
        NavigationManager navigation,
        ICommandPaletteService palette,
        CancellationToken cancellationToken)
    {
        Command = command;
        Query = query;
        ModePrefix = modePrefix;
        PromptValue = promptValue;
        SelectedCommands = selected;
        Services = services;
        Navigation = navigation;
        Palette = palette;
        CancellationToken = cancellationToken;
    }

    /// <summary>The command being executed.</summary>
    public Command Command { get; }

    /// <summary>The search text at execution time (without a mode prefix).</summary>
    public string Query { get; }

    /// <summary>Active mode prefix (e.g. <c>&gt;</c>), or null for the default mode.</summary>
    public string? ModePrefix { get; }

    /// <summary>Text entered in a prompt step, when the command was reached via <see cref="CommandResult.Prompt"/>.</summary>
    public string? PromptValue { get; }

    /// <summary>In multi-select pages, the set of checked commands.</summary>
    public IReadOnlyList<Command> SelectedCommands { get; }

    public IServiceProvider Services { get; }

    public NavigationManager Navigation { get; }

    public ICommandPaletteService Palette { get; }

    public CancellationToken CancellationToken { get; }

    public T GetRequiredService<T>() where T : notnull
        => (T)(Services.GetService(typeof(T)) ?? throw new InvalidOperationException($"Service {typeof(T)} is not registered."));
}
