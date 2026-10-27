# Pluto

A small bot with dotted eyes and a smile that lives at the top of your screen and keeps an eye on every AI coding agent on
your PC: Claude Code (also in the Claude app and VS Code), Codex, Gemini CLI, Cursor and Copilot CLI, in any
terminal or editor, and every agent in [Hiveory](https://github.com/raktim-yoddha/hiveory) with its pet name,
Workspace and Worktree. It peeks out when one finishes or needs you.

Native Windows app: WinUI 3 (Windows App SDK), compiled ahead of time to one native exe (NativeAOT, no .NET install),
a notch drawn and animated on Windows' compositor, native menus and lists, a NativeAOT hook relay. No Electron, no
browser engine. It runs on Windows' shared Windows App Runtime, part of Windows 11; a PC without it gets it once, from
Microsoft, when Pluto first starts and you say yes.

## Using it

Pluto looks like Windows: Windows' own acrylic, light or dark as Windows is set, with your accent. Settings picks
the **size** (Small, Medium, Large) and the **eyes** (dots or ovals).

| You | Pluto |
|---|---|
| Nothing is happening | Tucked into its notch at the top of the screen, just its eyes (floating: just its face) |
| Point at it | It pops, just a little |
| An agent starts, finishes, needs you or fails | A short note hanging from the top edge for three seconds ("Codex is done · Done in 4:12", "Maple needs you"), with a soft sound |
| Click | The notch glides open into the list: **All** agents, **Hiveory** (only when Hiveory is on) and **To Do** (click a section; a soft pill marks the chosen one); click a session to bring its terminal, editor or app forward |
| Click anywhere else, Escape, or leave it alone for 5 seconds | The list glides closed (not while you type into it) |
| Drag (open, by its heading) | Drop it near the top edge to make a notch right there, anywhere along it; anywhere else it floats |
| ⚙ or right-click | Settings |

**Which agents:** every coding agent CLI Hiveory knows (36 of them: Claude Code, Codex, Gemini CLI, Copilot CLI,
Cursor CLI, OpenCode, Qwen Code, Amp, Aider, Goose, Crush, Kiro, Droid…) shows up while it runs, with its official
icon, its project folder and the app it runs in: sessions in the Claude app's Code tab, the Codex app, VS Code,
Cursor, Terminal. For Claude Code, Codex, Gemini CLI, Cursor and Copilot CLI you can also turn on hooks, which add
live status: working, needs you, done.

**To Do:** a daily list in the To Do tab. Point at a task for its grip (drag to move it), rename and delete; double-click
or F2 renames, Delete deletes, Alt+Up and Alt+Down move. A new day starts at the time you choose; the finished day
moves to History, where you can bring back its unfinished tasks. History keeps 1 week, 2 weeks, 1 month, 3 months or
1 year: each new day lets the oldest day go. Every change is saved right away to `%LOCALAPPDATA%\Pluto\todos.json`
(Settings › To Do shows when, and the file). Settings › To Do holds the options (when a day starts, where checked
tasks go, how they look, how progress shows, how long History keeps days, start a new day now, uncheck all);
Settings › History shows the past days.

**Sound:** each moment has its own sound: Pluto's (a soft tap when an agent starts, a two-note chime when it's done,
a gentle knock when it needs you), none, or your own file (WAV, MP3, M4A, WMA, FLAC or AAC, copied into Pluto's
folder). One volume for all.

**Floating capsule:** lit from above, with a soft rim that runs light to dark; it fades away as the capsule opens into
the list. In the notch Pluto stays one solid colour, like a hardware notch.

**Settings** (a Windows Settings–style window that stays on top with Pluto): where Pluto lives (top edge or
floating), size, eyes, the agents to watch, Hiveory, sound, start with Windows, To Do and History.

**Always on top:** Pluto and its Settings go back on top whenever another window comes to the front, full-screen
apps included. A game in exclusive full screen draws past every window, so nothing can show over it; set the game to
borderless or windowed full screen to see Pluto there. Turning an agent on adds a small hook to that
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
- `src/Pluto` — the WinUI 3 window: a see-through, always-on-top `AppWindow` of one fixed size that never takes focus
  while small (`ClearBackdrop`, `Native.cs`); only the part under Pluto takes the mouse. `NotchSurface` draws Pluto's
  body on the compositor (the curved shoulders where the notch meets the top edge, the rounded bottom) and reshapes it
  on compositor springs, with the content clipped to the same geometry, so opening, closing and the hover pop run at
  the display's frame rate without resizing the window. The face (`BotFace.cs`) blinks, breathes, hops and looks at
  your cursor with composition animations. `Chime.cs` makes Pluto's sounds on the fly.
  `SettingsWindow` is a Mica window with the system title bar and Windows Settings cards (`SettingCard`).
  `Themes.cs` holds the few colours the compositor draws itself, light and dark.
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

## Install

From [Releases](https://github.com/raktim-yoddha/pluto/releases/latest) (Windows 10/11, x64; nothing else to install):

- **Pluto-Setup.exe**: one click installs Pluto for you (no admin), adds it to the Start menu and Apps, and starts it.
- **Pluto-Portable.zip**: unzip anywhere and run `Pluto.exe`; nothing is installed.

**Updates:** both look for a new version soon after Pluto starts and every few hours (Settings › About › Check for
updates automatically). A new version downloads in the background; then a note drops from the notch, the gear in the
list gets a dot, and Settings shows a banner with "Restart now". It starts with the next restart either way.
Uninstalling (Settings › Apps) also takes Pluto's hooks out of your agents' configs.

## Build

Requires Windows 10/11 and the .NET 10 SDK.

Run the dev build (closes a running Pluto, restores, builds the latest code and starts it), from any folder, in cmd
or PowerShell:

```powershell
.\dev.cmd                  # from the repository folder; or its full path from anywhere
```

```powershell
dotnet test tests/Pluto.Tests   # the tests
pwsh scripts/pack.ps1 -Version 0.2.0   # out/Releases: Setup.exe, portable zip, update packages (needs `dotnet tool install -g vpk`;
                                       # the NativeAOT relay needs the VS C++ workload)
```

Releases follow [Semantic Versioning](https://semver.org); see [CHANGELOG.md](CHANGELOG.md).

Set `HIVEORY_PET_DEBUG=1` to make the relay explain on stderr why an event was not delivered.

## License

MIT. Parts are adapted from coucou and Mithu AI Companion (both MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
