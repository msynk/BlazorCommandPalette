using BlazorCommandPalette.Components;
using BlazorCommandPalette.Search;
using BlazorCommandPalette.History;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorCommandPalette.Tests;

public class CommandPaletteComponentTests : BunitContext
{
    private readonly ICommandPaletteService _service;
    private readonly BunitJSModuleInterop _jsHandle;

    public CommandPaletteComponentTests()
    {
        Services.AddCommandPalette();
        Services.AddCommandPaletteHistoryStore<InMemoryCommandHistoryStore>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var module = JSInterop.SetupModule("./_content/BlazorCommandPalette/Components/CommandPalette.razor.js");
        var handle = _jsHandle = module.SetupModule("init", _ => true);
        handle.Setup<bool>("isMac").SetResult(false);
        handle.SetupVoid("open", _ => true).SetVoidResult();
        handle.SetupVoid("close").SetVoidResult();
        handle.SetupVoid("focusInput", _ => true).SetVoidResult();
        handle.SetupVoid("scrollIntoView", _ => true).SetVoidResult();
        handle.SetupVoid("setCommandShortcuts", _ => true).SetVoidResult();
        handle.SetupVoid("dispose").SetVoidResult();

        _service = Services.GetRequiredService<ICommandPaletteService>();
    }

    private IRenderedComponent<CommandPalette> RenderPalette() => Render<CommandPalette>();

    [Fact]
    public void Renders_closed_dialog_initially()
    {
        var cut = RenderPalette();
        var dialog = cut.Find("dialog");
        Assert.Equal("false", dialog.GetAttribute("data-open"));
        Assert.Empty(cut.FindAll("[data-cp=item]"));
    }

