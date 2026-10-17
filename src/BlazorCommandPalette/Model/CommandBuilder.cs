using Microsoft.AspNetCore.Components;

namespace BlazorCommandPalette;

/// <summary>Fluent builder for <see cref="Command"/>.</summary>
public sealed class CommandBuilder
{
    private string? _id;
    private string _title;
    private string? _subtitle;
    private string? _description;
    private readonly List<string> _keywords;
    private string? _group;
    private RenderFragment? _icon;
    private string? _iconCss;
    private KeyboardShortcut? _shortcut;
    private bool _globalShortcut = true;
    private int _priority;
    private string? _parent;
    private List<Command>? _children;
    private Func<CommandContext, CancellationToken, Task<IReadOnlyList<Command>>>? _childrenProvider;
    private string? _childrenPlaceholder;
    private bool _multiSelect;
    private List<Command>? _actions;
    private Func<CommandContext, Task<CommandResult>>? _handler;
    private string? _href;
    private bool _forceLoad;
    private bool _newTab;
    private CommandKind _kind = CommandKind.Action;
    private bool _disabled;
    private string? _disabledReason;
    private Func<bool>? _isVisible;
    private bool _destructive;
    private string? _confirm;
    private string? _badge;
    private string? _value;
    private bool? _checked;
    private bool _alwaysShow;
    private bool _excludeFromHistory;
    private object? _data;
    private readonly List<string> _tags;

    public CommandBuilder(string title)
    {
        _title = title;
        _keywords = [];
        _tags = [];
    }

    internal CommandBuilder(Command c)
    {
        _id = c.Id; // copies keep their identity even if the title changes
        _title = c.Title;
        _subtitle = c.Subtitle;
        _description = c.Description;
        _keywords = [.. c.Keywords];
        _group = c.Group;
        _icon = c.Icon;
        _iconCss = c.IconCss;
        _shortcut = c.Shortcut;
        _globalShortcut = c.GlobalShortcut;
        _priority = c.Priority;
        _parent = c.Parent;
        _children = c.Children is null ? null : [.. c.Children];
        _childrenProvider = c.ChildrenProvider;
        _childrenPlaceholder = c.ChildrenPlaceholder;
        _multiSelect = c.MultiSelectChildren;
        _actions = c.Actions is null ? null : [.. c.Actions];
        _handler = c.Handler;
        _href = c.Href;
        _forceLoad = c.ForceLoad;
        _newTab = c.OpenInNewTab;
        _kind = c.Kind;
        _disabled = c.Disabled;
        _disabledReason = c.DisabledReason;
        _isVisible = c.IsVisible;
        _destructive = c.Destructive;
        _confirm = c.ConfirmMessage;
        _badge = c.Badge;
        _value = c.Value;
        _checked = c.Checked;
        _alwaysShow = c.AlwaysShow;
        _excludeFromHistory = c.ExcludeFromHistory;
        _data = c.Data;
        _tags = [.. c.Tags];
    }

