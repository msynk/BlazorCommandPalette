# Changelog

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
