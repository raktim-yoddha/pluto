# Changelog

Pluto follows [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`. Before 1.0, new features bump MINOR
and fixes bump PATCH.

## 0.2.0 — 2026-10-26 — Install with Setup.exe, and Pluto updates itself

- Pluto-Setup.exe installs Pluto in one click (Start menu, Apps & features); Pluto-Portable.zip runs from any folder.
- Pluto looks for new versions on its own and downloads them in the background, then tells you: a note from the
  notch, a dot on the gear and a "Restart now" banner in Settings. Settings › About can check now or turn it off.
- "Start with Windows" always starts the copy of Pluto you are using, even after it moved.
- Uninstalling takes Pluto's hooks out of your agents' configs.

## 0.1.1 — 2026-10-26 — Steady list while agents work, clean theme switching

- The open list no longer flickers and bounces while an agent works: each agent's row updates in place instead of
  the whole list being rebuilt every second.
- Changing the theme no longer leaves Pluto or its Settings half in the old colours.

## 0.1.0 — 2026-10-26 — First release: Pluto watches every coding agent from a notch

- A small bot with dotted or oval eyes lives in a notch at the top of your screen (or floats anywhere) and shows
  every AI coding agent on your PC: Claude Code (also in the Claude app and VS Code), Codex, Gemini CLI, Cursor,
  Copilot CLI and 30 more, each with its official icon, folder and the app it runs in.
- When an agent starts, finishes or needs you, a short note drops from the notch with a sound: Pluto's own chimes,
  silence, or your own sound file.
- Click the notch and it glides open into the list: All agents, Hiveory's agents by pet name (when Hiveory is
  connected) and a daily To Do with reordering, renaming, a daily reset and History of up to a year.
- Three looks (Windows, Black, White), three sizes, and a notch with curved shoulders; it pops when you point at it,
  can be dragged anywhere, and stays on top of other windows.
- Native Windows app (WinUI 3); everything stays on your PC.
