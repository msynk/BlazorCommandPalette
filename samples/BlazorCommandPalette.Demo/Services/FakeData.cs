using BlazorCommandPalette.Search;

namespace BlazorCommandPalette.Demo.Services;

public sealed record Person(string Name, string Role, string Email);

public sealed record Issue(int Number, string Title, string State, string[] Labels);

/// <summary>In-memory "directory" standing in for an API.</summary>
public sealed class FakeDirectory
{
    public static readonly IReadOnlyList<Person> People =
    [
        new("Ada Lovelace", "Engineering", "ada@example.com"),
        new("Grace Hopper", "Engineering", "grace@example.com"),
        new("Alan Turing", "Research", "alan@example.com"),
        new("Katherine Johnson", "Data", "katherine@example.com"),
        new("Margaret Hamilton", "Engineering", "margaret@example.com"),
        new("Linus Torvalds", "Platform", "linus@example.com"),
        new("Barbara Liskov", "Architecture", "barbara@example.com"),
        new("Dennis Ritchie", "Platform", "dennis@example.com"),
        new("Hedy Lamarr", "Research", "hedy@example.com"),
        new("Tim Berners-Lee", "Web", "tim@example.com"),
    ];

    public static readonly IReadOnlyList<Issue> Issues =
    [
        new(101, "Palette flickers when opened during navigation", "open", ["bug", "ui"]),
        new(102, "Add vim keybindings", "closed", ["enhancement"]),
        new(103, "Support Ctrl+Shift+P as alternative toggle", "closed", ["enhancement"]),
        new(104, "Recents should survive page reload", "open", ["bug"]),
        new(105, "Nested pages lose query on back", "open", ["bug"]),
        new(106, "Document theming variables", "open", ["docs"]),
        new(107, "Virtualize long result lists", "open", ["performance"]),
        new(108, "Fuzzy match should prefer word starts", "closed", ["search"]),
        new(109, "Screen reader announces result count", "closed", ["a11y"]),
        new(110, "Mobile layout: full screen dialog", "open", ["ui"]),
    ];

    public static readonly IReadOnlyList<(string Title, string Path, string Excerpt)> Docs =
    [
        ("Getting started", "/docs/getting-started", "Install the package, add the service, drop the component into your layout."),
        ("Registering commands", "/docs/commands", "Fluent builder, declarative markup, page-scoped registration."),
        ("Modes and prefixes", "/docs/modes", "Scope the palette with > # @ ? prefixes like VS Code and GitHub."),
        ("Async sources", "/docs/sources", "Debounced, cancellable searches against your APIs."),
        ("Nested pages and prompts", "/docs/pages", "Multi-step flows, action panels, free-text prompts, multi-select."),
        ("Theming", "/docs/theming", "CSS custom properties, dark mode, unstyled mode."),
        ("Accessibility", "/docs/a11y", "ARIA combobox pattern, focus management, live announcements."),
        ("Keyboard shortcuts", "/docs/shortcuts", "Global chords and sequences like G then I."),
    ];
}

/// <summary>Simulates a remote search with latency. Results are re-ranked by the palette's matcher.</summary>
public sealed class PeopleSource : ICommandSource
{
    public string Name => "People";

    public int MinQueryLength => 1;

    public bool ApplyMatcher => true;

    public async Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        return FakeDirectory.People
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Role.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(p => Cmd.New(p.Name)
                .Id($"person:{p.Email}")
                .Subtitle(p.Role)
                .Keywords(p.Email)
                .Icon("demo-avatar")
                .Data(p)
                .Actions(
                    Cmd.Action("Copy e-mail", ctx => Task.FromResult(CommandResult.Toast($"Copied {p.Email}"))).Build(),
                    Cmd.Action("Mention in issue", ctx => Task.FromResult(CommandResult.Toast($"Mentioned {p.Name}"))).Build())
                .Handler(ctx => Task.FromResult(CommandResult.Toast($"Opening profile of {p.Name}")))
                .Build())
            .ToList();
    }
}

/// <summary>Source whose own ordering is kept (ApplyMatcher = false), e.g. a server already sorted by relevance.</summary>
public sealed class IssueSource : ICommandSource
{
    public string Name => "Issues";

    public int MinQueryLength => 0; // shows a default list on the empty query

    public async Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(180, cancellationToken);
        IEnumerable<Issue> hits = FakeDirectory.Issues;
        if (query.Length > 0)
        {
            hits = int.TryParse(query.TrimStart('#'), out var number)
                ? hits.Where(i => i.Number.ToString().StartsWith(number.ToString(), StringComparison.Ordinal))
                : hits.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Labels.Any(l => l.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        return hits
            .OrderByDescending(i => i.State == "open")
            .ThenByDescending(i => i.Number)
            .Select(i => Cmd.New($"#{i.Number} {i.Title}")
                .Id($"issue:{i.Number}")
                .Subtitle(string.Join(" · ", i.Labels))
                .Badge(i.State)
                .Kind(CommandKind.Navigation)
                .Handler(ctx => Task.FromResult(CommandResult.Toast($"Opened issue #{i.Number}")))
                .Build())
            .ToList();
    }
}

public sealed class DocsSource : ICommandSource
{
    public string Name => "Documentation";

    public int MinQueryLength => 2;

    public bool ApplyMatcher => true;

    public async Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
        return FakeDirectory.Docs
            .Select(d => Cmd.New(d.Title)
                .Id($"doc:{d.Path}")
                .Subtitle(d.Excerpt)
                .Kind(CommandKind.Help)
                .Priority(CommandPriority.Low)
                .Handler(_ => Task.FromResult(CommandResult.Toast($"Would open {d.Path}")))
                .Build())
            .ToList();
    }
}
