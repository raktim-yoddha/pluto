# Changelog

Pluto follows [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`. Before 1.0, new features bump MINOR
and fixes bump PATCH.

## 0.3.2 — 2026-10-29 — To-dos you can rename and rearrange

- Pressing Enter after renaming a task no longer freezes Pluto and closes it (the same could happen after checking,
  deleting or moving a task with the keyboard).
- Dragging a task by its grip moves it again, all the way to the top or bottom of the list.
- Double-clicking renames a task only on its words, not on the grip or the empty space beside them.
- Settings › History: "Delete day" and "Copy unfinished to today" work every time (after the first change, they did
  nothing), and checking a task no longer redraws History.
- An unexpected error is written to the log instead of closing Pluto and Settings.

## 0.3.1 — 2026-10-28 — Every status shows, Pluto stays in sight

- Every status note shows the moment it happens. When several come at once, they show one after another, and none are
  lost (before, the newest replaced the rest, and only one change per Hiveory update was shown).
- When Windows keeps Pluto from staying on top of other windows, Pluto keeps asking until it is in front again; it
  could stay hidden behind full-size windows after installing.

## 0.3.0 — 2026-10-27 — Six times smaller, one Windows look, Settings fixed

- Pluto-Setup.exe is 16 MB instead of 100 MB, and updates download about 5 MB: Pluto is now one native program on
  Windows' shared Windows App Runtime (part of Windows 11), without its own copy of .NET or AI libraries it never used.
  A PC without the runtime gets it once from Microsoft, the first time Pluto starts and you say yes.
- Pluto has one look, Windows' own acrylic, light or dark as Windows is set; the Black and White themes are gone.
- Opening Settings no longer makes Pluto crash.
- Settings opens in place, already drawn: no black window that jumps to the middle first.
- Pluto's logo shows on the taskbar and in Windows' app lists, instead of a blank app icon.

## 0.2.4 — 2026-10-27 — Calm, reliable docking and undocking

- Sticking Pluto to the top edge or pulling it away never makes it vanish or leave stray edges for a moment.
- No more bouncing: every move eases into place without overshooting.
- While you drag Pluto, its face stays with it.

## 0.2.3 — 2026-10-26 — Pointing is precise

- Pluto reacts only when the pointer is on it: it no longer pops or takes clicks from a distance.

## 0.2.2 — 2026-10-26 — Clicks work again

- Clicking Pluto opens the list again (0.2.1 ignored clicks).

## 0.2.1 — 2026-10-26 — Acrylic notch, depth on every theme, steadier dragging

- In the Windows theme the notch and the capsule are Windows' own acrylic, cut to their shape and gliding open with them.
- Black and White bodies stand out from backgrounds of their own colour: a soft shadow and a faint light along the edge.
- Dragging is simpler: Pluto is a small pill while it moves and is simply there when you let go.
- A fast drag no longer drops Pluto halfway: it follows the cursor until you let go of the button.

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
