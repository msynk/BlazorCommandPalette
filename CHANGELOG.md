# Changelog

## Unreleased

- Typing stays responsive with large registries: the fuzzy matcher no longer allocates per field, the query is parsed once
  per keystroke, ranking only sorts the rows it keeps (`MaxResults`), and async source results reuse the registry matches.
  On WebAssembly, searches slower than a frame run after the keystroke paints and fold fast typing into one search
  (navigation keys still act on the latest query). With 2,000 commands the median keystroke drops from ~180 ms to ~20 ms.

## 1.0.0

Initial release.

- `CommandPalette`, `CommandPaletteTrigger`, `CommandGroup`, `CommandItem`, `RegisterCommands`, `ShortcutHint`, `HighlightedText` components.
- `ICommandPaletteService` registry with page-scoped registrations, modes, sources and execution events.
- fzf-style `FuzzyMatcher` with highlight positions, multi-term queries and `' ^ $ !` operators; `SubstringMatcher`.
- Nested pages (static, lazy, declarative, ad-hoc), prompts, multi-select, confirmations, toasts, action panel.
- Recents, pinned and frecency ranking persisted to localStorage via `ICommandHistoryStore`.
- Global shortcuts with chords and sequences; platform-aware hints.
- Native `<dialog>`, ARIA combobox semantics, live announcements, dark mode, reduced motion, mobile layout, detail pane, virtualization.
- `ICommandPaletteService.IsMac` platform detection; `CommandPaletteTrigger` renders ⌘ on macOS automatically.
- Vim `Ctrl+J/K`, `Ctrl+N/P` and the in-palette pin chord take precedence over a toggle bound to the same keys.
