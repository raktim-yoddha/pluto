# Pluto — notes for coding agents (AGENTS.md = CLAUDE.md, keep both identical)

- The window is **WinUI 3** (Windows App SDK). Use the project skill `winui3-full-skill`
  (`.claude/skills/winui3-full-skill`, pinned in `skills-lock.json`) for any XAML or Windows App SDK work:
  x:Bind with x:DataType, AutomationProperties.Name on every control, no business logic in code-behind.
- Prefer native Windows pieces: composition (`NotchSurface`, springs), Mica, `MenuFlyout`, `ListView`, `FontIcon`, implicit
  transitions and composition animations. Never WPF or WinForms.
- `src/Pluto.Core` has no UI types and holds every decision (what shows, when, where); the window only wires
  UI to `PetViewModel`. Tests live in `tests/Pluto.Tests` and cover Core.
- The hook relay prints nothing and always exits 0. Hooks never answer a permission.
- Only names, statuses and short activity text ever leave an agent's hook; prompts, file contents and output never do.
- Hiveory's side of the link is its ADR 0054; change the wire format there and here together.
- Releases: Semantic Versioning (`Version` in `Directory.Build.props`, notes in `CHANGELOG.md`, tag `vX.Y.Z`).
  `scripts/pack.ps1 -Version X.Y.Z` builds every asset in `out/Releases` (Setup.exe, portable zip, the Velopack update
  packages and `releases.win.json`); attach all of them to the GitHub release, or installed copies can't update.
  Before 1.0: features bump MINOR, fixes PATCH.
