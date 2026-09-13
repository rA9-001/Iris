# Iris

> *îris* (ἶρις) — the rainbow, and the messenger who travelled it. The same word gives the
> eye its iris, the ring that opens and closes to let the light in.

A video player for Windows. Opens in about 0.7 seconds, plays anything, and gets out of the
way. No media library, no toolbars, no update nagger.

Iris does none of its own decoding — that goes to libvlc, which plays essentially every
container and codec you will meet (MP4, MKV, AVI, MOV, WebM, TS, FLV, HEVC, AV1, VP9 …)
with hardware decoding on by default. Everything above the picture is custom.

## Install

Build it (see [Building](#building)) and put `dist\` wherever you want it to live.
`Iris.exe` and the `libvlc\` folder next to it must travel together.

There is no installer, and nothing is written outside your own user account.

**To make Windows open videos with it:** start Iris, click *Set Iris as your default
player* on the start screen, then *Set up Iris*. That puts Iris in the **Open with** menu
for all 24 media types, adds a **Play with Iris** entry to the right-click menu, and makes
it the default for every type Windows allows.

> Windows reserves file types another installed app is already set to — those it will only
> let you change yourself, and the dialog says which and offers to open the right Settings
> page. See [internals](docs/internals.md) for exactly what is written and why.

`Iris.exe --set-default` does the same without opening a window, and `Iris.exe --remove`
takes every key back out again.

## Controls

| Key | Action |
| --- | --- |
| `Space` / `K` | Play / pause |
| `←` / `→` | Skip 5s (hold `Shift` for 1s) |
| `J` / `L` | Skip 10s |
| `.` | Step one frame on (paused) |
| `↑` / `↓` | Volume ±5% |
| `M` | Mute |
| `F` / `Enter` | Fullscreen |
| `Esc` | Leave fullscreen |
| `Ctrl+←` / `Ctrl+→` | Previous / next file in the folder |
| `Ctrl+↑` / `Ctrl+↓` | Playback speed, 0.5× to 2× |
| `Ctrl+0` | Back to normal speed |
| `O` | Open a file |
| `T` | Trim a clip out of this video |
| `I` | Set the trim start at the playhead |
| `S` | Copy the current frame to the clipboard |
| `R` | Cycle stop / repeat / play next |
| `Home` / `End` | Jump to start / end |
| `0`–`9` | Jump to that tenth of the file |
| `Ctrl+,` | Settings |

Everything on the keyboard is also a button, except the frame step.

## Cutting a clip out

`T` opens the trim bar: drag the handles, or set the edges at the playhead with `I` and `O`.
*Save clip* writes a new file next to the original and leaves the source untouched.

The cut is **frame accurate**, not keyframe accurate — asking for ten seconds gives ten
seconds, not the nine-to-sixteen a keyframe cut would. It renders through
`Windows.Media.Editing`, the same engine the built-in Windows player trims with, so it is
hardware accelerated and needs no extra binaries. That choice was measured: libvlc's own
stream copy was out by as much as 6.2 seconds on the same file.

## The rest of the folder

Open one video and Iris lists the others beside it, sorted the way Explorer sorts them.
Arrows either side of the file name step through them, as do `Ctrl+←` and `Ctrl+→`.

**When a file ends** it can stop, repeat, or play the next one — one setting with three
states, in Settings or by pressing `R` while watching.

## Language

English and German, switched in Settings or on the very first run. The change applies
immediately, including to the text Windows itself shows: the right-click entry, the
description in Settings, and the file type names.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish src/Iris -c Release -o dist
```

That produces `dist\Iris.exe` and the `libvlc\` folder it needs. Run the exe directly;
**do not use `dotnet run`** day to day, as it re-runs the build and takes about seven times
longer to reach a window.

`IRIS_TRACE=1` writes a startup phase breakdown to `%TEMP%\iris-trace-<pid>.log`.

### Layout

```
src/Iris/Shell/         the windows: MainWindow and its partials, settings and integration dialogs
src/Iris/Media/         PlaybackEngine (libvlc), TrimExport (rendering a clip), MediaTypes
src/Iris/Interop/       Win32: dark caption, fullscreen bounds, sleep suppression, file associations
src/Iris/Localization/  TextKey, Loc, and one file per language
src/Iris/Startup/       single-instance claim and launch tracing
src/Iris/Theme/         palette, icon geometry, control templates
docs/internals.md       why it is built the way it is
```

## Deliberately not included

A media library, playlists, streaming, codec packs, auto-update, telemetry, and any kind of
account. Each of those is what turns a video player into an application you have to manage.

## Known limitations

- **No reverse frame step.** libvlc has a forward step and no reverse one, and a seek is
  not a substitute: a seek moves the position but does not reliably repaint the video —
  measured, the picture changed on two of eight single-frame seeks while the clock advanced
  on every one. Doing it properly means caching decoded frames in memory, which is a real
  feature rather than a small fix.
- **No subtitle or audio track selection yet.** libvlc has already parsed those tracks;
  Iris does not yet ask for them. This is the biggest gap.
- **Trimming needs Windows 10 version 2004 or newer**, because that is where
  `Windows.Media.Editing` arrived. Everything else runs on older builds.
- **The controls are custom-drawn**, so there is no keyboard focus traversal and screen
  readers see very little of the chrome.

## License

MIT. See [LICENSE](LICENSE).
