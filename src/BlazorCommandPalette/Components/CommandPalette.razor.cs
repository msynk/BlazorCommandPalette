using System.Diagnostics;
using BlazorCommandPalette.History;
using BlazorCommandPalette.Localization;
using BlazorCommandPalette.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorCommandPalette.Components;

/// <summary>
/// The command palette dialog. Place it once in your layout. Commands come from
/// <see cref="ICommandPaletteService"/>, from declarative <see cref="CommandItem"/> children, and from async sources.
/// </summary>
public partial class CommandPalette : ComponentBase, IAsyncDisposable
{
    private const string ModeHintTag = "__cp_mode_hint";

    private readonly string _id = $"cp-{Guid.NewGuid():N}"[..12];
    private readonly List<Command> _declarative = [];
    private readonly List<PageFrame> _pages = [];
    private readonly Dictionary<string, IReadOnlyList<Command>> _sourceResults = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingSources = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _subscriptions = [];

    private ElementReference _dialogEl;
    private ElementReference _inputEl;
    private ElementReference _listEl;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private DotNetObjectReference<CommandPalette>? _selfRef;

    private bool _isOpen;
    private bool _pendingOpen;
    private bool _pendingFocus;
    private bool _pendingScroll;
    private bool _isMac;
    private string _query = string.Empty;
    private int _activeIndex;
    private bool _loading;
    private string? _confirmingId;
    private string? _toast;
    private string _announcement = string.Empty;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _executeCts;
    private CancellationTokenSource? _toastCts;
    private bool _executing;
    private bool _pointerSelectionSuspended;
    private bool _inputPending;
    private int _inputVersion;
    private TimeSpan _lastSearchCost;

    private List<Command> _all = [];
    private HashSet<string> _parentIds = new(StringComparer.Ordinal);
    private IEnumerable<Command>? _lastCommandsParameter;
    private List<PaletteRow> _rows = [];
    private List<PaletteRow> _itemRows = [];
    private CommandMode? _mode;
    private string _effectiveQuery = string.Empty;
    private List<CommandMatch> _registryMatches = [];
    private (PageFrame? Page, CommandMode? Mode, string? Query) _registryMatchKey;

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private CommandPaletteService Service { get; set; } = default!;
    [Inject] private CommandPaletteOptions Options { get; set; } = default!;
    [Inject] private CommandRanker Ranker { get; set; } = default!;
    [Inject] private CommandHistory History { get; set; } = default!;

    // ---- Parameters -------------------------------------------------------------------------

    /// <summary>Declarative <see cref="CommandGroup"/> / <see cref="CommandItem"/> children.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Placeholder override for the root page.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Strings override (defaults to <see cref="CommandPaletteOptions.Strings"/>).</summary>
    [Parameter] public CommandPaletteStrings? Strings { get; set; }

    /// <summary>Extra CSS class on the dialog element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline style on the dialog element (e.g. <c>--cp-width: 720px</c>).</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Theme override: <c>auto</c>, <c>light</c> or <c>dark</c>.</summary>
    [Parameter] public string? Theme { get; set; }

    /// <summary>Render no built-in styles/classes beyond structural data attributes.</summary>
    [Parameter] public bool Unstyled { get; set; }

    /// <summary>Controlled open state (two-way bindable).</summary>
    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    [Parameter] public RenderFragment<PaletteItemContext>? ItemTemplate { get; set; }

    [Parameter] public RenderFragment<string>? GroupHeaderTemplate { get; set; }

    [Parameter] public RenderFragment<string>? EmptyTemplate { get; set; }

    [Parameter] public RenderFragment? LoadingTemplate { get; set; }

    [Parameter] public RenderFragment<PaletteStateContext>? HeaderTemplate { get; set; }

    [Parameter] public RenderFragment<PaletteStateContext>? FooterTemplate { get; set; }

    /// <summary>Renders a detail/preview pane for the active command (Raycast style).</summary>
    [Parameter] public RenderFragment<Command>? DetailTemplate { get; set; }

    /// <summary>Custom icon renderer. Receives the command; return nothing for no icon.</summary>
    [Parameter] public RenderFragment<Command>? IconTemplate { get; set; }

    /// <summary>Override <see cref="CommandPaletteOptions.Loop"/>.</summary>
    [Parameter] public bool? Loop { get; set; }

    [Parameter] public bool? VimBindings { get; set; }

    [Parameter] public bool? Virtualize { get; set; }

    [Parameter] public bool? ShowFooter { get; set; }

    [Parameter] public bool? ShowShortcuts { get; set; }

    [Parameter] public bool? HighlightMatches { get; set; }

    [Parameter] public bool? EnableHistory { get; set; }

    [Parameter] public bool? GroupWhenSearching { get; set; }

    /// <summary>Ignore mouse hover for selecting the active item (keyboard only).</summary>
    [Parameter] public bool DisablePointerSelection { get; set; }

    /// <summary>Toggle shortcut override (e.g. <c>Ctrl+Shift+P</c>).</summary>
    [Parameter] public string? ToggleShortcut { get; set; }

    /// <summary>Commands supplied directly to this instance (in addition to the service registry).</summary>
    [Parameter] public IEnumerable<Command>? Commands { get; set; }

    [Parameter] public EventCallback OnOpened { get; set; }

    [Parameter] public EventCallback OnClosed { get; set; }

    [Parameter] public EventCallback<string> OnQueryChanged { get; set; }

