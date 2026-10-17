namespace BlazorCommandPalette.Demo.Services;

/// <summary>Global commands registered from the layout. Shows the fluent API end to end.</summary>
public static class DemoCommands
{
    public static IEnumerable<Command> Global(DemoState state) =>
    [
        // ---- Navigation (also reachable with G-then-X sequences, like Linear / GitHub) ----
        Cmd.Navigate("Home", "/").Group("Navigation").Shortcut("G H").Keywords("start", "landing").Priority(CommandPriority.High),
        Cmd.Navigate("Features", "/features").Group("Navigation").Shortcut("G F").Keywords("capabilities", "checklist"),
        Cmd.Navigate("Playground", "/playground").Group("Navigation").Shortcut("G P").Keywords("demo", "try", "sandbox"),
        Cmd.Navigate("Settings", "/settings").Group("Navigation").Shortcut("G S").Keywords("preferences", "options", "config"),
        Cmd.New("GitHub repository").Href("https://github.com/msynk/BlazorCommandPalette", newTab: true).Group("Navigation").Keywords("source", "code", "repo"),

        // ---- Actions ----
        Cmd.New("Rename project…")
            .Group("Actions")
            .Shortcut("F2")
            .Keywords("title", "name")
            .Handler(_ => Task.FromResult(CommandResult.Prompt(
                "New project name",
                ctx =>
                {
                    if (!string.IsNullOrWhiteSpace(ctx.PromptValue))
                    {
                        state.Rename(ctx.PromptValue.Trim());
                    }

                    return Task.FromResult(CommandResult.Toast($"Renamed to “{ctx.PromptValue}”"));
                },
                placeholder: "Project name…",
                initialValue: state.ProjectName))),

        Cmd.New("Set labels…")
            .Group("Actions")
            .Keywords("tags", "categorize")
            .MultiSelect()
            .ChildrenPlaceholder("Toggle labels, Ctrl+Enter to apply…")
            .Children(
                Cmd.New("bug").Id("label:bug").Icon("demo-dot demo-dot-red"),
                Cmd.New("enhancement").Id("label:enhancement").Icon("demo-dot demo-dot-blue"),
                Cmd.New("documentation").Id("label:documentation").Icon("demo-dot demo-dot-green"),
                Cmd.New("performance").Id("label:performance").Icon("demo-dot demo-dot-yellow"),
                Cmd.New("accessibility").Id("label:accessibility").Icon("demo-dot demo-dot-purple"))
            .Handler(ctx =>
            {
                state.SetLabels(ctx.SelectedCommands.Select(c => c.Title));
                return Task.FromResult(CommandResult.Toast($"{ctx.SelectedCommands.Count} labels applied"));
            }),

        Cmd.New("Assign to…")
            .Group("Actions")
            .Keywords("owner", "person", "user")
            .Children(async (ctx, ct) =>
            {
                await Task.Delay(400, ct); // lazily loaded page
                return FakeDirectory.People
                    .Select(p => Cmd.New(p.Name)
                        .Subtitle(p.Role)
                        .Icon("demo-avatar")
                        .Handler(_ =>
                        {
                            state.Record($"Assigned to {p.Name}");
                            return Task.FromResult(CommandResult.Toast($"Assigned to {p.Name}"));
                        })
                        .Build())
                    .ToList();
            }, placeholder: "Pick an assignee…"),

        Cmd.New("Copy share link")
            .Group("Actions")
            .Shortcut("Mod+Shift+C")
            .Keywords("url", "clipboard")
            .Tags("command")
            .Handler(_ =>
            {
                state.Record("Share link copied");
                return Task.FromResult(CommandResult.Toast("Link copied to clipboard"));
            }),

        Cmd.New("Export…")
            .Group("Actions")
            .Keywords("download", "save as")
            .Tags("command")
            .Handler(_ => Task.FromResult(CommandResult.Page("Export format",
            [
                Cmd.Action("CSV", () => state.Record("Exported CSV")).Subtitle("Comma separated values").Build(),
                Cmd.Action("JSON", () => state.Record("Exported JSON")).Subtitle("Pretty printed").Build(),
                Cmd.Action("PDF", () => state.Record("Exported PDF")).Subtitle("Print friendly").Badge("Pro").Disabled(true, "Requires the Pro plan").Build(),
            ]))),

        // ---- Preferences (toggles keep the palette open and update in place) ----
        Cmd.New("Theme")
            .Group("Preferences")
            .Keywords("appearance", "dark", "light", "color scheme")
            .Value(Capitalize(state.Theme))
            .Kind(CommandKind.Setting)
            .Children(
                ThemeOption(state, "auto", "Follow the operating system"),
                ThemeOption(state, "light", "Always light"),
                ThemeOption(state, "dark", "Always dark")),

        Toggle("Word wrap", "wordwrap", state.WordWrap, state.ToggleWordWrap, "Alt+Z", "soft wrap lines"),
        Toggle("Desktop notifications", "notifications", state.Notifications, state.ToggleNotifications, null, "alerts push"),
        Toggle("Show minimap", "minimap", state.ShowMinimap, state.ToggleMinimap, null, "outline overview"),

        // ---- Danger zone ----
        Cmd.New("Delete project")
            .Group("Danger zone")
            .Keywords("remove", "destroy")
            .Destructive("This permanently deletes the project")
            .Handler(_ =>
            {
                state.Record("Project deleted (not really)");
                return Task.FromResult(CommandResult.Toast("Project deleted"));
            }),

        Cmd.New("Reset all settings")
            .Group("Danger zone")
            .Destructive("Reset every preference to its default?")
            .Handler(async ctx =>
            {
                await state.SetThemeAsync("auto");
                return CommandResult.Toast("Settings reset");
            }),

        // ---- Help ----
        Cmd.New("Keyboard shortcuts")
            .Group("Help")
            .Shortcut("Mod+/")
            .Keywords("hotkeys", "keys", "bindings")
            .Kind(CommandKind.Help)
            .Href("/features#shortcuts"),

        Cmd.New("What's new")
            .Group("Help")
            .Badge("1.0")
            .Kind(CommandKind.Help)
            .Handler(_ => Task.FromResult(CommandResult.Toast("You are on the latest version", keepOpen: true))),

        Cmd.New("Feedback: couldn't find a command?")
            .Group("Help")
            .Kind(CommandKind.Help)
            .AlwaysShow()
            .Priority(CommandPriority.Lowest)
            .Handler(ctx => Task.FromResult(CommandResult.Toast(ctx.Query.Length > 0 ? $"Thanks! We logged “{ctx.Query}”." : "Thanks for the feedback!"))),
    ];

    private static Command ThemeOption(DemoState state, string theme, string subtitle) =>
        Cmd.New(Capitalize(theme))
            .Id($"theme:{theme}")
            .Subtitle(subtitle)
            .Checked(state.Theme == theme)
            .Handler(async ctx =>
            {
                await state.SetThemeAsync(theme);
                return CommandResult.Back;
            })
            .Build();

    private static Command Toggle(string title, string id, bool value, Action toggle, string? shortcut, string keywords)
    {
        var b = Cmd.New(title)
            .Id($"toggle:{id}")
            .Group("Preferences")
            .Keywords(keywords.Split(' '))
            .Checked(value)
            .Handler(_ =>
            {
                toggle();
                return Task.FromResult(CommandResult.KeepOpen);
            });
        if (shortcut is not null)
        {
            b.Shortcut(shortcut);
        }

        return b.Build();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
