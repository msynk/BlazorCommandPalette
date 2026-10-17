using BlazorCommandPalette.Search;

namespace BlazorCommandPalette;

/// <summary>
/// Scoped service that owns the command registry and the open/closed state of the palette.
/// Inject it anywhere to register commands, open the palette, or react to executions.
/// </summary>
public interface ICommandPaletteService
{
    /// <summary>Whether a palette component is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>True once the palette has detected a macOS / iOS browser (use it to render ⌘ instead of Ctrl).</summary>
    bool IsMac { get; }

    /// <summary>All registered commands (root and nested).</summary>
    IReadOnlyCollection<Command> Commands { get; }

    /// <summary>Registered modes.</summary>
    IReadOnlyCollection<CommandMode> Modes { get; }

    /// <summary>Async sources used by the default mode.</summary>
    IReadOnlyCollection<ICommandSource> Sources { get; }

    /// <summary>Registers commands. Dispose the returned handle to remove them (e.g. in a page's <c>Dispose</c>).</summary>
    IDisposable Register(params Command[] commands);

    /// <summary>Registers commands. Dispose the returned handle to remove them.</summary>
    IDisposable Register(IEnumerable<Command> commands);

    /// <summary>Replaces a command with the same <see cref="Command.Id"/> (or adds it). Useful for toggles whose label changes.</summary>
    void Update(Command command);

    /// <summary>Removes a command by id.</summary>
    bool Unregister(string commandId);

    /// <summary>Looks up a command by id across all registrations.</summary>
    Command? Find(string commandId);

    IDisposable RegisterMode(CommandMode mode);

    IDisposable RegisterSource(ICommandSource source);

    /// <summary>Opens the palette, optionally with an initial query (a mode prefix is honoured) or directly inside a command's page.</summary>
    Task OpenAsync(string? query = null, string? pageCommandId = null);

    Task CloseAsync();

    Task ToggleAsync();

    /// <summary>Executes a command programmatically as if selected.</summary>
    Task ExecuteAsync(string commandId);

    /// <summary>Fired whenever the registry changes.</summary>
    event Action? CommandsChanged;

    event Action? Opened;

    event Action? Closed;

    event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    event EventHandler<CommandErrorEventArgs>? CommandFailed;
}

public sealed class CommandExecutedEventArgs(Command command, CommandResult result, TimeSpan duration) : EventArgs
{
    public Command Command { get; } = command;

    public CommandResult Result { get; } = result;

    public TimeSpan Duration { get; } = duration;
}

public sealed class CommandErrorEventArgs(Command command, Exception exception) : EventArgs
{
    public Command Command { get; } = command;

    public Exception Exception { get; } = exception;

    /// <summary>Set to true to prevent the exception from being rethrown.</summary>
    public bool Handled { get; set; }
}
