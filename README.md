# Iris

> *îris* (ἶρις) — the rainbow, and the messenger who travelled it. The same word gives the
> eye its iris, the ring that opens and closes to let the light in.

A video player for Windows. Opens in about 0.7 seconds, plays anything, and gets out of the
way. No media library, no toolbars, no update nagger.

Decoding goes to libvlc, which plays essentially every container and codec you will meet
(MP4, MKV, AVI, MOV, WebM, TS, FLV, HEVC, AV1, VP9 …) with hardware decoding on by default.
Everything above the picture is custom.

[![licence](https://img.shields.io/github/license/rA9-001/Iris)](LICENSE)

## Install

Build it (see [Building](#building)) and put `dist\` wherever you want it to live. `Iris.exe`
and the `libvlc\` folder next to it must travel together. There is no installer, and nothing
is written outside your own user account.

To make Windows open videos with it: start Iris, click *Set Iris as your default player*, then
*Set up Iris*. `Iris.exe --set-default` does the same without a window, and `Iris.exe --remove`
takes every key back out.

## Controls

| Key | Action |
| --- | --- |
| `Space` / `K` | Play / pause |
| `←` / `→` · `J` / `L` | Skip 5s (`Shift` 1s) · 10s |
| `.` | Step one frame on (paused) |
| `↑` / `↓` · `M` | Volume ±5% · mute |
| `F` / `Enter` · `Esc` | Fullscreen · leave it |
| `Ctrl+←` / `Ctrl+→` | Previous / next file in the folder |
| `Ctrl+↑` / `Ctrl+↓` / `Ctrl+0` | Playback speed 0.5×–2× · reset |
| `O` · `S` | Open a file · copy the current frame |
| `T` · `I` | Trim a clip · set the trim start at the playhead |
| `R` | Cycle stop / repeat / play next |
| `Home` / `End` · `0`–`9` | Jump to start / end · to that tenth |
| `Ctrl+,` | Settings |

Everything on the keyboard is also a button, except the frame step.

## Cutting a clip

`T` opens the trim bar; *Save clip* writes a new file next to the original and leaves the
source untouched. The cut is **frame accurate**, not keyframe accurate — asking for ten
seconds gives ten seconds. It renders through `Windows.Media.Editing`, so it is hardware
accelerated and needs no extra binaries.

Open one video and Iris lists the rest of the folder beside it. The interface is English or
German, switched in Settings or on first run.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish src/Iris -c Release -o dist
```

That produces `dist\Iris.exe` and the `libvlc\` folder it needs. Run the exe directly — `dotnet
run` re-runs the build and takes about seven times longer to reach a window.

## Docs

[How it works](docs/internals.md) — launch time, rendering, file associations, layout, limitations.

## License

MIT. See [LICENSE](LICENSE).
