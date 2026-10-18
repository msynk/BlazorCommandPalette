**Note**: moved to bitplatform (https://balette.bitplatform.dev)

# BlazorCommandPalette

A professional, accessible, keyboard-first **command palette** (`Ctrl+K` / `⌘K`) for Blazor — Server, WebAssembly, and Blazor Web App (any interactive render mode). Inspired by the best of [cmdk](https://github.com/pacocoursey/cmdk), [kbar](https://github.com/timc1/kbar), VS Code's Quick Pick, Raycast, Linear and GitHub's command palette, built natively on .NET 8/9/10.

```
┌───────────────────────────────────────────────────────────────┐
│  ⌕  Type a command or search…                           Esc   │
├───────────────────────────────────────────────────────────────┤
│  RECENT                                                       │
│  ↗  Go to Settings                        G then S            │
│  NAVIGATION                                                   │
│  ↗  Home                                  G then H            │
│  ▸  Theme                                 Auto             ›  │
│  ⚡ Rename project…                        F2                  │
│  ☐  Set labels…                                            ›  │
├───────────────────────────────────────────────────────────────┤
│  ↑↓ to navigate   ↵ to select   Tab for actions   Esc close   │
└───────────────────────────────────────────────────────────────┘
```

## Features

**Search & ranking**
- fzf-style fuzzy matcher (DP with affine gaps): bonuses for word boundaries, camelCase humps, consecutive runs and the first character; gap penalties; smart case; optimal positions for highlighting.
- Multi-term AND queries and operators: `'exact`, `^prefix`, `suffix$`, `!negate`.
- Weighted fields (title › keywords › subtitle › group › description › tags), keyword hits surfaced.
- Final rank = match score + priority + **frecency** (frequency × recency). Pluggable `ICommandMatcher`.

**Structure & flows**
- Groups with sticky headings and explicit ordering; flat list while searching (configurable).
- **Nested pages**: static children, lazily loaded children, declarative `<CommandItem>` nesting, or ad-hoc pages returned from handlers. Breadcrumbs, `Backspace` to go back.
- **Modes** by prefix (`>` commands, `#` issues, `@` people, `?` help…) scoping registry commands and async sources.
- **Async sources** (`ICommandSource`): debounced, cancellable, per-mode or global, either re-ranked by the matcher or shown as an ordered block.
- **Multi-step**: free-text **prompts**, **multi-select** with `Ctrl+Enter`, **confirmation** for destructive commands, **toasts**, `KeepOpen` / `Reset` / `Back` results.
- Raycast-style **action panel** on `Tab`: per-command secondary actions plus pin / forget.
- **Recents & pinned** persisted to `localStorage` (swap in your own `ICommandHistoryStore`).

**Keyboard**
- Toggle with `Mod+K` (+ any extra chords such as `Ctrl+Shift+P`).
- Global command shortcuts, including **sequences** like `G I`, suppressed inside editable elements.
- `↑ ↓`, `Ctrl+N/P`, optional vim `Ctrl+J/K`, `Home/End`, `PgUp/PgDn`, loop, `Enter`, `Ctrl+Enter`, `Tab`, `Esc` (cancel → back → close).

**Accessibility & UX**
- Native `<dialog>` (top layer, focus trap, focus restore, scroll lock, backdrop click).
- WAI-ARIA combobox/listbox: `aria-activedescendant`, `aria-selected`, `aria-checked`, live region announcing result counts, platform-aware shortcut hints (⌘ vs Ctrl).
- Light/dark via `prefers-color-scheme` or forced; reduced-motion aware; mobile full-screen layout; optional detail pane.
- Templates for item, group header, empty, loading, header, footer, detail and icons — or go fully `Unstyled` and style the `data-cp` attributes yourself.
- `Virtualize` for very large registries. Trimming / AOT friendly. Multi-targets `net8.0`, `net9.0`, `net10.0`.

## Install

```bash
dotnet add package BlazorCommandPalette
```

**1. Register services** (`Program.cs`):

```csharp
builder.Services.AddCommandPalette(options =>
{
    options.AdditionalToggleShortcuts.Add("Mod+Shift+P");
    options.GroupOrder.AddRange(["Navigation", "Actions", "Preferences"]);
    options.Modes.Add(new CommandMode { Prefix = ">", Name = "Commands", Tags = ["command"] });
});
```

**2. Add the stylesheet** (`index.html` / `App.razor`):

```html
<link rel="stylesheet" href="_content/BlazorCommandPalette/command-palette.css" />
```

**3. Place the component once** in your layout (it needs an interactive render mode):

```razor
@using BlazorCommandPalette.Components

<CommandPalette />
<CommandPaletteTrigger Label="Search…" />
```

**4. Register commands** anywhere via `ICommandPaletteService`:

```csharp
@inject ICommandPaletteService Palette

palette.Register(
    Cmd.Navigate("Home", "/").Group("Navigation").Shortcut("G H"),
    Cmd.Navigate("Settings", "/settings").Group("Navigation").Shortcut("G S").Keywords("preferences"),
    Cmd.Action("Toggle sidebar", () => sidebar.Toggle()).Shortcut("Mod+B"),
    Cmd.New("Theme").Children(
        Cmd.Action("Light", () => theme.Set("light")),
        Cmd.Action("Dark",  () => theme.Set("dark"))),
    Cmd.New("Delete project").Destructive("This cannot be undone").Handler(async ctx => { await api.DeleteAsync(); return CommandResult.Toast("Deleted"); }));
```

`Register` returns an `IDisposable` — dispose it to remove page-scoped commands, or use the `<RegisterCommands Commands="..."/>` component.

### Declarative markup

```razor
<CommandPalette>
    <CommandGroup Heading="Quick create">
        <CommandItem Title="New issue" Shortcut="C" OnExecute="CreateIssue" />
        <CommandItem Title="New…">
            <CommandItem Title="Project"  OnExecute="NewProject" />
            <CommandItem Title="Document" OnExecute="NewDocument" Badge="Beta" />
        </CommandItem>
    </CommandGroup>
</CommandPalette>
```

### Handler results

A handler returns a `CommandResult`:

| Result | Effect |
|---|---|
| `CommandResult.Close` | Close (default for `Action(...)`) |
| `CommandResult.KeepOpen` | Stay open, rebuild list (toggles) |
| `CommandResult.Back` / `Reset` | Pop one page / go to root |
| `CommandResult.Page(title, commands, multiSelect: true, onSubmit: ...)` | Push an ad-hoc page |
| `CommandResult.Prompt(title, onSubmit, placeholder)` | Ask for free text; `ctx.PromptValue` holds it |
| `CommandResult.Toast(message, keepOpen)` | Show a footer message |

### Async sources

```csharp
public sealed class PeopleSource : ICommandSource
{
    public string Name => "People";
    public bool ApplyMatcher => true; // re-rank with the fuzzy matcher, or false to keep server order

    public async Task<IReadOnlyList<Command>> SearchAsync(string query, CommandSourceContext ctx, CancellationToken ct)
    {
        var people = await api.SearchAsync(query, ct);
        return people.Select(p => Cmd.New(p.Name).Subtitle(p.Role).Handler(_ => Open(p)).Build()).ToList();
    }
}

options.Sources.Add(new PeopleSource());                       // default mode
options.Modes.Add(new CommandMode { Prefix = "@", Name = "People", HideRegistryCommands = true, Sources = [new PeopleSource()] });
```

### Theming

All visuals are CSS custom properties (`--cp-bg`, `--cp-accent`, `--cp-width`, `--cp-radius`, …) defined on `:root`, so the trigger button and shortcut hints share them; override them globally or on `dialog[data-cp="dialog"]`. The theme follows `prefers-color-scheme`, or `<html data-theme="light|dark">`, or the component's `Theme` parameter. Component styles are scoped to `.cp-dialog`: set `Unstyled="true"` and target the `data-cp="…"` / `data-active` / `data-kind` attributes with your own CSS.

`ICommandPaletteService.IsMac` reports the detected platform so your own UI can render ⌘ instead of Ctrl.

### Templates

`ItemTemplate`, `GroupHeaderTemplate`, `EmptyTemplate`, `LoadingTemplate`, `HeaderTemplate`, `FooterTemplate`, `DetailTemplate`, `IconTemplate` are all `RenderFragment` parameters. `DetailTemplate` renders a side pane for the active command (Raycast style).

### Localization

Set `options.Strings` (or the `Strings` parameter) to a `CommandPaletteStrings` instance or subclass to translate every label and announcement.

## Options

| Option | Default | Notes |
|---|---|---|
| `ToggleShortcut` | `Mod+K` | plus `AdditionalToggleShortcuts` |
| `DebounceMs` | 150 | async sources |
| `Loop`, `VimBindings` | true, false | |
| `EnableHistory`, `MaxRecents` | true, 5 | `HistoryStorageKey` for localStorage |
| `GroupWhenSearching` | true | flat ranked list when false |
| `PriorityWeight`, `FrecencyWeight` | 0.25, 6 | ranking nudges |
| `ShowModeHints` | true | lists registered prefixes on the empty query |
| `CloseOnBackdropClick`, `CloseOnNavigation`, `ResetOnClose` | true | |
| `Virtualize`, `RowHeight` | false, 44 | |
| `Matcher`, `Weights` | `FuzzyMatcher` | pluggable |

## Repository layout

- `src/BlazorCommandPalette` — the Razor class library (NuGet package)
- `samples/BlazorCommandPalette.Demo` — WebAssembly demo showcasing every feature (`dotnet run --project samples/BlazorCommandPalette.Demo`)
- `tests/BlazorCommandPalette.Tests` — xUnit + bUnit tests
- `docs/RESEARCH.md` — the research that drove the feature set

## License

MIT
