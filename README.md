# Pluto

A small bot with dotted eyes and a smile that lives at the top of your screen and keeps an eye on every AI coding agent on
your PC: Claude Code (also in the Claude app and VS Code), Codex, Gemini CLI, Cursor and Copilot CLI, in any
terminal or editor, and every agent in [Hiveory](https://github.com/raktim-yoddha/hiveory) with its pet name,
Workspace and Worktree. It peeks out when one finishes or needs you.

Native Windows app: WinUI 3 (Windows App SDK) on .NET 10, Windows' own desktop acrylic, native menus and lists, a
NativeAOT hook relay. No Electron, no browser engine.

## Using it

Pick a look in Settings › Theme: **Windows** (the default: your light or dark mode, Windows' own acrylic, your
accent), **Black** (true black, white ink, dot-matrix headings) or **White** (its inverse). In Black and White the
only colour is a red dot when an agent needs you.

| You | Pluto |
|---|---|
| Nothing is happening | Tucked into its notch at the top of the screen, just its eyes (floating: just its face); sleepy after 10 quiet minutes |
| An agent starts, finishes, needs you or fails | A short note in place of the face for three seconds ("Codex is done · Done in 4:12", "Maple needs you"), with a soft sound; the pet never grows for it |
| Click | Opens the list: **All** agents, **Hiveory** (only when Hiveory is on) and **To Do**; click a session to bring its terminal, editor or app forward |
| Drag | Drop it near the top edge to make a notch right there, anywhere along it; anywhere else it floats |
| ⚙ or right-click | Settings |

**Which agents:** every coding agent CLI Hiveory knows (36 of them: Claude Code, Codex, Gemini CLI, Copilot CLI,
Cursor CLI, OpenCode, Qwen Code, Amp, Aider, Goose, Crush, Kiro, Droid…) shows up while it runs, with its official
icon, its project folder and the app it runs in: sessions in the Claude app's Code tab, the Codex app, VS Code,
Cursor, Terminal. For Claude Code, Codex, Gemini CLI, Cursor and Copilot CLI you can also turn on hooks, which add
live status: working, needs you, done.

**To Do:** a daily list in the To Do tab: add, check, rename, delete and drag tasks into order. A new day starts at
the time you choose; the finished day moves to History, where you can bring back its unfinished tasks. Settings ›
To Do holds the options (when a day starts, where checked tasks go, how they look, how progress shows, how long
History keeps days, start a new day now, uncheck all); Settings › History shows the past days.

**Settings** (a Windows Settings–style window): theme, where Pluto lives (top edge or floating), the agents to
watch, Hiveory, sounds, start with Windows, To Do and History. Turning an agent on adds a small hook to that
agent's own config (`~/.claude/settings.json`, `~/.codex/hooks.json`, `~/.gemini/settings.json`,
`~/.cursor/hooks.json`, `~/.copilot/hooks/pluto.json`), next to yours, and keeps a copy of the original file the
first time (`*.before-pluto`). Turning it off removes only its own entries. Codex asks you to trust new hooks once
(`/hooks`). Hooks from the earlier Hiveory Pet are switched to Pluto on their own.

**Hiveory:** turn on Settings › Hiveory in Pluto, and in Hiveory (from its next release, and any dev build)
Settings › Agents › Let Pluto see your agents. Pluto connects on its own, to the release and a dev build at the
same time if both run. The Hiveory tab lists every Hiveory agent by its pet name, CLI, workspace and worktree
("Dev" marks a dev build's); agents running inside Hiveory show only there. Hiveory shares names and statuses
only, over a local link.

## How it works

```
agent hook ──stdin──▶ pluto-hook.exe ──named pipe (this user only)──▶ pet ◀──loopback SSE── Hiveory
```

- `src/Pluto.Hook` — the relay each hook runs. Wraps the hook JSON with the agent's name, the Hiveory agent
  it runs in (`HIVEORY_INSTANCE_ID`) and the window that holds it, then writes it to
  `\\.\pipe\pluto-<your SID>` (`PipeOptions.CurrentUserOnly` on both ends). Prints nothing, always exits 0,
  about 20 ms.
- `src/Pluto.Core` — everything but the window, no UI types:
  - `Agents/` parses every agent's events into one shape, runs the session state machine (Idle · Working · Needs
    you, the same three states as Hiveory's board), installs hooks and listens on the pipe. `ProcessScanner` finds
    running agents every 3 s from the process list (program names, npm packages in node's command line, each
    process's working folder and the app above it).
  - `Hiveory/` is the Hiveory link: reads `pet-link.json` from `%LOCALAPPDATA%\Hiveory\Runtime` and `%LOCALAPPDATA%\Hiveory Dev\Runtime` and follows
    `GET /pet/events` (Hiveory's ADR 0054).
  - `Board.cs` makes one list from both (a hook session inside a Hiveory agent shows once), `Placement.cs` the
    notch and floating positions, `PetViewModel.cs` what the pet shows and when, and what Settings binds to.
- `src/Pluto` — the WinUI 3 window: a borderless, always-on-top `AppWindow` that never takes focus, with a
  `DesktopAcrylicController` backdrop. Size changes glide on a spring; the dot-matrix face (`BotFace.cs`) blinks,
  breathes, hops and looks at your cursor with composition animations, so it all runs at the display's frame rate.
  `SettingsWindow` is a Mica window with the system title bar and Windows Settings cards (`SettingCard`).
  `Themes.cs` holds the three looks as theme-resource overrides on each window's root.
- `src/Pluto.Core/Todos` — the To Do: `TodoStore` (the day, rollover into History, the options; saved to
  `%LOCALAPPDATA%\Pluto\todos.json`) and `TodoViewModel`.

## Logo

`scripts/make-logo.ps1` draws Pluto's mark (the dotted eyes and smile) into `src/Pluto/Assets/Logo`.

## Icons

`src/Pluto/Assets/Agents` holds the same official marks Hiveory uses, copied with
`node scripts/import-icons.mjs <Hiveory>/src/main/services/cli/adapters/icon-data.ts`.

## Privacy

Everything stays on your PC. The pet keeps sessions in memory only: agent, project folder name, a few words about
the current step ("Editing App.cs"). Settings and To Do live in `%LOCALAPPDATA%\Pluto`. Prompts, file contents and command output are never stored. No network
access except Hiveory's link on 127.0.0.1.

## Build

Requires Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet test tests/Pluto.Tests  # 42 tests
dotnet build src/Pluto          # then run src/Pluto/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Pluto.exe
pwsh scripts/publish.ps1             # out/Pluto-win-x64.zip, self-contained (NativeAOT relay needs the VS C++ workload)
```

Set `HIVEORY_PET_DEBUG=1` to make the relay explain on stderr why an event was not delivered.

## License

MIT. Parts are adapted from coucou and Mithu AI Companion (both MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