    [Parameter] public EventCallback<CommandExecutedEventArgs> OnCommandExecuted { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    // ---- Public API --------------------------------------------------------------------------

    /// <summary>Whether the dialog is open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Current raw query (including a mode prefix).</summary>
    public string Query => _query;

    /// <summary>Currently highlighted command, if any.</summary>
    public Command? ActiveCommand => _activeIndex >= 0 && _activeIndex < _itemRows.Count ? _itemRows[_activeIndex].Command : null;

    public Task OpenAsync(string? query = null, string? pageCommandId = null) => OpenCoreAsync(query, pageCommandId);

    public Task CloseAsync() => CloseCoreAsync();

    public Task ToggleAsync() => _isOpen ? CloseCoreAsync() : OpenCoreAsync(null, null);

    /// <summary>Sets the query programmatically.</summary>
    public Task SetQueryAsync(string query)
    {
        _query = query ?? string.Empty;
        return OnQueryUpdatedAsync();
    }

    // ---- Lifecycle ---------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        Service.OpenRequested += OpenCoreAsync;
        Service.CloseRequested += CloseCoreAsync;
        Service.ExecuteRequested += ExecuteFromServiceAsync;
        Service.CommandsChanged += OnRegistryChanged;
        History.Changed += OnHistoryChanged;
        Navigation.LocationChanged += OnLocationChanged;

        foreach (var source in Services.GetService(typeof(IEnumerable<ICommandSource>)) as IEnumerable<ICommandSource> ?? [])
        {
            if (!Service.Sources.Contains(source))
            {
                _subscriptions.Add(Service.RegisterSource(source));
            }
        }

        _pages.Add(new PageFrame { Title = S.Home });
        Rebuild();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(_lastCommandsParameter, Commands))
        {
            _lastCommandsParameter = Commands;
            Rebuild();
            SyncShortcuts();
        }

