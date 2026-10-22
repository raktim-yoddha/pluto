# Hiveory Pet

A small black-and-white bot that lives at the top of your screen and keeps an eye on every AI coding agent on
your PC: Claude Code (also in the Claude app and VS Code), Codex, Gemini CLI, Cursor and Copilot CLI, in any
terminal or editor, and every agent in [Hiveory](https://github.com/raktim-yoddha/hiveory) with its pet name,
Workspace and Worktree. It peeks out when one finishes or needs you.

Native Windows app: C# / WPF on .NET 10, a NativeAOT hook relay, no Electron, no browser engine.

## Using it

| You | The pet |
|---|---|
| Nothing is happening | Tucked against the edge, just its eyes (floating: just its face) |
| An agent works | A pill: "2 agents working · Editing Cart.cs", thinking dots |
| An agent needs you | A red dot, "Codex needs you · Needs your OK: Running npm test", a ping |
| An agent finishes | A happy hop, "Finished · 4:12 · 3 edits", a chime |
| Hover | It opens to the pill |
| Click | The list of every session; click one to bring its window forward |
| Drag | Float it anywhere; drop it near an edge spot to dock it again |
| ⚙ or right-click | Position, Agents, Connect Hiveory, Sounds, Start with Windows, Quit |

**Positions:** top center (the notch), top left, top right, bottom left, bottom center, bottom right, floating.

**Agents:** under Settings › Agents, tick the agents to follow. The pet adds a small hook to that agent's own
config (`~/.claude/settings.json`, `~/.codex/hooks.json`, `~/.gemini/settings.json`, `~/.cursor/hooks.json`,
`~/.copilot/hooks/hiveory-pet.json`), next to yours, and keeps a copy of the original file the first time
(`*.before-hiveory-pet`). Unticking removes only its own entries. Codex asks you to trust new hooks once (`/hooks`).

**Hiveory:** click Connect Hiveory. Hiveory (from its next release, Settings › Agents › Hiveory Pet) shares its running
agents' names and statuses over a local link; nothing else.

## How it works

```
agent hook ──stdin──▶ hiveory-pet-hook.exe ──named pipe (this user only)──▶ pet ◀──loopback SSE── Hiveory
```

- `src/HiveoryPet.Hook` — the relay each hook runs. Wraps the hook JSON with the agent's name, the Hiveory agent
  it runs in (`HIVEORY_INSTANCE_ID`) and the window that holds it, then writes it to
  `\\.\pipe\hiveory-pet-<your SID>` (`PipeOptions.CurrentUserOnly` on both ends). Prints nothing, always exits 0,
  about 20 ms.
- `src/HiveoryPet/Agents` — parsing every agent's events into one shape, the session state machine
  (Idle · Working · Needs you, the same three states as Hiveory's board), the hook installer and the pipe.
- `src/HiveoryPet/Hiveory` — the Hiveory link: reads `%LOCALAPPDATA%\Hiveory\Runtime\pet-link.json` and follows
  `GET /pet/events` (Hiveory's ADR 0054).
- `src/HiveoryPet/Board.cs` — one list from both; a hook session inside a Hiveory agent shows once.
- `src/HiveoryPet/Ui` — the window, the dot-matrix face, the chimes.

## Privacy

Everything stays on your PC. The pet keeps sessions in memory only: agent, project folder name, a few words about
the current step ("Editing App.cs"). Prompts, file contents and command output are never stored. No network
access except Hiveory's link on 127.0.0.1.

## Build

Requires Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet test HiveoryPet.slnx         # 30 tests
dotnet run --project src/HiveoryPet  # run it
pwsh scripts/publish.ps1             # out/HiveoryPet-win-x64.zip (NativeAOT relay needs the VS C++ workload)
```

Set `HIVEORY_PET_DEBUG=1` to make the relay explain on stderr why an event was not delivered.

## License

MIT. Parts are adapted from coucou and Mithu AI Companion (both MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
