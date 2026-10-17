using BlazorCommandPalette.History;
using BlazorCommandPalette.Search;
using Microsoft.Extensions.Time.Testing;

namespace BlazorCommandPalette.Tests;

public class CommandModelTests
{
    [Fact]
    public void Id_defaults_to_slug_of_title_group_and_parent()
    {
        var c = new Command { Title = "Go to Settings!", Group = "Navigation" };
        Assert.Equal("navigation.go-to-settings", c.Id);

        var child = new Command { Title = "Dark", Parent = "theme" };
        Assert.Equal("theme.dark", child.Id);

        var plain = new Command { Title = "  Hello   World " };
        Assert.Equal("hello-world", plain.Id);
    }

    [Fact]
    public void Builder_round_trips_through_With()
    {
        var c = Cmd.New("Rename").Shortcut("F2").Keywords("edit").Destructive("Sure?").Badge("New").Build();
        var copy = c.With(b => b.Title("Rename file"));
        Assert.Equal("Rename file", copy.Title);
        Assert.Equal(c.Id, copy.Id);
        Assert.Equal("f2", copy.Shortcut!.Normalized);
        Assert.Equal(["edit"], copy.Keywords);
        Assert.True(copy.Destructive);
        Assert.Equal("Sure?", copy.ConfirmMessage);
        Assert.Equal("New", copy.Badge);
    }

    [Fact]
    public void Builder_infers_kind()
    {
        Assert.Equal(CommandKind.Navigation, Cmd.Navigate("Home", "/").Build().Kind);
        Assert.Equal(CommandKind.Page, Cmd.Page("Theme", Cmd.New("Dark")).Build().Kind);
        Assert.Equal(CommandKind.Toggle, Cmd.New("Wrap").Checked(true).Build().Kind);
        Assert.Equal(CommandKind.Action, Cmd.Action("Save", () => { }).Build().Kind);
    }

    [Fact]
    public async Task Action_handler_closes_by_default()
    {
        var ran = false;
        var c = Cmd.Action("Save", () => ran = true).Build();
        var result = await c.Handler!(null!);
        Assert.True(ran);
        Assert.IsType<CommandResult.CloseResult>(result);
    }

    [Fact]
    public void Mode_accepts_by_tag_and_group()
    {
        var mode = new CommandMode { Prefix = ">", Name = "Commands", Tags = ["command"] };
        Assert.True(mode.Accepts(Cmd.New("A").Tags("command").Build()));
        Assert.False(mode.Accepts(Cmd.New("B").Build()));

        var groupMode = new CommandMode { Prefix = "#", Name = "Issues", Groups = ["Issues"] };
        Assert.True(groupMode.Accepts(Cmd.New("C").Group("issues").Build()));
        Assert.False(groupMode.Accepts(Cmd.New("D").Group("Pages").Build()));

        var sourcesOnly = new CommandMode { Prefix = "@", Name = "People", HideRegistryCommands = true };
        Assert.False(sourcesOnly.Accepts(Cmd.New("E").Build()));
    }
}

public class CommandPaletteServiceTests
{
    private static CommandPaletteService NewService() => new(new CommandPaletteOptions());

    [Fact]
    public void Register_and_dispose_removes_commands()
    {
        var svc = NewService();
        var changed = 0;
        svc.CommandsChanged += () => changed++;

        var handle = svc.Register(Cmd.New("One"), Cmd.New("Two"));
        Assert.Equal(2, svc.Commands.Count);
        Assert.Equal(1, changed);

        handle.Dispose();
        Assert.Empty(svc.Commands);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Nested_children_are_flattened_with_parent_ids()
    {
        var svc = NewService();
        svc.Register(Cmd.Page("Theme", Cmd.New("Light"), Cmd.New("Dark")));
        var ids = svc.Commands.Select(c => c.Id).OrderBy(x => x).ToList();
        Assert.Equal(["theme", "theme.dark", "theme.light"], ids);
        Assert.All(svc.Commands.Where(c => c.Id != "theme"), c => Assert.Equal("theme", c.Parent));
    }

    [Fact]
    public void Later_registration_overrides_same_id()
    {
        var svc = NewService();
        svc.Register(Cmd.New("Save").Id("save").Subtitle("global"));
        svc.Register(Cmd.New("Save").Id("save").Subtitle("page"));
        Assert.Equal("page", svc.Find("save")!.Subtitle);
        Assert.Single(svc.Commands);
    }

    [Fact]
    public void Update_replaces_in_place()
    {
        var svc = NewService();
        svc.Register(Cmd.New("Wrap").Id("wrap").Checked(false));
        svc.Update(Cmd.New("Wrap").Id("wrap").Checked(true));
        Assert.True(svc.Find("wrap")!.Checked);
        Assert.Single(svc.Commands);
    }

    [Fact]
    public void Options_commands_are_preloaded()
    {
        var options = new CommandPaletteOptions();
        options.Commands.Add(Cmd.New("Preloaded"));
        options.Modes.Add(new CommandMode { Prefix = ">", Name = "Commands" });
        var svc = new CommandPaletteService(options);
        Assert.Single(svc.Commands);
        Assert.Single(svc.Modes);
    }
}

public class CommandRankerTests
{
    private static readonly FuzzyMatcher Matcher = new();