    public CommandBuilder Id(string id) { _id = id; return this; }
    public CommandBuilder Title(string title) { _title = title; return this; }
    public CommandBuilder Subtitle(string? subtitle) { _subtitle = subtitle; return this; }
    public CommandBuilder Description(string? description) { _description = description; return this; }
    public CommandBuilder Keywords(params string[] keywords) { _keywords.AddRange(keywords); return this; }
    public CommandBuilder Group(string? group) { _group = group; return this; }
    public CommandBuilder Icon(RenderFragment icon) { _icon = icon; return this; }
    public CommandBuilder Icon(string iconCss) { _iconCss = iconCss; return this; }
    public CommandBuilder Shortcut(string shortcut, bool global = true) { _shortcut = KeyboardShortcut.Parse(shortcut); _globalShortcut = global; return this; }
    public CommandBuilder Shortcut(KeyboardShortcut shortcut, bool global = true) { _shortcut = shortcut; _globalShortcut = global; return this; }
    public CommandBuilder Priority(int priority) { _priority = priority; return this; }
    public CommandBuilder Parent(string? parentId) { _parent = parentId; return this; }
    public CommandBuilder Children(params Command[] children) { (_children ??= []).AddRange(children); if (_kind == CommandKind.Action) { _kind = CommandKind.Page; } return this; }
    public CommandBuilder Children(IEnumerable<Command> children) { (_children ??= []).AddRange(children); if (_kind == CommandKind.Action) { _kind = CommandKind.Page; } return this; }
    public CommandBuilder Children(Func<CommandContext, CancellationToken, Task<IReadOnlyList<Command>>> provider, string? placeholder = null) { _childrenProvider = provider; _childrenPlaceholder = placeholder; if (_kind == CommandKind.Action) { _kind = CommandKind.Page; } return this; }
    public CommandBuilder ChildrenPlaceholder(string? placeholder) { _childrenPlaceholder = placeholder; return this; }
    public CommandBuilder MultiSelect(bool multiSelect = true) { _multiSelect = multiSelect; return this; }
    public CommandBuilder Actions(params Command[] actions) { (_actions ??= []).AddRange(actions); return this; }
    public CommandBuilder Handler(Func<CommandContext, Task<CommandResult>> handler) { _handler = handler; return this; }
    public CommandBuilder Handler(Func<CommandContext, Task> handler) { _handler = async ctx => { await handler(ctx).ConfigureAwait(false); return CommandResult.Close; }; return this; }
    public CommandBuilder Handler(Action<CommandContext> handler) { _handler = ctx => { handler(ctx); return Task.FromResult(CommandResult.Close); }; return this; }
    public CommandBuilder Handler(Action handler) { _handler = _ => { handler(); return Task.FromResult(CommandResult.Close); }; return this; }
    public CommandBuilder Href(string href, bool forceLoad = false, bool newTab = false) { _href = href; _forceLoad = forceLoad; _newTab = newTab; _kind = CommandKind.Navigation; return this; }
    public CommandBuilder Kind(CommandKind kind) { _kind = kind; return this; }
    public CommandBuilder Disabled(bool disabled = true, string? reason = null) { _disabled = disabled; _disabledReason = reason; return this; }
    public CommandBuilder VisibleWhen(Func<bool> predicate) { _isVisible = predicate; return this; }
    public CommandBuilder Destructive(string? confirmMessage = null) { _destructive = true; _confirm = confirmMessage; return this; }
    public CommandBuilder Confirm(string message) { _confirm = message; return this; }
    public CommandBuilder Badge(string? badge) { _badge = badge; return this; }
    public CommandBuilder Value(string? value) { _value = value; if (_kind == CommandKind.Action) { _kind = CommandKind.Setting; } return this; }
    public CommandBuilder Checked(bool? isChecked) { _checked = isChecked; _kind = CommandKind.Toggle; return this; }
    public CommandBuilder AlwaysShow(bool alwaysShow = true) { _alwaysShow = alwaysShow; return this; }
    public CommandBuilder ExcludeFromHistory(bool exclude = true) { _excludeFromHistory = exclude; return this; }
    public CommandBuilder Data(object? data) { _data = data; return this; }
    public CommandBuilder Tags(params string[] tags) { _tags.AddRange(tags); return this; }

    public Command Build() => new()
    {
        Id = _id!, // null keeps the id derived (see Command.Id)
        Title = _title,
        Subtitle = _subtitle,
        Description = _description,
        Keywords = _keywords,
        Group = _group,
        Icon = _icon,
        IconCss = _iconCss,
        Shortcut = _shortcut,
        GlobalShortcut = _globalShortcut,
        Priority = _priority,
        Parent = _parent,
        Children = _children,
        ChildrenProvider = _childrenProvider,
        ChildrenPlaceholder = _childrenPlaceholder,
        MultiSelectChildren = _multiSelect,
        Actions = _actions,
        Handler = _handler,
        Href = _href,
        ForceLoad = _forceLoad,
        OpenInNewTab = _newTab,
        Kind = _kind,
        Disabled = _disabled,
        DisabledReason = _disabledReason,
        IsVisible = _isVisible,
        Destructive = _destructive,
        ConfirmMessage = _confirm,
        Badge = _badge,
        Value = _value,
        Checked = _checked,
        AlwaysShow = _alwaysShow,
        ExcludeFromHistory = _excludeFromHistory,
        Data = _data,
        Tags = _tags,
    };

    public static implicit operator Command(CommandBuilder b) => b.Build();
}

/// <summary>Entry points for the fluent API: <c>Cmd.New("Title").Href("/").Build()</c>.</summary>
public static class Cmd
{
    public static CommandBuilder New(string title) => new(title);
    public static CommandBuilder Navigate(string title, string href) => new CommandBuilder(title).Href(href);
    public static CommandBuilder Action(string title, Action action) => new CommandBuilder(title).Handler(action);
    public static CommandBuilder Action(string title, Func<Task> action) => new CommandBuilder(title).Handler(_ => action());
    public static CommandBuilder Action(string title, Func<CommandContext, Task<CommandResult>> action) => new CommandBuilder(title).Handler(action);
    public static CommandBuilder Page(string title, params Command[] children) => new CommandBuilder(title).Kind(CommandKind.Page).Children(children);
}
