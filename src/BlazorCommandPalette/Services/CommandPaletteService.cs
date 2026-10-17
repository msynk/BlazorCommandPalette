using BlazorCommandPalette.Search;

namespace BlazorCommandPalette;

/// <summary>Default <see cref="ICommandPaletteService"/>. One instance per scope (circuit / browser tab).</summary>
public sealed class CommandPaletteService : ICommandPaletteService
{
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];
    private readonly Dictionary<string, Command> _byId = new(StringComparer.Ordinal);
    private readonly List<CommandMode> _modes = [];
    private readonly List<ICommandSource> _sources = [];
    private readonly CommandPaletteOptions _options;

    public CommandPaletteService(CommandPaletteOptions options)
    {
        _options = options;
        if (options.Commands.Count > 0)
        {
            Register(options.Commands);
        }

        _modes.AddRange(options.Modes);
        _sources.AddRange(options.Sources);
    }

    public bool IsOpen { get; private set; }

    public bool IsMac { get; private set; }

    /// <summary>Raised when the palette detected the client platform (internal plumbing for <c>CommandPaletteTrigger</c>).</summary>
    internal event Action? PlatformDetected;

    internal void SetPlatform(bool isMac)
    {
        IsMac = isMac;
        PlatformDetected?.Invoke();
    }

    public IReadOnlyCollection<Command> Commands
    {
        get
        {
            lock (_gate)
            {
                return _byId.Values.ToList();
            }
        }
    }

    public IReadOnlyCollection<CommandMode> Modes
    {
        get
        {
            lock (_gate)
            {
                return _modes.ToList();
            }
        }
    }

    public IReadOnlyCollection<ICommandSource> Sources
    {
        get
        {
            lock (_gate)
            {
                return _sources.ToList();
            }
        }
    }

    public event Action? CommandsChanged;

    public event Action? Opened;

    public event Action? Closed;

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public event EventHandler<CommandErrorEventArgs>? CommandFailed;

    /// <summary>The component asks the service to open with these arguments (internal plumbing).</summary>
    internal event Func<string?, string?, Task>? OpenRequested;

    internal event Func<Task>? CloseRequested;

    internal event Func<Command, Task>? ExecuteRequested;

    public IDisposable Register(params Command[] commands) => Register((IEnumerable<Command>)commands);

    public IDisposable Register(IEnumerable<Command> commands)
    {
        var list = Flatten(commands).ToList();
        var reg = new Registration(this, list);
        lock (_gate)
        {
            _registrations.Add(reg);
            Rebuild();
        }

        CommandsChanged?.Invoke();
        return reg;
    }

    public void Update(Command command)
    {
        lock (_gate)
        {
            var replaced = false;
            foreach (var reg in _registrations)
            {
                var idx = reg.Commands.FindIndex(c => c.Id == command.Id);
                if (idx >= 0)
                {
                    reg.Commands[idx] = command;
                    replaced = true;
                }
            }

            if (!replaced)
            {
                _registrations.Add(new Registration(this, [command]));
            }

            Rebuild();
        }

        CommandsChanged?.Invoke();
    }

    public bool Unregister(string commandId)
    {
        bool removed;
        lock (_gate)
        {
            removed = false;
            foreach (var reg in _registrations)
            {
                removed |= reg.Commands.RemoveAll(c => c.Id == commandId) > 0;
            }

            if (removed)
            {
                Rebuild();
            }
        }

        if (removed)
        {
            CommandsChanged?.Invoke();
        }

        return removed;
    }

    public Command? Find(string commandId)
    {
        lock (_gate)
        {
            return _byId.GetValueOrDefault(commandId);
        }
    }

    public IDisposable RegisterMode(CommandMode mode)
    {
        lock (_gate)
        {
            _modes.RemoveAll(m => m.Prefix == mode.Prefix);
            _modes.Add(mode);
        }

        CommandsChanged?.Invoke();
        return new ActionDisposable(() =>
        {
            lock (_gate)
            {
                _modes.Remove(mode);
            }

            CommandsChanged?.Invoke();
        });
    }

    public IDisposable RegisterSource(ICommandSource source)
    {
        lock (_gate)
        {
            _sources.Add(source);
        }

        CommandsChanged?.Invoke();
        return new ActionDisposable(() =>
        {
            lock (_gate)
            {
                _sources.Remove(source);
            }

            CommandsChanged?.Invoke();
        });
    }

    public Task OpenAsync(string? query = null, string? pageCommandId = null)
        => OpenRequested?.Invoke(query, pageCommandId) ?? Task.CompletedTask;

    public Task CloseAsync() => CloseRequested?.Invoke() ?? Task.CompletedTask;

    public Task ToggleAsync() => IsOpen ? CloseAsync() : OpenAsync();

    public Task ExecuteAsync(string commandId)
    {
        var cmd = Find(commandId) ?? throw new KeyNotFoundException($"Command '{commandId}' is not registered.");
        return ExecuteRequested?.Invoke(cmd) ?? Task.CompletedTask;
    }

    internal void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        if (open)
        {
            Opened?.Invoke();
        }
        else
        {
            Closed?.Invoke();
        }
    }

    internal void RaiseExecuted(Command command, CommandResult result, TimeSpan duration)
        => CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, result, duration));

    internal bool RaiseFailed(Command command, Exception ex)
    {
        var args = new CommandErrorEventArgs(command, ex);
        CommandFailed?.Invoke(this, args);
        return args.Handled;
    }

    internal CommandPaletteOptions Options => _options;

    /// <summary>Flattens static children into the registry with <see cref="Command.Parent"/> set so nested pages work uniformly.</summary>
    private static IEnumerable<Command> Flatten(IEnumerable<Command> commands, string? parent = null)
    {
        foreach (var c in commands)
        {
            var cmd = parent is not null && c.Parent is null
                ? c.With(b => b.Parent(parent).Id(c.HasExplicitId ? c.Id : CommandIdGenerator.FromTitle(c.Title, c.Group, parent)))
                : c;
            yield return cmd;
            if (cmd.Children is { Count: > 0 })
            {
                foreach (var child in Flatten(cmd.Children, cmd.Id))
                {
                    yield return child;
                }
            }
        }
    }

    private void Rebuild()
    {
        _byId.Clear();
        foreach (var c in _registrations.SelectMany(r => r.Commands))
        {
            _byId[c.Id] = c; // later registrations win, allowing pages to override global commands
        }
    }

    private void Remove(Registration reg)
    {
        lock (_gate)
        {
            if (!_registrations.Remove(reg))
            {
                return;
            }

            Rebuild();
        }

        CommandsChanged?.Invoke();
    }

    private sealed class Registration(CommandPaletteService owner, List<Command> commands) : IDisposable
    {
        public List<Command> Commands { get; } = commands;

        public void Dispose() => owner.Remove(this);
    }

    private sealed class ActionDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