    [Fact]
    public void Empty_query_sorts_by_priority_then_frecency_then_title()
    {
        var ranker = new CommandRanker(new CommandPaletteOptions());
        var matches = new[]
        {
            Matcher.Match(Cmd.New("Zeta").Build(), "")!,
            Matcher.Match(Cmd.New("Alpha").Build(), "")!,
            Matcher.Match(Cmd.New("Important").Priority(CommandPriority.High).Build(), "")!,
        };
        var ranked = ranker.Rank(matches, hasQuery: false, id => id == "zeta" ? 50 : 0);
        Assert.Equal(["Important", "Zeta", "Alpha"], ranked.Select(r => r.Command.Title));
    }

    [Fact]
    public void Query_ranking_is_dominated_by_match_score()
    {
        var ranker = new CommandRanker(new CommandPaletteOptions());
        var exact = Matcher.Match(Cmd.New("Settings").Build(), "settings")!;
        var weak = Matcher.Match(Cmd.New("Set the ring tones").Priority(CommandPriority.Highest).Build(), "settings")!;
        var ranked = ranker.Rank([weak, exact], hasQuery: true, _ => 0);
        Assert.Equal("Settings", ranked[0].Command.Title);
    }
}

public class CommandHistoryTests
{
    [Fact]
    public async Task Records_usage_and_computes_frecency()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var history = new CommandHistory(new InMemoryCommandHistoryStore(), new CommandPaletteOptions(), time);

        await history.RecordAsync("a");
        time.Advance(TimeSpan.FromMinutes(1));
        await history.RecordAsync("a");
        time.Advance(TimeSpan.FromMinutes(1));
        await history.RecordAsync("b");

        Assert.Equal(2, history.Get("a")!.Count);
        Assert.True(history.Frecency("a") > history.Frecency("b"));
        Assert.Equal(["b", "a"], history.Recents(5));

        time.Advance(TimeSpan.FromDays(40));
        var old = history.Frecency("a");
        await history.RecordAsync("c");
        Assert.True(history.Frecency("c") > old);
    }

    [Fact]
    public async Task Pin_moves_out_of_recents_and_survives_clear()
    {
        var history = new CommandHistory(new InMemoryCommandHistoryStore(), new CommandPaletteOptions());
        await history.RecordAsync("a");
        await history.RecordAsync("b");
        await history.TogglePinAsync("a");

        Assert.Equal(["a"], history.Pinned());
        Assert.Equal(["b"], history.Recents(5));

        await history.ClearAsync();
        Assert.Equal(["a"], history.Pinned());
        Assert.Empty(history.Recents(5));
    }

    [Fact]
    public async Task Prunes_to_max_entries()
    {
        var options = new CommandPaletteOptions { MaxHistoryEntries = 3 };
        var time = new FakeTimeProvider();
        var history = new CommandHistory(new InMemoryCommandHistoryStore(), options, time);
        for (var i = 0; i < 6; i++)
        {
            await history.RecordAsync($"c{i}");
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(3, history.Recents(10).Count);
        Assert.Equal(["c5", "c4", "c3"], history.Recents(10));
    }

    [Fact]
    public async Task Persists_through_store()
    {
        var store = new InMemoryCommandHistoryStore();
        var h1 = new CommandHistory(store, new CommandPaletteOptions());
        await h1.RecordAsync("x");

        var h2 = new CommandHistory(store, new CommandPaletteOptions());
        await h2.EnsureLoadedAsync();
        Assert.Equal(1, h2.Get("x")!.Count);
    }
}