    [Fact]
    public async Task Opening_lists_registered_commands_grouped()
    {
        _service.Register(
            Cmd.Navigate("Home", "/").Group("Navigation"),
            Cmd.Navigate("Settings", "/settings").Group("Navigation"),
            Cmd.Action("Toggle theme", () => { }).Group("Preferences"));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("dialog").GetAttribute("data-open"));
            Assert.Equal(3, cut.FindAll("[data-cp=item]").Count);
            var headings = cut.FindAll("[data-cp=group-heading]").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("Navigation", headings);
            Assert.Contains("Preferences", headings);
        });
    }

    [Fact]
    public async Task Typing_filters_and_highlights()
    {
        _service.Register(Cmd.New("Open file"), Cmd.New("Close window"), Cmd.New("Save all"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());

        var input = cut.Find("[data-cp=input]");
        await cut.InvokeAsync(() => input.Input("clo"));

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-cp=item]");
            Assert.Single(items);
            Assert.Contains("Close window", items[0].TextContent);
            Assert.NotEmpty(cut.FindAll("[data-cp=mark]"));
        });
    }

    [Fact]
    public async Task Shows_empty_state_for_no_matches()
    {
        _service.Register(Cmd.New("Alpha"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync("zzz"));

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-cp=empty]");
            Assert.Contains("zzz", empty.TextContent);
        });
    }

    [Fact]
    public async Task Keyboard_navigation_moves_active_item_and_enter_executes()
    {
        var executed = new List<string>();
        _service.Register(
            Cmd.Action("First", () => executed.Add("first")).Priority(CommandPriority.High),
            Cmd.Action("Second", () => executed.Add("second")));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        Assert.Equal("First", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("ArrowDown", false, false, false, false));
        Assert.Equal("Second", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.Equal(["second"], executed);
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("dialog").GetAttribute("data-open")));
    }

    [Fact]
    public async Task Loop_wraps_from_first_to_last()
    {
        _service.Register(Cmd.New("A").Priority(10), Cmd.New("B"), Cmd.New("C").Priority(-10));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("ArrowUp", false, false, false, false));
        Assert.Equal("C", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());
    }

    [Fact]
    public async Task Nested_pages_push_breadcrumb_and_backspace_pops()
    {
        _service.Register(Cmd.Page("Theme", Cmd.New("Light"), Cmd.New("Dark")), Cmd.New("Other"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        // "Theme" sorts first alphabetically among equal priority? "Other" < "Theme"; move to Theme.
        var items = cut.FindAll("[data-cp=item]");
        var themeIndex = items.ToList().FindIndex(i => i.TextContent.Contains("Theme"));
        for (var i = 0; i < themeIndex; i++)
        {
            await cut.InvokeAsync(() => cut.Instance.OnKeyDown("ArrowDown", false, false, false, false));
        }

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("dialog").GetAttribute("data-open"));
            Assert.Contains("Theme", cut.Find("[data-cp=breadcrumbs]").TextContent);
            var titles = cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim()).ToList();
            Assert.Equal(["Dark", "Light"], titles);
        });

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Backspace", false, false, false, false));
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-cp=breadcrumbs]"));
            Assert.Equal(2, cut.FindAll("[data-cp=item]").Count);
        });
    }

    [Fact]
    public async Task Escape_goes_back_then_closes()
    {
        _service.Register(Cmd.Page("Theme", Cmd.New("Light")));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync(pageCommandId: "theme"));
        cut.WaitForAssertion(() => Assert.Contains("Theme", cut.Find("[data-cp=breadcrumbs]").TextContent));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Escape", false, false, false, false));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-cp=breadcrumbs]")));
        Assert.Equal("true", cut.Find("dialog").GetAttribute("data-open"));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Escape", false, false, false, false));
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("dialog").GetAttribute("data-open")));
    }

    [Fact]
    public async Task Mode_prefix_scopes_commands_and_shows_chip()
    {
        _service.RegisterMode(new CommandMode { Prefix = ">", Name = "Commands", Tags = ["command"], Description = "Run a command" });
        _service.Register(Cmd.New("Run build").Tags("command"), Cmd.New("Readme page"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync(">"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Commands", cut.Find("[data-cp=mode]").TextContent.Trim());
            var titles = cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim()).ToList();
            Assert.Equal(["Run build"], titles);
        });
    }

    [Fact]
    public async Task Mode_hints_appear_on_empty_root_query()
    {
        _service.RegisterMode(new CommandMode { Prefix = ">", Name = "Commands", Tags = ["command"], Description = "Run a command" });
        _service.Register(Cmd.New("Anything"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() =>
        {
            var headings = cut.FindAll("[data-cp=group-heading]").Select(h => h.TextContent.Trim());
            Assert.Contains("Modes", headings);
        });
    }

    [Fact]
    public async Task Disabled_commands_render_disabled_and_do_not_execute()
    {
        var ran = false;
        _service.Register(Cmd.Action("Deploy", () => ran = true).Disabled(true, "No permission"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-cp=item][data-disabled=true]")));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.False(ran);
        cut.WaitForAssertion(() => Assert.Contains("No permission", cut.Find("[data-cp=toast]").TextContent));
    }

    [Fact]
    public async Task Confirm_requires_second_enter()
    {
        var ran = false;
        _service.Register(Cmd.Action("Delete everything", () => ran = true).Destructive("Really delete?"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-cp=item]")));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.False(ran);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-confirming=true]")));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.True(ran);
    }

    [Fact]
    public async Task Prompt_result_collects_text()
    {
        string? received = null;
        _service.Register(Cmd.Action("Rename", _ => Task.FromResult(CommandResult.Prompt("New name", ctx =>
        {
            received = ctx.PromptValue;
            return Task.FromResult(CommandResult.Close);
        }))));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-cp=item]")));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-cp=prompt]")));

        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("report.pdf"));
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.Equal("report.pdf", received);
    }

    [Fact]
    public async Task Page_result_pushes_ad_hoc_items()
    {
        _service.Register(Cmd.Action("Pick", _ => Task.FromResult(CommandResult.Page("Pick one", [Cmd.New("X"), Cmd.New("Y")]))));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-cp=item]")));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        cut.WaitForAssertion(() =>
        {
            var titles = cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim()).ToList();
            Assert.Equal(["X", "Y"], titles);
        });
    }

    [Fact]
    public async Task Multi_select_toggles_and_submits_selection()
    {
        IReadOnlyList<Command>? submitted = null;
        _service.Register(Cmd.New("Labels").MultiSelect().Children(Cmd.New("Bug"), Cmd.New("Feature"), Cmd.New("Docs"))
            .Handler(ctx =>
            {
                submitted = ctx.SelectedCommands;
                return Task.FromResult(CommandResult.Close);
            }));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync(pageCommandId: "labels"));
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false)); // toggle Bug
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("ArrowDown", false, false, false, false));
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false)); // toggle Docs
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-checked=true]").Count));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", true, false, false, false)); // Ctrl+Enter
        Assert.NotNull(submitted);
        Assert.Equal(["Bug", "Docs"], submitted!.Select(c => c.Title).OrderBy(x => x));
    }

    [Fact]
    public async Task Declarative_items_register_and_unregister()
    {
        var cut = Render<CommandPalette>(p => p.AddChildContent<CommandGroup>(g => g
            .Add(x => x.Heading, "Declared")
            .AddChildContent<CommandItem>(i => i.Add(x => x.Title, "From markup").Add(x => x.Href, "/markup"))));

        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("From markup", cut.Find("[data-cp=list]").TextContent);
            Assert.Contains("Declared", cut.Find("[data-cp=group-heading]").TextContent);
        });
    }

    [Fact]
    public async Task Executed_event_fires_with_timing()
    {
        CommandExecutedEventArgs? args = null;
        _service.CommandExecuted += (_, e) => args = e;
        _service.Register(Cmd.Action("Ping", () => { }));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-cp=item]")));
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));

        Assert.NotNull(args);
        Assert.Equal("Ping", args!.Command.Title);
        Assert.IsType<CommandResult.CloseResult>(args.Result);
    }

    [Fact]
    public async Task Recents_section_appears_after_execution()
    {
        _service.Register(Cmd.Action("Ping", _ => Task.FromResult(CommandResult.KeepOpen)).Group("Tools"), Cmd.New("Other").Group("Tools"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        var items = cut.FindAll("[data-cp=item]");
        var pingIndex = items.ToList().FindIndex(i => i.TextContent.Contains("Ping"));
        for (var i = 0; i < pingIndex; i++)
        {
            await cut.InvokeAsync(() => cut.Instance.OnKeyDown("ArrowDown", false, false, false, false));
        }

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));

        cut.WaitForAssertion(() =>
        {
            var headings = cut.FindAll("[data-cp=group-heading]").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("Recent", headings);
        });
    }

    [Fact]
    public async Task Nested_declarative_items_open_a_page()
    {
        var created = new List<string>();
        var cut = Render<CommandPalette>(p => p.AddChildContent<CommandItem>(parent => parent
            .Add(x => x.Title, "New…")
            .Add(x => x.Id, "new")
            .AddChildContent<CommandItem>(child => child
                .Add(x => x.Title, "Project")
                .Add(x => x.OnExecute, EventCallback.Factory.Create<CommandContext>(this, () => created.Add("project"))))));

        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() =>
        {
            var titles = cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim()).ToList();
            Assert.Equal(["New…"], titles); // the child lives on the nested page, not the root
            Assert.Single(cut.FindAll("[data-cp=item] .cp-chevron"));
        });

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        cut.WaitForAssertion(() => Assert.Equal(["Project"], cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim())));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));
        Assert.Equal(["project"], created);
    }

    [Fact]
    public async Task Async_source_results_are_debounced_and_rendered_under_their_group()
    {
        var calls = new List<string>();
        _service.RegisterSource(new DelegateCommandSource("Remote", async (q, ct) =>
        {
            calls.Add(q);
            await Task.Delay(10, ct);
            return [Cmd.New($"Remote hit for {q}").Build()];
        }));

        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        var input = cut.Find("[data-cp=input]");
        await cut.InvokeAsync(() => input.Input("a"));
        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("ab"));
        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("abc"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Remote", cut.FindAll("[data-cp=group-heading]").Select(h => h.TextContent.Trim()));
            Assert.Contains("Remote hit for abc", cut.Find("[data-cp=list]").TextContent);
        }, TimeSpan.FromSeconds(3));

        Assert.Equal(["abc"], calls); // keystrokes inside the debounce window never reach the source
    }

    [Fact]
    public async Task Slow_searches_wait_for_the_keystroke_to_paint_and_coalesce()
    {
        var queries = new List<string>();
        _service.Register(Cmd.New("Alpha"), Cmd.New("Beta"), Cmd.New("Gamma"));
        var paint = _jsHandle.SetupVoid("afterPaint");
        var cut = Render<CommandPalette>(p => p.Add(x => x.OnQueryChanged, q => queries.Add(q)));
        cut.Instance.SlowSearchThreshold = TimeSpan.Zero;
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("g"));
        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("ga"));
        Assert.Equal(3, cut.FindAll("[data-cp=item]").Count); // not searched (nor re-rendered) before the paint

        await cut.InvokeAsync(paint.SetVoidResult);
        cut.WaitForAssertion(() => Assert.Equal(["Gamma"], cut.FindAll("[data-cp=item] .cp-item-title").Select(t => t.TextContent.Trim())));
        Assert.Equal(["ga"], queries);
    }

    [Fact]
    public async Task Enter_during_a_deferred_search_runs_the_fresh_top_result()
    {
        var executed = new List<string>();
        _service.Register(
            Cmd.Action("Alpha", () => executed.Add("alpha")).Priority(CommandPriority.High),
            Cmd.Action("Beta", () => executed.Add("beta")));
        _jsHandle.SetupVoid("afterPaint"); // never completes: the keystroke stays deferred
        var cut = RenderPalette();
        cut.Instance.SlowSearchThreshold = TimeSpan.Zero;
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("bet"));
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("Enter", false, false, false, false));

        Assert.Equal(["beta"], executed);
    }

    [Fact]
    public async Task Closing_during_a_deferred_search_keeps_rendering()
    {
        _service.Register(Cmd.New("Alpha"), Cmd.New("Beta"));
        _jsHandle.SetupVoid("afterPaint");
        var cut = RenderPalette();
        cut.Instance.SlowSearchThreshold = TimeSpan.Zero;
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("bet"));
        await cut.InvokeAsync(() => _service.CloseAsync());
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("dialog").GetAttribute("data-open")));

        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));
    }

    [Fact]
    public async Task Commands_parameter_adds_instance_commands()
    {
        var cut = Render<CommandPalette>(p => p.Add(x => x.Commands, new[] { Cmd.New("Instance only").Build() }));
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Contains("Instance only", cut.Find("[data-cp=list]").TextContent));

        cut.Render(p => p.Add(x => x.Commands, new[] { Cmd.New("Replaced").Build() }));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Replaced", cut.Find("[data-cp=list]").TextContent);
            Assert.DoesNotContain("Instance only", cut.Find("[data-cp=list]").TextContent);
        });
    }

    [Fact]
    public async Task ResetOnClose_false_restores_query_and_page()
    {
        Services.GetRequiredService<CommandPaletteOptions>().ResetOnClose = false;
        _service.Register(Cmd.Page("Theme", Cmd.New("Light"), Cmd.New("Dark")));
        var cut = RenderPalette();

        await cut.InvokeAsync(() => _service.OpenAsync(pageCommandId: "theme"));
        await cut.InvokeAsync(() => cut.Find("[data-cp=input]").Input("da"));
        await cut.InvokeAsync(() => _service.CloseAsync());
        await cut.InvokeAsync(() => _service.OpenAsync());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("da", cut.Find("[data-cp=input]").GetAttribute("value"));
            Assert.Contains("Theme", cut.Find("[data-cp=breadcrumbs]").TextContent);
        });
    }

    [Fact]
    public async Task Vim_bindings_move_only_when_enabled()
    {
        _service.Register(Cmd.New("A").Priority(10), Cmd.New("B"));
        var cut = Render<CommandPalette>(p => p.Add(x => x.VimBindings, false));
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("j", true, false, false, false));
        Assert.Equal("A", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());

        cut.Render(p => p.Add(x => x.VimBindings, true));
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("j", true, false, false, false));
        Assert.Equal("B", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());

        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("n", true, false, false, false)); // Ctrl+N always on, loops
        Assert.Equal("A", cut.Find("[data-active=true] .cp-item-title").TextContent.Trim());
    }

    [Fact]
    public async Task Trigger_opens_palette_with_query_and_shows_shortcut()
    {
        _service.Register(Cmd.New("Run build").Tags("command"));
        _service.RegisterMode(new CommandMode { Prefix = ">", Name = "Commands", Tags = ["command"] });

        var palette = RenderPalette();
        var trigger = Render<CommandPaletteTrigger>(p => p.Add(x => x.Query, ">"));

        Assert.Equal("Control+K", trigger.Find("button").GetAttribute("aria-keyshortcuts"));
        Assert.Equal(["Ctrl", "K"], trigger.FindAll(".cp-kbd").Select(k => k.TextContent));

        await trigger.InvokeAsync(() => trigger.Find("button").Click());
        palette.WaitForAssertion(() => Assert.Equal("Commands", palette.Find("[data-cp=mode]").TextContent.Trim()));
    }

    [Fact]
    public void Trigger_can_force_mac_symbols()
    {
        var trigger = Render<CommandPaletteTrigger>(p => p.Add(x => x.IsMac, true));
        Assert.Equal(["⌘", "K"], trigger.FindAll(".cp-kbd").Select(k => k.TextContent));
        Assert.Equal("Meta+K", trigger.Find("button").GetAttribute("aria-keyshortcuts"));
    }

    [Fact]
    public void Unstyled_omits_the_styling_hook_class()
    {
        var cut = Render<CommandPalette>(p => p.Add(x => x.Unstyled, true).Add(x => x.Class, "mine"));
        var dialog = cut.Find("dialog");
        Assert.DoesNotContain("cp-dialog", dialog.ClassList);
        Assert.Contains("mine", dialog.ClassList);
        Assert.Equal("dialog", dialog.GetAttribute("data-cp"));
    }

    [Fact]
    public async Task Pin_shortcut_matches_shifted_uppercase_key()
    {
        _service.Register(Cmd.New("Alpha").Group("G"), Cmd.New("Beta").Group("G"));
        var cut = RenderPalette();
        await cut.InvokeAsync(() => _service.OpenAsync());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-cp=item]").Count));

        // Browsers send key "P" (upper-case) when Shift is held.
        await cut.InvokeAsync(() => cut.Instance.OnKeyDown("P", true, true, false, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Pinned", cut.FindAll("[data-cp=group-heading]").Select(h => h.TextContent.Trim()));
            Assert.Single(cut.FindAll("[data-cp=item] .cp-pin"));
        });
    }
}