        if (Open && !_isOpen)
        {
            await OpenCoreAsync(null, null);
        }
        else if (!Open && _isOpen && OpenChanged.HasDelegate)
        {
            await CloseCoreAsync();
        }
    }

    // A keystroke whose search is still deferred (see OnInput) renders together with its results.
    protected override bool ShouldRender() => !_inputPending;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/BlazorCommandPalette/Components/CommandPalette.razor.js");
                _selfRef = DotNetObjectReference.Create(this);
                _handle = await _module.InvokeAsync<IJSObjectReference>("init", _dialogEl, _selfRef, BuildJsOptions());
                _isMac = await _handle.InvokeAsync<bool>("isMac");
                Service.SetPlatform(_isMac);
                StateHasChanged();
            }
            catch (JSException)
            {
                // Prerendering without JS or module blocked: palette will still render but without hotkeys.
            }
        }

        if (_handle is null)
        {
            return;
        }

        if (_pendingOpen)
        {
            _pendingOpen = false;
            await _handle.InvokeVoidAsync("open", _inputEl);
            _pendingScroll = true;
        }

        if (_pendingFocus)
        {
            _pendingFocus = false;
            await _handle.InvokeVoidAsync("focusInput", _inputEl);
        }

        if (_pendingScroll && _isOpen)
        {
            _pendingScroll = false;
            var active = _activeIndex >= 0 && _activeIndex < _itemRows.Count ? _itemRows[_activeIndex].ElementId : null;
            if (active is not null)
            {
                await _handle.InvokeVoidAsync("scrollIntoView", _listEl, active);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Service.OpenRequested -= OpenCoreAsync;
        Service.CloseRequested -= CloseCoreAsync;
        Service.ExecuteRequested -= ExecuteFromServiceAsync;
        Service.CommandsChanged -= OnRegistryChanged;
        History.Changed -= OnHistoryChanged;
        Navigation.LocationChanged -= OnLocationChanged;
        foreach (var s in _subscriptions)
        {
            s.Dispose();
        }

        _debounceCts?.Cancel();
        _executeCts?.Cancel();
        _toastCts?.Cancel();

        try
        {
            if (_handle is not null)
            {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }

        _selfRef?.Dispose();
    }

    // ---- Declarative registration (used by CommandItem) -------------------------------------

    internal void AddDeclarative(Command command)
    {
        _declarative.RemoveAll(c => c.Id == command.Id);
        _declarative.Add(command);
        Rebuild();
        SyncShortcuts();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>True when selecting the command opens a nested page (static, lazy, or declaratively registered children).</summary>
    internal bool HasPage(Command cmd) => cmd.HasChildren || _parentIds.Contains(cmd.Id);

    internal void RemoveDeclarative(string id)
    {
        if (_declarative.RemoveAll(c => c.Id == id) > 0)
        {
            Rebuild();
            SyncShortcuts();
            _ = InvokeAsync(StateHasChanged);
        }
    }

    // ---- Service / environment callbacks --------------------------------------------------------

    private void OnRegistryChanged() => _ = InvokeAsync(() =>
    {
        Rebuild();
        SyncShortcuts();
        StateHasChanged();
    });

    private void OnHistoryChanged() => _ = InvokeAsync(() =>
    {
        Rebuild();
        StateHasChanged();
    });

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (_isOpen && Options.CloseOnNavigation && !_executing)
        {
            _ = InvokeAsync(CloseCoreAsync);
        }
    }

    private Task ExecuteFromServiceAsync(Command command) => InvokeAsync(() => ExecuteCommandAsync(command, viaKeyboard: false));

    // ---- Open / close ----------------------------------------------------------------------------

    private async Task OpenCoreAsync(string? query, string? pageCommandId)
    {
        if (_isOpen && query is null && pageCommandId is null)
        {
            return;
        }

        // Closing already resets when ResetOnClose is set; an explicit query or page always starts fresh.
        if (query is not null || pageCommandId is not null)
        {
            ResetState();
        }

        if (pageCommandId is not null && Service.Find(pageCommandId) is { } page)
        {
            PushPage(new PageFrame { Title = page.Title, Command = page, Placeholder = page.ChildrenPlaceholder, MultiSelect = page.MultiSelectChildren, OnSubmit = page.Handler });
            if (page.ChildrenProvider is not null)
            {
                _ = LoadChildrenAsync(_pages[^1], page);
            }
        }

        if (query is not null)
        {
            _query = query;
        }

        var wasOpen = _isOpen;
        _isOpen = true;
        _pendingOpen = !wasOpen;
        Service.SetOpen(true);

        if (EffectiveEnableHistory)
        {
            _ = History.EnsureLoadedAsync();
        }

        Rebuild();
        TriggerSourceSearch();
        StateHasChanged();

        if (!wasOpen)
        {
            if (OpenChanged.HasDelegate)
            {
                await OpenChanged.InvokeAsync(true);
            }

            await OnOpened.InvokeAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        _inputPending = false;
        _debounceCts?.Cancel();
        _confirmingId = null;
        Service.SetOpen(false);

        if (_handle is not null)
        {
            try
            {
                await _handle.InvokeVoidAsync("close");
            }
            catch (JSException)
            {
            }
        }

        if (Options.ResetOnClose)
        {
            ResetState();
        }

        StateHasChanged();

        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }

        await OnClosed.InvokeAsync();
    }

    private void ResetState()
    {
        _query = string.Empty;
        _inputPending = false;
        _pages.Clear();
        _pages.Add(new PageFrame { Title = S.Home });
        _sourceResults.Clear();
        _pendingSources.Clear();
        _loading = false;
        _activeIndex = 0;
        _confirmingId = null;
        _toast = null;
    }

    // ---- Input ------------------------------------------------------------------------------------

    private async Task OnInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        if (_handle is null || _lastSearchCost < SlowSearchThreshold)
        {
            await OnQueryUpdatedAsync();
            return;
        }

        // Searching takes longer than a frame: let the keystroke paint first, and fold keys typed meanwhile into one search.
        // Rendering waits too (see ShouldRender), so the list never re-renders with stale rows in between.
        var version = ++_inputVersion;
        _inputPending = true;
        try
        {
            await _handle.InvokeVoidAsync("afterPaint");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException)
        {
        }

        if (_inputPending && version == _inputVersion)
        {
            await OnQueryUpdatedAsync();
        }
    }

    private async Task OnQueryUpdatedAsync()
    {
        _inputPending = false;
        _confirmingId = null;
        _activeIndex = 0;
        _pointerSelectionSuspended = true;
        var started = Stopwatch.GetTimestamp();
        Rebuild();
        _lastSearchCost = Stopwatch.GetElapsedTime(started);
        TriggerSourceSearch();
        _pendingScroll = true;
        if (OnQueryChanged.HasDelegate)
        {
            await OnQueryChanged.InvokeAsync(_query);
        }
    }

    /// <summary>Called from JS for navigation keys (already preventDefault-ed).</summary>
    [JSInvokable]
    public async Task OnKeyDown(string key, bool ctrl, bool shift, bool alt, bool meta)
    {
        if (!_isOpen)
        {
            return;
        }

        // Navigation acts on the results of everything typed so far.
        if (_inputPending)
        {
            await OnQueryUpdatedAsync();
        }

        // Browsers report Shift+letter as an upper-case key ("P"); match bindings case-insensitively.
        if (key.Length == 1)
        {
            key = key.ToLowerInvariant();
        }

        var mod = _isMac ? meta : ctrl;
        var page = CurrentPage;
        switch (key)
        {
            case "ArrowDown":
                Move(+1);
                break;
            case "ArrowUp":
                Move(-1);
                break;
            case "n" when ctrl && !shift:
            case "j" when ctrl && !shift && EffectiveVim:
                Move(+1);
                break;
            case "p" when ctrl && !shift:
            case "k" when ctrl && !shift && EffectiveVim:
                Move(-1);
                break;
            case "Home":
                SetActive(0);
                break;
            case "End":
                SetActive(_itemRows.Count - 1);
                break;
            case "PageDown":
                Move(+10, clamp: true);
                break;
            case "PageUp":
                Move(-10, clamp: true);
                break;
            case "Enter":
                if (page.Prompt is not null)
                {
                    await SubmitPromptAsync();
                }
                else if (mod && page.MultiSelect)
                {
                    await SubmitMultiSelectAsync();
                }
                else if (mod && ActiveCommand is { Actions.Count: > 0 } ac)
                {
                    // Mod+Enter runs the first secondary action.
                    await ExecuteCommandAsync(ac.Actions[0], viaKeyboard: true);
                }
                else if (ActiveCommand is { } cmd)
                {
                    await ExecuteCommandAsync(cmd, viaKeyboard: true);
                }

                break;
            case "Tab":
            case "ArrowRight" when string.IsNullOrEmpty(_query) || key == "Tab":
                if (ActiveCommand is { } target)
                {
                    await OpenActionsAsync(target);
                }

                break;
            case " " when page.MultiSelect && ActiveCommand is { } toggled:
                ToggleSelection(toggled);
                break;
            case "Backspace" when string.IsNullOrEmpty(_query) && _pages.Count > 1:
            case "ArrowLeft" when string.IsNullOrEmpty(_query) && _pages.Count > 1:
                PopPage();
                break;
            case "Escape":
                if (_confirmingId is not null)
                {
                    _confirmingId = null;
                    ShowToast(null);
                }
                else if (_pages.Count > 1)
                {
                    PopPage();
                }
                else
                {
                    await CloseCoreAsync();
                }

                break;
            case "Delete" when shift && ActiveCommand is { } removable && EffectiveEnableHistory:
                await History.RemoveAsync(removable.Id);
                break;
            case "p" when mod && shift && ActiveCommand is { } pinnable && EffectiveEnableHistory:
                await History.TogglePinAsync(pinnable.Id);
                break;
        }

        StateHasChanged();
    }

    [JSInvokable]
    public Task OnToggleShortcut() => InvokeAsync(ToggleAsync);

    [JSInvokable]
    public Task OnGlobalShortcut(string normalized) => InvokeAsync(async () =>
    {
        var cmd = _all.FirstOrDefault(c => c.Shortcut is not null && c.GlobalShortcut && c.Shortcut.Normalized == normalized);
        if (cmd is null || cmd.Disabled || cmd.IsVisible?.Invoke() == false)
        {
            return;
        }

        if (HasPage(cmd) || cmd.ConfirmMessage is not null)
        {
            await OpenCoreAsync(null, null);
        }

        await ExecuteCommandAsync(cmd, viaKeyboard: true);
        StateHasChanged();
    });

    [JSInvokable]
    public Task OnBackdropClick() => Options.CloseOnBackdropClick ? InvokeAsync(CloseCoreAsync) : Task.CompletedTask;

    [JSInvokable]
    public Task OnPointerMoved()
    {
        _pointerSelectionSuspended = false;
        return Task.CompletedTask;
    }

    private void Move(int delta, bool clamp = false)
    {
        if (_itemRows.Count == 0)
        {
            return;
        }

        var next = _activeIndex + delta;
        if (clamp || !EffectiveLoop)
        {
            next = Math.Clamp(next, 0, _itemRows.Count - 1);
        }
        else
        {
            next = ((next % _itemRows.Count) + _itemRows.Count) % _itemRows.Count;
        }

        SetActive(next);
    }

    private void SetActive(int index)
    {
        if (_itemRows.Count == 0)
        {
            _activeIndex = 0;
            return;
        }

        _activeIndex = Math.Clamp(index, 0, _itemRows.Count - 1);
        _confirmingId = null;
        _pendingScroll = true;
    }

    private void OnItemHover(PaletteRow row)
    {
        if (DisablePointerSelection || _pointerSelectionSuspended)
        {
            return;
        }

        if (row.ItemIndex != _activeIndex)
        {
            _activeIndex = row.ItemIndex;
        }
    }

    private Task OnItemClick(PaletteRow row)
    {
        _activeIndex = row.ItemIndex;
        return ExecuteCommandAsync(row.Command, viaKeyboard: false);
    }

    private Task OnBackClick()
    {
        if (_pages.Count > 1)
        {
            PopPage();
        }

        return Task.CompletedTask;
    }

    // ---- Pages -------------------------------------------------------------------------------------

    private PageFrame CurrentPage => _pages[^1];

    private void PushPage(PageFrame frame)
    {
        _pages.Add(frame);
        _query = frame.Prompt?.InitialValue ?? string.Empty;
        _inputPending = false;
        _activeIndex = 0;
        _confirmingId = null;
        _sourceResults.Clear();
        _pendingFocus = true;
        _pendingScroll = true;
        Rebuild();
    }

    private void PopPage()
    {
        if (_pages.Count <= 1)
        {
            return;
        }

        var frame = _pages[^1];
        _pages.RemoveAt(_pages.Count - 1);
        _query = frame.QueryBefore;
        _inputPending = false;
        _activeIndex = 0;
        _confirmingId = null;
        _sourceResults.Clear();
        _pendingFocus = true;
        _pendingScroll = true;
        Rebuild();
        TriggerSourceSearch();
    }

    private async Task LoadChildrenAsync(PageFrame frame, Command parent)
    {
        _loading = true;
        StateHasChanged();
        try
        {
            var ctx = CreateContext(parent, null, CancellationToken.None);
            var items = await parent.ChildrenProvider!(ctx, CancellationToken.None);
            if (_pages.Contains(frame))
            {
                frame.Items = items;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!Service.RaiseFailed(parent, ex))
            {
                throw;
            }
        }
        finally
        {
            _loading = false;
            Rebuild();
            StateHasChanged();
        }
    }

    private Task OpenActionsAsync(Command target)
    {
        var actions = new List<Command>(target.Actions ?? []);
        if (EffectiveEnableHistory && !target.ExcludeFromHistory && !target.Tags.Contains(ModeHintTag))
        {
            var pinned = History.IsPinned(target.Id);
            actions.Add(new Command
            {
                Id = $"__pin:{target.Id}",
                Title = pinned ? S.Unpin : S.Pin,
                Kind = CommandKind.Action,
                Icon = IconSvg("pin"),
                ExcludeFromHistory = true,
                Handler = async _ =>
                {
                    await History.TogglePinAsync(target.Id);
                    return CommandResult.Back;
                },
            });

            if (History.Get(target.Id) is { Count: > 0 })
            {
                actions.Add(new Command
                {
                    Id = $"__forget:{target.Id}",
                    Title = S.RemoveFromRecents,
                    Icon = IconSvg("clock"),
                    ExcludeFromHistory = true,
                    Handler = async _ =>
                    {
                        await History.RemoveAsync(target.Id);
                        return CommandResult.Back;
                    },
                });
            }
        }

        if (actions.Count == 0)
        {
            return Task.CompletedTask;
        }

        PushPage(new PageFrame
        {
            Title = target.Title,
            Items = actions,
            IsActions = true,
            Placeholder = S.Actions,
            QueryBefore = _query,
        });
        return Task.CompletedTask;
    }

    private void ToggleSelection(Command cmd)
    {
        var page = CurrentPage;
        if (!page.Selected.Remove(cmd.Id))
        {
            page.Selected.Add(cmd.Id);
        }
    }

    private async Task SubmitMultiSelectAsync()
    {
        var page = CurrentPage;
        var selected = (page.Items ?? _all.Where(c => c.Parent == page.Command?.Id).ToList())
            .Where(c => page.Selected.Contains(c.Id))
            .ToList();

        var owner = page.Command ?? new Command { Id = "__multiselect", Title = page.Title, ExcludeFromHistory = true };
        var submit = page.OnSubmit ?? page.Command?.Handler;
        if (submit is null)
        {
            await CloseCoreAsync();
            return;
        }

        await RunHandlerAsync(owner, submit, selected, null);
    }

    private async Task SubmitPromptAsync()
    {
        var page = CurrentPage;
        if (page.Prompt is null)
        {
            return;
        }

        var owner = page.Command ?? new Command { Id = "__prompt", Title = page.Title, ExcludeFromHistory = true };
        await RunHandlerAsync(owner, page.Prompt.OnSubmit, [], _query);
    }

    // ---- Execution -----------------------------------------------------------------------------------

    private async Task ExecuteCommandAsync(Command cmd, bool viaKeyboard)
    {
        if (_executing)
        {
            return;
        }

        if (cmd.Disabled)
        {
            ShowToast(cmd.DisabledReason ?? S.Disabled);
            return;
        }

        var page = CurrentPage;
        if (page.MultiSelect && !HasPage(cmd))
        {
            ToggleSelection(cmd);
            return;
        }

        if (cmd.Tags.Contains(ModeHintTag))
        {
            _query = cmd.Value ?? string.Empty;
            await OnQueryUpdatedAsync();
            _pendingFocus = true;
            return;
        }

        if (cmd.ConfirmMessage is not null && _confirmingId != cmd.Id)
        {
            _confirmingId = cmd.Id;
            ShowToast(S.ConfirmPrefix + cmd.ConfirmMessage, sticky: true);
            return;
        }

        _confirmingId = null;

        if (HasPage(cmd))
        {
            var hasRegisteredChildren = _parentIds.Contains(cmd.Id);
            var frame = new PageFrame
            {
                Title = cmd.Title,
                Command = cmd,
                Items = !hasRegisteredChildren && cmd.Children is { Count: > 0 } ? cmd.Children : null,
                Placeholder = cmd.ChildrenPlaceholder,
                MultiSelect = cmd.MultiSelectChildren,
                OnSubmit = cmd.Handler,
                QueryBefore = _query,
            };
            PushPage(frame);
            if (cmd.ChildrenProvider is not null)
            {
                await LoadChildrenAsync(frame, cmd);
            }

            return;
        }

        if (cmd.Handler is null && cmd.Href is not null)
        {
            await RunHandlerAsync(cmd, async ctx =>
            {
                if (cmd.OpenInNewTab && _handle is not null)
                {
                    await _handle.InvokeVoidAsync("openInNewTab", cmd.Href);
                }
                else
                {
                    ctx.Navigation.NavigateTo(cmd.Href, cmd.ForceLoad);
                }

                return CommandResult.Close;
            }, [], null);
            return;
        }

        if (cmd.Handler is null)
        {
            await CloseCoreAsync();
            return;
        }

        await RunHandlerAsync(cmd, cmd.Handler, [], null);
    }

    private async Task RunHandlerAsync(Command cmd, Func<CommandContext, Task<CommandResult>> handler, IReadOnlyList<Command> selected, string? promptValue)
    {
        _executeCts?.Cancel();
        _executeCts = new CancellationTokenSource();
        var ctx = CreateContext(cmd, promptValue, _executeCts.Token, selected);
        var sw = Stopwatch.StartNew();
        _executing = true;
        CommandResult result;
        try
        {
            result = await handler(ctx);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _executing = false;
            if (!Service.RaiseFailed(cmd, ex))
            {
                throw;
            }

            ShowToast(ex.Message);
            return;
        }
        finally
        {
            _executing = false;
        }

        sw.Stop();
        if (EffectiveEnableHistory && !cmd.ExcludeFromHistory && cmd.Id.Length > 0 && !cmd.Id.StartsWith("__", StringComparison.Ordinal))
        {
            _ = History.RecordAsync(cmd.Id);
        }

        Service.RaiseExecuted(cmd, result, sw.Elapsed);
        if (OnCommandExecuted.HasDelegate)
        {
            await OnCommandExecuted.InvokeAsync(new CommandExecutedEventArgs(cmd, result, sw.Elapsed));
        }

        await ApplyResultAsync(cmd, result);
    }

    private async Task ApplyResultAsync(Command cmd, CommandResult result)
    {
        switch (result)
        {
            case CommandResult.CloseResult:
                await CloseCoreAsync();
                break;
            case CommandResult.KeepOpenResult:
                Rebuild();
                _pendingFocus = true;
                break;
            case CommandResult.ResetResult:
                _pages.RemoveRange(1, _pages.Count - 1);
                _query = string.Empty;
                _inputPending = false;
                _activeIndex = 0;
                _pendingFocus = true;
                Rebuild();
                break;
            case CommandResult.BackResult:
                PopPage();
                break;
            case CommandResult.PageResult p:
                PushPage(new PageFrame
                {
                    Title = p.Title,
                    Command = null,
                    Items = p.Commands,
                    Placeholder = p.Placeholder,
                    MultiSelect = p.MultiSelect,
                    OnSubmit = p.OnSubmit,
                    QueryBefore = _query,
                });
                break;
            case CommandResult.PromptResult pr:
                PushPage(new PageFrame
                {
                    Title = pr.Title,
                    Command = cmd,
                    Prompt = pr,
                    Placeholder = pr.Placeholder ?? S.PromptPlaceholder,
                    QueryBefore = _query,
                });
                break;
            case CommandResult.ToastResult t:
                ShowToast(t.Message);
                if (!t.StayOpen)
                {
                    await Task.Delay(900);
                    await CloseCoreAsync();
                }

                break;
        }

        StateHasChanged();
    }

    private CommandContext CreateContext(Command cmd, string? promptValue, CancellationToken ct, IReadOnlyList<Command>? selected = null)
        => new(cmd, _effectiveQuery, _mode?.Prefix, promptValue, selected ?? [], Services, Navigation, Service, ct);

    private void ShowToast(string? message, bool sticky = false)
    {
        _toast = message;
        _toastCts?.Cancel();
        if (message is null || sticky)
        {
            return;
        }

        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        _ = Task.Delay(3000, token).ContinueWith(_ => InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested && _toast == message)
            {
                _toast = null;
                StateHasChanged();
            }
        }), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    // ---- Async sources --------------------------------------------------------------------------------

    private IEnumerable<ICommandSource> ActiveSources()
    {
        var page = CurrentPage;
        if (!page.IsRoot)
        {
            return [];
        }

        return _mode is not null ? _mode.Sources : Service.Sources;
    }

    private void TriggerSourceSearch()
    {
        _debounceCts?.Cancel();
        var sources = ActiveSources().Where(s => _effectiveQuery.Length >= s.MinQueryLength).ToList();
        if (sources.Count == 0)
        {
            // Every caller has just rebuilt; only stale source rows or the loading state need another pass.
            var stale = _sourceResults.Count > 0 || _loading;
            _sourceResults.Clear();
            _pendingSources.Clear();
            _loading = false;
            if (stale)
            {
                Rebuild(reuseRegistryMatches: true);
            }

            return;
        }

        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        var query = _effectiveQuery;
        var ctx = new CommandSourceContext(_mode?.Prefix, CurrentPage.Command?.Id, Services);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Options.DebounceMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await InvokeAsync(() =>
            {
                _loading = true;
                _pendingSources.Clear();
                foreach (var s in sources)
                {
                    _pendingSources.Add(s.Name);
                }

                StateHasChanged();
            });

            await Task.WhenAll(sources.Select(async source =>
            {
                IReadOnlyList<Command> results;
                try
                {
                    results = await source.SearchAsync(query, ctx, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    results = [];
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await InvokeAsync(() =>
                {
                    _sourceResults[source.Name] = results.Take(source.MaxResults).ToList();
                    _pendingSources.Remove(source.Name);
                    _loading = _pendingSources.Count > 0;
                    Rebuild(reuseRegistryMatches: true);
                    _pendingScroll = true;
                    StateHasChanged();
                });
            }));
        }, token);
    }

    // ---- Row building -------------------------------------------------------------------------------------

    private IEnumerable<Command> AllCommands()
    {
        foreach (var c in Service.Commands)
        {
            yield return c;
        }

        foreach (var c in _declarative)
        {
            yield return c;
        }

        if (Commands is not null)
        {
            foreach (var c in Commands)
            {
                yield return c;
            }
        }
    }

    /// <summary>Takes one snapshot of every command source so rendering never rescans the registry per row.</summary>
    private void RefreshSnapshot()
    {
        _all = AllCommands().ToList();
        _parentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in _all)
        {
            if (c.Parent is not null)
            {
                _parentIds.Add(c.Parent);
            }
        }
    }

    /// <param name="reuseRegistryMatches">Only async source results changed: keep the registry matches computed for this query.</param>
    private void Rebuild(bool reuseRegistryMatches = false)
    {
        var page = CurrentPage;
        ResolveMode();
        var key = (page, _mode, _effectiveQuery);
        var reuse = reuseRegistryMatches && _registryMatchKey == key;
        if (!reuse)
        {
            RefreshSnapshot();
        }

        var rows = new List<PaletteRow>();
        var items = new List<PaletteRow>();

        if (page.Prompt is not null)
        {
            _rows = rows;
            _itemRows = items;
            _announcement = string.Empty;
            return;
        }

        var hasQuery = _effectiveQuery.Length > 0;
        var matcher = Options.Matcher;
        var query = CommandQuery.Parse(_effectiveQuery);

        // Matching the registry dominates the cost of a keystroke; async source results arriving later reuse it.
        if (!reuse)
        {
            _registryMatches = MatchCandidates(page, matcher, query);
            _registryMatchKey = key;
        }

        var matches = new List<CommandMatch>(_registryMatches);

        // Interleave sources that opt into matcher ranking.
        var blockSources = new List<(string Name, List<CommandMatch> Items)>();
        foreach (var source in ActiveSources())
        {
            if (!_sourceResults.TryGetValue(source.Name, out var results) || results.Count == 0)
            {
                continue;
            }

            if (source.ApplyMatcher)
            {
                foreach (var c in results)
                {
                    var scoped = c.Group is null ? c.With(b => b.Group(source.Name)) : c;
                    var m = matcher.Match(scoped, query, Options.Weights);
                    if (m is not null)
                    {
                        matches.Add(m with { FromSource = true, SourceName = source.Name });
                    }
                }
            }
            else
            {
                var block = results.Select((c, i) =>
                {
                    var m = hasQuery ? matcher.Match(c, query, Options.Weights) : null;
                    return new CommandMatch(c, 0, m?.TitlePositions ?? [], m?.SubtitlePositions ?? [], null) { Rank = -i, FromSource = true, SourceName = source.Name };
                }).ToList();
                blockSources.Add((source.Name, block));
            }
        }

        var ranked = Ranker.Rank(matches, hasQuery, id => EffectiveEnableHistory ? History.Frecency(id) : 0, Options.MaxResults);

        var itemCounter = 0;
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        void AddHeader(string group) => rows.Add(new PaletteRow(PaletteRowKind.GroupHeader, group, null, -1, $"{_id}-g-{rows.Count}"));

        void AddItem(CommandMatch m, string? group)
        {
            var row = new PaletteRow(PaletteRowKind.Item, group, m, itemCounter, $"{_id}-i-{itemCounter}");
            itemCounter++;
            rows.Add(row);
            items.Add(row);
        }

        // Pinned & recents (root, default mode, empty query).
        if (!hasQuery && page.IsRoot && _mode is null && EffectiveEnableHistory && History.IsLoaded)
        {
            var byId = ranked.ToDictionary(m => m.Command.Id, StringComparer.Ordinal);
            var pinned = History.Pinned().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (pinned.Count > 0)
            {
                AddHeader(S.Pinned);
                foreach (var m in pinned)
                {
                    AddItem(m, S.Pinned);
                    usedIds.Add(m.Command.Id);
                }
            }

            var recents = History.Recents(Options.MaxRecents * 2)
                .Where(id => byId.ContainsKey(id) && !usedIds.Contains(id))
                .Take(Options.MaxRecents)
                .Select(id => byId[id])
                .ToList();
            if (recents.Count > 0)
            {
                AddHeader(S.Recent);
                foreach (var m in recents)
                {
                    AddItem(m with { Command = m.Command }, S.Recent);
                    usedIds.Add(m.Command.Id);
                }
            }
        }

        var remaining = ranked.Where(m => !usedIds.Contains(m.Command.Id)).ToList();
        var grouped = !hasQuery || EffectiveGroupWhenSearching;

        if (grouped)
        {
            var groups = remaining
                .GroupBy(m => m.Command.Group ?? string.Empty)
                .Select(g => new { Name = g.Key, Items = g.ToList(), Best = g.Max(x => x.Rank), Order = GroupOrderIndex(g.Key) })
                .OrderBy(g => g.Name.Length == 0 ? 0 : 1) // ungrouped items first
                .ThenBy(g => hasQuery ? 0 : g.Order)
                .ThenByDescending(g => g.Best)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var g in groups)
            {
                if (g.Name.Length > 0)
                {
                    AddHeader(g.Name);
                }

                foreach (var m in g.Items)
                {
                    AddItem(m, g.Name.Length > 0 ? g.Name : null);
                }
            }
        }
        else
        {
            foreach (var m in remaining)
            {
                AddItem(m, null);
            }
        }

        foreach (var (name, block) in blockSources)
        {
            AddHeader(name);
            foreach (var m in block)
            {
                AddItem(m, name);
            }
        }

        _rows = rows;
        _itemRows = items;
        if (_activeIndex >= items.Count)
        {
            _activeIndex = Math.Max(0, items.Count - 1);
        }

        // Skip disabled items as initial selection.
        if (items.Count > 0 && items[_activeIndex].Command.Disabled)
        {
            var firstEnabled = items.FindIndex(r => !r.Command.Disabled);
            if (firstEnabled >= 0)
            {
                _activeIndex = firstEnabled;
            }
        }

        _announcement = items.Count switch
        {
            0 when _loading => S.Loading,
            0 => S.NoResultsAnnouncement,
            1 => S.OneResultAnnouncement,
            _ => S.Format(S.ResultsAnnouncement, items.Count),
        };
    }

    private List<CommandMatch> MatchCandidates(PageFrame page, ICommandMatcher matcher, CommandQuery query)
    {
        var matches = new List<CommandMatch>();
        foreach (var c in Candidates(page))
        {
            var m = matcher.Match(c, query, Options.Weights);
            if (m is null)
            {
                if (c.AlwaysShow)
                {
                    matches.Add(new CommandMatch(c, double.NegativeInfinity, [], [], null));
                }

                continue;
            }

            matches.Add(m);
        }

        return matches;
    }

    private int GroupOrderIndex(string group)
    {
        var idx = Options.GroupOrder.FindIndex(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? int.MaxValue : idx;
    }

    private void ResolveMode()
    {
        _mode = null;
        _effectiveQuery = _query.Trim();
        if (!CurrentPage.IsRoot)
        {
            return;
        }

        foreach (var mode in Service.Modes.OrderByDescending(m => m.Prefix.Length))
        {
            if (_query.StartsWith(mode.Prefix, StringComparison.Ordinal))
            {
                _mode = mode;
                _effectiveQuery = _query[mode.Prefix.Length..].Trim();
                return;
            }
        }
    }

    private IEnumerable<Command> Candidates(PageFrame page)
    {
        IEnumerable<Command> source;
        if (page.Items is not null)
        {
            source = page.Items;
        }
        else if (page.Command is not null)
        {
            source = _all.Where(c => c.Parent == page.Command.Id);
        }
        else
        {
            source = _all.Where(c => c.Parent is null);
            if (_mode is not null)
            {
                source = source.Where(_mode.Accepts);
            }
            else if (Options.ShowModeHints && _effectiveQuery.Length == 0)
            {
                source = source.Concat(ModeHints());
            }
        }

        return source.Where(c => c.IsVisible?.Invoke() != false);
    }

    private IEnumerable<Command> ModeHints() => Service.Modes.Where(m => m.ShowHint).Select(m => new Command
    {
        Id = $"__mode:{m.Prefix}",
        Title = m.Name,
        Subtitle = m.Description,
        Group = S.Modes,
        Kind = CommandKind.Search,
        Value = m.Prefix,
        Badge = m.Prefix,
        Priority = CommandPriority.Lowest,
        ExcludeFromHistory = true,
        Tags = [ModeHintTag],
    });

    // ---- JS plumbing ---------------------------------------------------------------------------------------

    private object BuildJsOptions()
    {
        var toggles = new List<string>();
        if (ToggleShortcut is not null && KeyboardShortcut.TryParse(ToggleShortcut, out var ts))
        {
            toggles.Add(ts.Normalized);
        }
        else if (Options.ToggleShortcut is not null)
        {
            toggles.Add(Options.ToggleShortcut.Normalized);
        }

        toggles.AddRange(Options.AdditionalToggleShortcuts.Select(s => s.Normalized));

        return new
        {
            toggleShortcuts = toggles,
            commandShortcuts = GlobalShortcutStrings(),
            suppressInEditable = Options.SuppressShortcutsInEditableElements,
            sequenceTimeout = Options.SequenceTimeoutMs,
            vimBindings = EffectiveVim,
        };
    }

    private List<string> GlobalShortcutStrings() => AllCommands()
        .Where(c => c.Shortcut is not null && c.GlobalShortcut)
        .Select(c => c.Shortcut!.Normalized)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private void SyncShortcuts()
    {
        if (_handle is null)
        {
            return;
        }

        _ = _handle.InvokeVoidAsync("setCommandShortcuts", GlobalShortcutStrings()).AsTask().ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    // ---- Effective settings ----------------------------------------------------------------------------------

    private CommandPaletteStrings S => Strings ?? Options.Strings;

    private bool EffectiveLoop => Loop ?? Options.Loop;

    private bool EffectiveVim => VimBindings ?? Options.VimBindings;

    private bool EffectiveVirtualize => Virtualize ?? Options.Virtualize;

    private bool EffectiveShowFooter => ShowFooter ?? Options.ShowFooter;

    private bool EffectiveShowShortcuts => ShowShortcuts ?? Options.ShowShortcuts;

    private bool EffectiveHighlight => HighlightMatches ?? Options.HighlightMatches;

    private bool EffectiveEnableHistory => EnableHistory ?? Options.EnableHistory;

    private bool EffectiveGroupWhenSearching => GroupWhenSearching ?? Options.GroupWhenSearching;

    private string EffectivePlaceholder => CurrentPage.Placeholder ?? (_mode?.Placeholder) ?? Placeholder ?? S.Placeholder;

    private string EffectiveTheme => Theme ?? Options.Theme;

    /// <summary>
    /// Searches slower than this defer to the next frame so typing stays responsive. Only on WebAssembly, where search and
    /// painting share one thread; on the server each keystroke already round-trips, so another hop would only add latency.
    /// </summary>
    internal TimeSpan SlowSearchThreshold { get; set; } = OperatingSystem.IsBrowser() ? TimeSpan.FromMilliseconds(8) : TimeSpan.MaxValue;

    private IReadOnlyList<string> Breadcrumbs => _pages.Skip(1).Select(p => p.Title).ToList();

    private PaletteStateContext StateContext => new(
        _effectiveQuery,
        _mode?.Prefix,
        _mode?.Name,
        Breadcrumbs,
        _itemRows.Count,
        _activeIndex,
        ActiveCommand,
        _loading,
        CurrentPage.MultiSelect,
        CurrentPage.Selected.Count,
        _toast,
        _isMac);

    private string? ActiveDescendant => _itemRows.Count > 0 && _activeIndex < _itemRows.Count ? _itemRows[_activeIndex].ElementId : null;
}
