# Research: what a modern command palette needs

This document summarises the research done before designing BlazorCommandPalette and maps each finding to what the library implements.

## Sources studied

| Product / library | What it contributes |
|---|---|
| [cmdk](https://github.com/pacocoursey/cmdk) (React, shadcn/ui) | Composable primitives (Command, Input, List, Item, Group, Separator, Empty, Loading, Dialog), built-in fuzzy filter + sort, `keywords`, `forceMount`, `loop`, `shouldFilter={false}` for custom filtering/virtualization, `data-selected`/`data-disabled` styling hooks, nested "pages" as a state stack with Escape/Backspace to go back, VoiceOver-tested ARIA. |
| [kbar](https://github.com/timc1/kbar) (React) | Action registry model: `id, name, shortcut, keywords, section, subtitle, icon, priority, parent, perform`; `useRegisterActions` for page-scoped actions; nested actions with Backspace; `Ctrl+N/P`; single-key global shortcuts; sections with priority; virtualized results; history (undo/redo). |
| VS Code Quick Pick | Prefix modes (`>` commands, `#` symbols, `:` go to line, `?` help), `matchOnDescription/Detail`, separators, `canSelectMany`, item buttons, busy state, step/totalSteps multi-step input, label highlight. |
| Raycast | List + Detail pane, ActionPanel per item (secondary actions behind `⌘K`/Tab), push/pop navigation stack, toast feedback, Form steps. |
| Linear / GitHub / Vercel | `Cmd+K` as the universal entry point, `G` + letter sequence shortcuts, contextual actions, prefix scoping (`#`, `>`, `?`), current-location indicator, visible shortcut hints. |
| fzf scoring algorithm | Bonus for word starts and camelCase humps, first-char multiplier, gap start/extension penalties, consecutive bonus, smart case; DP with backtrace for positions. |
| WAI-ARIA APG combobox | Focus stays on the input; `aria-activedescendant` points at the active option; `role=listbox`/`option`; `aria-selected` on the active option; manual scroll-into-view; Enter selects, Escape closes. |
| UX pattern articles (SaaS palettes) | Be useful before typing (recents, suggestions); group by type with consistent icons; show title + secondary detail per row; inline actions; scoped search; respect permissions; make destructive actions visually distinct and not one careless Enter away; debounce; keep plain navigation path solid before power features. |
| Blazor / .NET 10 guidance | Collocated `.razor.js` modules loaded via `IJSRuntime` import in `OnAfterRenderAsync`, `_content/{PackageId}/…` paths for RCLs, dispose `IJSObjectReference`/`DotNetObjectReference`, trap `JSDisconnectedException`, minimise interop calls, interactive render mode required. |

## Feature matrix

| Capability | cmdk | kbar | VS Code | Raycast | **BlazorCommandPalette** |
|---|---|---|---|---|---|
| Fuzzy filter + rank | ✅ | ✅ | ✅ | ✅ | ✅ fzf-style DP, pluggable |
| Highlight matches | — | — | ✅ | — | ✅ title + subtitle |
| Keywords / aliases | ✅ | ✅ | — | ✅ | ✅ weighted, hit shown |
| Groups / sections | ✅ | ✅ | ✅ (separators) | ✅ | ✅ sticky, ordered |
| Priority | — | ✅ | — | — | ✅ |
| Recents / frecency | — | ✅ (history) | ✅ (recently used) | ✅ | ✅ persisted, pinning |
| Nested pages | manual | ✅ | multi-step | ✅ push/pop | ✅ static / lazy / declarative / ad-hoc |
| Prefix modes | — | — | ✅ | — | ✅ |
| Async sources | manual | manual | ✅ | ✅ | ✅ debounced, cancellable |
| Multi-select | — | — | ✅ | — | ✅ |
| Free-text prompt | — | — | ✅ (InputBox) | ✅ (Form) | ✅ |
| Confirm destructive | — | — | — | — | ✅ |
| Secondary actions | — | — | item buttons | ✅ ActionPanel | ✅ Tab action panel |
| Global shortcuts | — | ✅ | ✅ | ✅ | ✅ chords + sequences |
| Detail pane | — | — | — | ✅ | ✅ template |
| Virtualization | — | ✅ | ✅ | ✅ | ✅ opt-in |
| ARIA combobox | ✅ | ✅ | ✅ | n/a | ✅ + live region |
| Theming | CSS | CSS | — | — | ✅ CSS vars, dark, unstyled |
| Templates | composable | composable | — | — | ✅ 8 render fragments |

## Design decisions

- **Native `<dialog>` + `showModal()`** instead of a hand-rolled overlay: free focus trap, top layer, backdrop, Escape handling (intercepted via `cancel` so Escape can mean "back").
- **Navigation keys handled in JS, typing in Blazor**: keydown for arrows/Enter/Tab/Escape is intercepted in the collocated module with `preventDefault`, then forwarded to .NET. Blazor's `preventDefault` is all-or-nothing and async over SignalR, so Tab would otherwise leave the dialog and Backspace would delete text when it should pop a page.
- **Global shortcut matching in JS**: registered shortcuts are normalised strings (`mod+k`, `g i`) synced to the browser; only matches cross the interop boundary, so typing never produces chatty round-trips in Blazor Server.
- **Immutable `Command` + fluent builder**: easy to share, derive (`With`) and re-register when state changes; identity (`Id`) is preserved across copies so history and selection stay valid.
- **Everything flattened into rows**: groups and items become one list so keyboard navigation, `aria-activedescendant`, scrolling and `Virtualize` all operate on a single model.
- **Scoped service + singleton options**: `ICommandPaletteService` is per circuit/tab (registry, open state, events); options are process-wide.
