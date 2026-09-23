# Iris internals

How Iris is put together, and why. Most of this is the reasoning behind decisions that look
arbitrary from the outside — startup timing, the flicker-free launch, what libvlc will and
will not do, and how Windows file associations actually work.

See the [README](../README.md) for what Iris is and how to run it.

## Launch time

Measured from process start, with `IRIS_TRACE=1` writing a phase breakdown to
`%TEMP%
ova-trace-<pid>.log`. Two cases matter, and they are very different:

| | window visible | first video frame |
| --- | --- | --- |
| warm — Iris run before | **~0.70s** | ~0.90s |
| cold — first run after a download or a reboot | **~1.6s** | ~1.9s |
| `dotnet run` | ~5.9s | — |

Three things get it there:

- **The engine starts in parallel.** Building libvlc means scanning 323 plugin DLLs.
  That used to happen in the constructor, before the window could be shown at all. It now
  runs on a worker started at the top of the constructor, so it overlaps the ~700ms WPF
  needs anyway and is ready at ~0.33s — well before the window. Opening a file is then
  immediate, and a bare launch never waits for the engine. Everything that touches the
  player tolerates one that has not arrived yet.
- **The plugins are read in parallel too.** See below — this is the whole cold-start story.
- **ReadyToRun.** Roughly half the remaining time was JIT. Precompiling the app and its
  dependencies to native code removed about 110ms.

What is left warm is WPF itself: ~180ms of runtime and framework load, ~230ms parsing
XAML, and ~200ms for the first layout and render. That is close to the floor for a WPF
window.

#### The cold start, and the ten seconds it used to cost

**libvlc here has no plugin cache.** VLC normally ships a `plugins.dat` built by
`vlc-cache-gen` at packaging time; the `VideoLAN.LibVLC.Windows` package ships none, and
libvlc only ever reads that file — it never writes one. There is no cache anywhere on
disk after a run, so every single start dlopens all 323 plugin DLLs, one after another.

Warm that costs about 300ms and nobody would notice. Cold — a fresh download, or the
first launch after a reboot — each of those opens is a separate synchronous
on-access virus scan, and they were happening strictly one at a time:

| | engine ready | first video frame |
| --- | --- | --- |
| before | 10.4s | 10.7s |
| after | **1.4s** | **1.9s** |

The files have to be read either way. The only thing wrong with the original was waiting
for them one at a time, so `PrewarmPlugins` reads all of them on many threads while
libvlc scans, and libvlc keeps finding the next file already in memory. Warm timings are
unchanged, because there the reads cost nothing.

It deliberately uses **dedicated threads, not the thread pool**: every one of these
blocks on the scanner, and the pool adds blocked threads back at roughly one per second,
which throttles exactly the parallelism this needs. Measured on a 20-core machine,
engine-ready went 10.4s → 2.1s at 8 threads, 1.8s at 16, 1.6s at 32 and 1.4s at 64, so it
scales the count with the CPU and clamps it to 16-64. The work is waiting, not computing.

To reproduce a cold start without rebooting, re-copy the plugin tree — the files are then
new to the scanner again:

```
Remove-Item dist\libvlc\win-x64 -Recurse -Force
Copy-Item bin\Release\net10.0-windows\libvlc\win-x64 dist\libvlc\win-x64 -Recurse
```

Trimming the plugin set is a second, smaller lever that stays rejected. The groups Iris
plainly cannot use (streaming output, muxers, visualisations, service discovery) are only
about 50 of the 323 files, so with the reads already parallel they are worth a fraction of
a second — not worth the risk of losing a codec later.

## Settings, and the language

**Settings** is the sliders icon in the control bar, the *Settings* link on the start
screen, or `Ctrl+,`. It holds the language, what happens when a file ends, and a way into
the Windows integration below; everything else about Iris is meant to need no configuring.

Iris ships in **English and German**, and starts in English. German is chosen either in
Settings or on the very first run — the setup dialog carries the same choice, labelled
*Language · Sprache* in both languages, because that is the one screen a German speaker is
guaranteed to meet before anything has been configured.

Switching is live. Every label in XAML is written as `{loc:Str SomeKey}`, which is a
binding to an indexer on `Loc.Current` rather than a fixed string, so changing the language
raises one property-changed notification and every open window re-reads itself — the player
behind the dialog included. Text that is built in code rather than bound (the folder
arrows' tooltips, the dialog panes) re-applies itself from the `Loc.Changed` event.

Numbers follow the language rather than the Windows region, so German shows
`10,5 s ausgewählt` and English `10.5s selected`. `Loc.Get` formats through `Loc.Culture`,
which is picked from the chosen language — a German window showing an English decimal
point because the OS region says so would be the wrong way round.

**The text Windows shows is translated too**, not just the text inside Iris: the
*Mit Iris abspielen* right-click entry, the description in Settings, and the type names
(`MP4-Video`, `MPEG-Transportstrom`). Those live in the registry, so changing the language
re-registers to rewrite them. The type names are composed from a pattern — `{0} Video` in
English, `{0}-Video` in German — rather than stored one by one, since only the word around
the format name changes. Three of them opt out of the pattern: `.ts` is not
"&lt;format&gt; Video" at all, and `.wmv`/`.wma` would come out as "Windows Media-Video",
where the hyphen binds the wrong two words.

Adding a language means adding one file next to `Localization/English.cs` and one case in
`Loc.Use`. The keys live in a `TextKey` enum rather than as loose strings, so a typo is a
build error in code and a load error in XAML, and a table that is missing an entry falls
back to English instead of showing a blank. There are 100 keys; a table with all of them is
the only thing a new language needs.

Coverage is checked mechanically rather than by eye — both tables define all 100 keys, no
key is unused, and no translation disagrees with English about how many `{0}` placeholders
it takes, which would otherwise throw at runtime only in the other language. Strings that
take arguments are rendered with realistic values in both languages and read back, because
a missing translation is invisible in the source: it looks exactly like a string nobody
localised yet. That is how `10s selected` survived the first pass — the sweep looked for
`.Text = "…"` and never saw it, because it was built inside an interpolated string.

## When a file ends, speed, and volume

**When a file ends** is one setting with three states rather than two toggles, because
"loop this file" and "play the next one" cannot both happen and a pair of checkboxes would
let you ask for both:

- **Stop** — stay on the last frame. Pressing play starts the file again.
- **Repeat** — loop the same file.
- **Play next** — continue with the next file in the folder, in the order the arrows use.
  On the last file it stops rather than wrapping round, which would look like a video that
  never ends.

`R` cycles it while watching and names the mode on the OSD. Neither repeat nor play-next
fires while a clip is being trimmed or exported: switching files would throw the range
away, and looping would fight the export.

**Speed** is the `1×` next to the clock — the readout is the button. Clicking steps
through 0.5× to 2× and wraps; `Ctrl+↑` and `Ctrl+↓` step without wrapping, and `Ctrl+0`
returns to normal. It sits with the clock rather than in the icon cluster because it is a
fact about time, and because the right-hand side is already full. At 1× the label is
tertiary grey; at anything else it turns accent blue, since an unexplained-looking picture
usually has the speed as its explanation. The rate is kept across files for the session but
never saved — a fresh launch at 1.75× would be a nasty surprise.

**Volume runs to 200%**, where 100 is the untouched signal and above that libvlc amplifies.
The slider carries a small mark at unity, above the rail so neither the fill nor the thumb
can hide it. Anything above 100 can clip a track that was already loud; it is there to
rescue quiet recordings.

### Two things libvlc will not tell you

Both of these were found by reading values back rather than trusting the call:

**Neither volume nor rate sticks until the file is actually playing.** Set immediately
after `Play()`, libvlc has no audio output to apply a volume to and no input to change the
rate of, and silently keeps its own values — asking for 150 and reading back gave `80`.
Both are now applied from the frame loop the moment the state first reports playing, at
which point 150 reads back as 150. The original code set the volume straight after `Play()`
with a comment saying volume only sticks once output is attached; it was right about the
reason and still too early.

**`SetRate` returns `false` when it works.** libvlc's C function reports success as `0`
and the binding reads that as failure, so it claims to have failed every single time.
Probed against a playing file, 0.5, 1.5 and 2.0 all read back exactly. The return value is
ignored; the rate read back is the only meaningful check.

## Frame stepping, and copying a frame

**`.` steps one frame on**, paused. Forwards only — there is deliberately no `,`, and the
reason is worth recording so it does not get "fixed" back in.

libvlc has a native forward step, `NextFrame`, which decodes and displays one frame. It has
no reverse step. The obvious substitute — seek back by one frame — fails for a reason that
took a long time to see: **a seek moves the position but does not reliably repaint the
video.** Measured against the window itself, the picture changed on two of eight
single-frame seeks while libvlc's clock advanced on every one, and several never repainted
within two seconds. In use that shows up as roughly one press in four registering.

Everything built on seeking inherited that. Three further attempts failed in their own ways:

- Seek, then confirm where it landed and correct. Accurate, and about 50ms of polling per
  press against a forward step that costs nothing. It felt like lag.
- Seek and trust it. Fast, but a forward step leaves libvlc seeking to keyframes only, and
  permanently — so a step back after a step forward moved in seconds rather than frames.
- Restore precise seeking with a play/pause nudge. `Pause()` is a *toggle*, so it sometimes
  left the video playing instead; and explicit `SetPause` kept it paused but did not restore
  precise seeking at all, which is when it became clear the nudge had only ever worked by
  accidentally leaving the player running.

The honest position is that frame-accurate reverse stepping is not available on this engine.
Doing it properly means keeping recently decoded frames in memory so stepping back is a
lookup rather than a seek — which is what editing software does, and a real feature with a
real memory cost rather than a small fix.

**`S` copies the current frame to the clipboard** and says so on the OSD. libvlc can only
write a snapshot to a file, on one of its own threads, so Iris asks for one, picks the
result up from the `SnapshotTaken` event, loads it with `BitmapCacheOption.OnLoad` — which
reads it immediately rather than lazily, and is what lets the temp file be deleted straight
after — and puts it on the clipboard. The clipboard is shared and another program can hold
it open for a moment, which surfaces as a COM failure rather than a wait, so the attempt is
retried a few times before it gives up.

Verified end to end: a 1280x720 PNG written, put on the clipboard, read back at the same
size, and the temp file gone afterwards.

### The bar stopping short of the end

A finished video used to leave a sliver of unfilled track at the right-hand end, as though
it were a second short. WPF's `Track` reserves the thumb's width at the end of the bar, so
the fill at maximum stops one thumb short of the track. The fill is carried under the thumb
by a negative margin, and that margin was **half** the thumb — enough to reach the thumb's
centre, which is exactly 9px shy of the end.

It is now the whole thumb width, so at maximum the fill ends where the track does. Checked
by reading the pixels: the accent now runs to the last pixel of the track and meets the
background directly, with no grey left over.

## A file that will not play

Iris used to sit there in silence: a damaged or unsupported file gave you a black window
with the name in the title bar, a dead transport, and nothing to say what had gone wrong.
It now shows the file name and an explanation, greys out the transport, and offers to open
something else.

Catching it needs two checks, because **libvlc does not reliably raise an error for a file
it cannot make sense of.** A damaged MP4 is opened quite happily, yields no duration and no
output, and goes straight to `Ended` — which looks exactly like a video that finished. So:

- `EncounteredError` is subscribed for the honest failures. It fires on a libvlc thread and
  only posts to the dispatcher; touching the player inside its own callback is how you
  deadlock it.
- `Ended` with a length of zero is treated as a failure rather than an ending. A real file,
  audio-only included, has a duration by the time it ends.

The first version only polled for `VLCState.Error` and caught nothing at all — the test
file went straight to `Ended`, and the black window came back. Worth pointing a deliberately
broken file at any change here: the failure mode is silence, so it looks like it works.

Two states of libvlc's therefore mean different things, and the difference is the length:

- `Ended` **with a duration** is a file that finished. Acted on only once this file has
  actually been seen playing, because for a moment after `Play()` the *previous* file's
  `Ended` is still what libvlc reports, and reading that as an ending marks a video as
  finished before it has started.
- `Ended` **without one** is a file that never played. That cannot wait for a playing
  state, since a broken file never reaches one — it is counted over three ticks instead,
  because immediately after `Play()` the old `Ended` can briefly coincide with the new
  file having no length yet, and one tick of that must not raise a false failure.

### Switching files after one has ended

Opening a file resets the transport before anything else happens. libvlc reports no time
for a new file until it has actually begun — a few hundred milliseconds for a large one —
and until then the bar and the clock still showed the file that just finished: full, and
sitting at its duration. Switching after a video ended therefore made the next one look
like it had already ended too, which is exactly what it was: the previous file's end state,
left on screen.

Measured on a 92MB file, the gap between opening and the first real position was ~350ms,
with the bar sitting at `0:15 / 0:15` throughout. It now reads `0:00 / 0:00` from the
instant the file is opened.

## Making Windows open videos with Iris

Iris is portable — there is no installer — but it can still register itself with
Windows. On first launch it offers to do exactly that, and the same dialog is always
reachable from *Set Iris as your default player* on the start screen.

Accepting it writes to `HKEY_CURRENT_USER` only. No admin rights, nothing on other
user accounts, and *Remove Iris* in that dialog deletes every key it added. What you get:

- Iris in the **Open with** menu for all 24 media types, flagged as a recommended
  handler so it appears in the top level of the menu rather than behind *Choose another app*
- A **Play with Iris** entry when you right-click a media file
- Iris listed in **Settings → Default apps**, so you can assign file types to it
- Iris set as the **default player** for every type Windows leaves available

### How far "default" can honestly go

Since Windows 8 the default handler lives in a per-extension `UserChoice` key protected
by an undocumented hash. **Iris never writes one.** Forging it is fragile and hostile,
breaks on updates, and gets flagged by antivirus. Iris also never touches a type whose
`UserChoice` names an app that is still installed — that is a decision you made, and only
Windows' own UI may change it.

That still leaves a lot, because most types are not actually *decided*. Windows resolves
an extension through several records, and the ones that decide an undecided type are not
protected. For each type it is allowed to take, Iris:

- removes a `UserChoice` / `UserChoiceLatest` record that points at software no longer
  installed — the same thing *Reset* in Settings does, and the reason such a type lands on
  the "How do you want to open this?" picker every single time
- writes its ProgId as the per-user class default under `HKCU\Software\Classes`
- puts itself at the head of the extension's **Open with** most-recently-used list, which
  is what Windows falls back on once nothing valid is recorded

Deleting the stale key needs care: Windows protects it with a *Deny SetValue* rule, so any
API that opens the key for writing fails silently and deletes nothing. It has to be removed
through its parent, where removing a subkey is an ordinary right.

On the machine this was built on — where the previously chosen player had been uninstalled,
leaving 20 of 22 types pointing at software that was gone — that took Iris to all 24 types.
Before it, `.mkv` and `.mp3` landed on the picker every single time. Verified by actually
launching a file of each type and watching which process started, not by reading the
registry back: an earlier attempt reported 24 successes while having silently changed
nothing, which is exactly the failure reading keys back cannot catch.

For a type an installed app genuinely holds, the dialog says so by name and offers
*Choose default apps*, which deep-links to Iris's own page in Settings. The alternative is
to tick **Always use this app** the next time you open such a file.

`Iris.exe --set-default` performs the whole hook-up without opening a window, and exits
with the number of file types Iris ends up owning.

Because Iris is portable, moving or renaming `Iris.exe` would normally leave Explorer
pointing at a file that is gone. It doesn't: on each launch Iris compares the registered
path against where it is actually running from, and quietly re-registers if they differ.

One Windows 11 caveat: *Play with Iris* is a classic shell verb, so on Windows 11 it
lives under **Show more options** (or Shift+F10) rather than the short modern menu. The
**Open with** submenu, which is the part most people use, appears in the modern menu.

## Opening a file

Launching Iris with a file — double-click, *Open with*, *Play with Iris*, or a path on
the command line — opens it **maximised** and goes straight to the video. Launching Iris
on its own restores the size and position you last left it at and shows the start screen.

Opening a file from inside a window that is already running (the file dialog, or
drag-and-drop) leaves your window where it is; only a first launch maximises.

Maximising for a passed-in file is not treated as a preference, so it does not overwrite
the size you normally keep the window at. Resize or restore it yourself and that becomes
your preference again.

### One window

Clicking a second video never opens a second Iris. Windows has no idea the first one is
running, so it starts a new process regardless; that process finds Iris already up, hands
the file over and exits without ever building a UI. The running window takes the file,
comes to the front, and keeps the size and position it already had.

The coordination is a session-local mutex plus a named pipe. The first process to start
claims the mutex and listens; every later one posts its path down the pipe. Measured
end to end, a second double-click swaps the file in the existing window in **about
190ms**, and the handing-off process is gone by then.

Measure that lifetime by polling, not with PowerShell's `Start-Process -Wait`: `-Wait`
adds about 800ms of its own, which is easy to mistake for the app being slow to exit.

Three details this has to get right:

- **The window comes forward.** Windows only lets the process that owns the foreground
  give it away, so the handing-off process calls `AllowSetForegroundWindow` before it
  writes. Without that the file would load behind whatever you were looking at.
- **A minimised window comes back to what it was**, maximised or normal — not always
  normal. `_restoreState` deliberately ignores minimising for the same reason: being in
  the taskbar says nothing about how big you want the window, and recording it would both
  lose the state to restore and get saved as a size preference.
- **Simultaneous launches.** Select four files, press Enter, and four processes start at
  once — the winner's pipe may not be listening yet, and the pipe serves one client at a
  time. A client that is refused retries for up to three seconds. Verified: four
  at-once launches produce one window, three times out of three.

If the running instance never answers, the new process gives up waiting and opens its own
window. A second window is not what you asked for, but silently dropping the file you
just double-clicked is worse.

### How the launch stays flicker-free

Two separate things make a WPF video player flash on startup:

- **The start screen.** Showing the window and *then* loading the file lets the start
  screen render for a frame or two. The file now goes in through the `MainWindow`
  constructor, so it is collapsed before the window is ever shown.
- **A white client area.** WPF shows a window before it has composed its first frame and
  DWM fills that gap with white — measured at 80–150ms here, and impossible to miss on a
  maximised window.

The white one is the stubborn one. Setting the window class background brush dark is not
enough on its own, and neither is `WS_EX_LAYERED` with zero alpha: WPF caches the window's
extended style and rewrites it during `Show()`, silently dropping the layered bit.

What works is **DWM cloaking** (`DWMWA_CLOAK`). `Native.HideUntilPainted` cloaks the
window in `SourceInitialized`, before it is ever displayed, so sizing, positioning and
maximising all happen invisibly. `RevealWindow` uncloaks once `ContentRendered` fires,
with a two-second timer as a backstop so a window can never be stranded invisible.
Cloaking is a compositor attribute, so WPF's style bookkeeping cannot clobber it.

The dark class brush is kept as well, since it covers erases during later resizes.

Verified by capturing the screen at ~7ms intervals through launch and classifying every
frame: zero white frames, zero start-screen frames. A detector that only looks for a
*fully* white frame is not good enough — the first version of this check missed a real
150ms flash because it tested the frame mean instead of counting near-white pixels.

The overlay also stays opaque until libvlc reports `VoutCount > 0`, so the video window
is never shown before there is a picture in it. Audio-only files simply keep the dark
backdrop, which is why they never flash either.

That moment is also where the pillarbox bars get dealt with. libvlc does not paint the
picture straight into the window Iris hands it — it builds its own pair inside:

```
Static                 1920x1009     the host window VideoView creates
VLC video main         1920x1009     fills it
VLC video output       1793x1009     the picture, inset 63px
```

A 16:9 video in a wider window is pillarboxed, and those 63px bars belong to *VLC video
main*. libvlc creates that window when playback starts, long after `SetClientBackground`
walked the window tree at `SourceInitialized`, so it kept the default class brush and
erased them **white** — a white band down the left edge on roughly one launch in ten,
which is exactly as ugly as it sounds.

`Native.DarkenVideoSurface` re-walks the tree at the `VoutCount > 0` moment, sets the
dark brush and forces the erase, all while the opaque backdrop is still over the top so
the repaint is invisible. The brush belongs to the window *class*, not the window, so it
only has to win once: every vout window libvlc makes afterwards is already dark.

Measured before the fix: 1 of 10 launches, with 48% of the sampled band white. After:
0 of 30, with every run reading 0%. Resizing re-exposes the same bars, so fullscreen,
restore-down and re-maximise were each checked too.

### Fullscreen transitions

Fullscreen is entered by *staying maximised*, not by resizing. `WndProc` answers
`WM_GETMINMAXINFO` with the full monitor rectangle while `_isFullscreen` is set, so a
borderless maximised window covers the screen, taskbar included.

That matters because the obvious implementation — drop to `Normal`, then resize to the
monitor — leaves the screen uncovered for roughly 150ms in the middle, which reads as a
flicker. Measured frame by frame, the desktop was plainly visible behind the window
mid-transition.

Windows will not resize an already-maximised window in place: neither `SWP_FRAMECHANGED`
nor re-applying `SW_SHOWMAXIMIZED` makes it recompute. So for a window that is already
maximised, Iris captures the exact maximised rectangle on the way in and sets the
rectangle outright with a single `SetWindowPos` each way, keeping `WS_MAXIMIZE` intact so
the maximise button and the restore-down rectangle both still behave. From a normal
window a single `WindowState = Maximized` lands directly on fullscreen.

Verified by capturing a full-width strip of the screen at ~10ms intervals through the
transition with a uniformly dark window, where any uncovered desktop is unmistakable:
0 of 80 frames uncovered going in, 0 of 80 coming back, and no dropout with video playing.

### Window state

Leaving fullscreen restores exactly what you came from, maximised or not. Two things
this has to get right, both of which were once wrong:

- `_restoreBounds` comes from `RestoreBounds`, not `Left`/`Top`/`Width`/`Height`. While a
  window is maximised those report the *maximised* rectangle, so restoring them produced
  an oversized, undocked window sitting off the edge of the screen.
- `_restoreState` is captured before anything is touched, and `_suppressStateTracking`
  is held across both transitions. Setting `WindowState = Normal` on the way into
  fullscreen raises `StateChanged`, which would otherwise overwrite the very value
  needed to re-maximise on the way out.

Window size is also clamped to the work area when restoring, so a settings file written
by an older build cannot bring back a window too large to dock.

## Controls

| Key | Action |
| --- | --- |
| `Space` / `K` | Play / pause |
| `←` / `→` | Skip 5s (hold `Shift` for 1s) |
| `J` / `L` | Skip 10s |
| `↑` / `↓` | Volume ±5% |
| `M` | Mute |
| `F` / `Enter` | Fullscreen |
| `Esc` | Leave fullscreen |
| `Ctrl+←` / `Ctrl+→` | Previous / next file in the folder |
| `O` | Open a file (sets the out point while trimming) |
| `T` | Trim a clip out of this video |
| `I` | Set the trim start at the playhead |
| `Home` / `End` | Jump to start / end |
| `0`–`9` | Jump to that tenth of the file |
| `R` | Cycle stop / repeat / play next |
| `Ctrl+↑` / `Ctrl+↓` | Playback speed, 0.5× to 2× |
| `Ctrl+0` | Back to normal speed |
| `.` | Step one frame on (paused) |
| `S` | Copy the current frame to the clipboard |
| `Ctrl+,` | Settings |

The same four skips are buttons either side of play — `10` and `5` back on the left, `5`
and `10` forward on the right — so nothing is keyboard-only. Verified against the seek
bar: every one of the eight moves the playhead by the amount on the label.

Each is a ring with the seconds inside it. The ring is open at the top with the arrowhead
in the gap, rather than pointing inward across the middle: the middle is where the number
has to go, and an arrow reaching into it left no room to set the seconds large enough to
read. The head is filled rather than stroked, because a 1.7px chevron at this size reads
as a stray mark rather than an arrow.

Mouse: click to play/pause, double-click for fullscreen, wheel for volume, and drop a
file anywhere on the window to play it.

The controls and the cursor fade out after 1.4 seconds of stillness and come back the
moment you move. Moving the pointer off the video hides them straight away — measured at
about 40ms — so nothing sits over the picture once you are no longer aiming at anything.
Hovering the control bar keeps them up, and a seek drag that wanders outside the window
does not dismiss them.

The timeline is a 7px bar that grows to 10px under the pointer, but its click target is a
28px band, so it can be grabbed without aiming at the line itself.

Volume, window size and position are remembered in
`%AppData%\Iris\settings.json`. Deleting that file resets everything.

## The rest of the folder

Opening one clip out of a folder of them usually means the neighbours matter too, so the
file name in the middle of the control bar has a chevron either side of it. They step to
the previous and next media file in the same folder, and `Ctrl+←` / `Ctrl+→` do the same
without the mouse. No playlist, no library — just the folder you already opened from.

The listing is built on a worker, so a folder on a slow or network drive cannot hold up
playback starting, and it is cached per folder: stepping between neighbours does not
rescan. Ordering is Explorer's own, through `StrCmpLogicalW`, so `clip2` comes before
`clip10` rather than after it.

Some details that are easy to get wrong:

- **The arrows stop at the ends rather than wrapping**, and grey out when they do, so the
  control says where you are in the folder. The exact position is in the file name's
  tooltip, and each arrow's tooltip names the file it would open.
- **They disappear entirely when the folder holds only one media file**, rather than
  sitting there permanently dead.
- **They go quiet while a trim range is being set**, because changing file discards it and
  a stray click is a poor way to lose a careful selection.
- **The whole row hides below 900px of window**, before the left and right clusters get
  close enough to collide with it.
- **They stay available in fullscreen.** The name appears in the top strip there as well,
  which is mild duplication, but not being able to change file without leaving fullscreen
  is worse.

Paths are canonicalised with `Path.GetFullPath` the moment a file is opened. Paths reach
Iris from the command line, Explorer, drag-and-drop and the file dialog and they do not
all agree on separators — a path carrying a forward slash never matched the backslashed
ones that come back from enumerating its own folder, and the arrows silently never
appeared.

## Cutting a clip out

Something worth keeping happened in a two-minute capture and you want the ten seconds
around it. Press `T`, or the scissors in the control bar.

The timeline turns into a range: two handles, everything outside them dimmed, and the
selection in accent blue. Drag either handle and the picture follows it, so you are
choosing a cut you can actually see. `I` and `O` set the start and end wherever the
playhead is, which is usually faster than dragging. Playback loops inside the selection,
so what you are watching is the clip you are about to save, not whatever follows it.
*Save clip* writes it next to the original as *<name> (clip).mp4*.

The selection starts as **ten seconds centred on wherever you already are**, rather than
the whole file. That is the case this feature exists for: pause on the moment, press `T`,
and the range is roughly right before you touch anything. Videos under 25 seconds start
fully selected instead, since there is little to trim away.

Iris cuts one range and keeps it. Cutting a section out of the *middle* and joining the
two halves is a different job — it needs a concatenation step and a second set of
handles — and it is not built.

### How the cut is made

Trimming uses **`Windows.Media.Editing`**, the same pipeline the built-in Windows player
trims with. It is hardware accelerated, it ships with Windows, and it renders a 10-second
cut from a 90MB capture in about **2 seconds**.

The obvious alternative was libvlc, which is already here and can write a time range with
its stream-output chain. It was measured and rejected, because a stream copy has to begin
at a keyframe. Asking a real capture for ten seconds gave back:

| asked | got |
| --- | --- |
| 0:00 - 0:10 | 9.98s |
| 0:40 - 0:50 | 11.24s |
| 0:20 - 0:30 | **16.23s** |
| 1:00 - 1:30 | 34.73s |

Up to six seconds of unwanted lead-in, varying with where the cut lands. Re-encoding
inside libvlc would fix the accuracy, but the shipped build cannot: VideoLAN strip the
encoders out of their libavcodec, which reports *"cannot find encoder H264"* and
*"Your Libav/FFmpeg installation is crippled"*. Windows' own encoder has neither problem.

`MediaTrimmingPreference.Precise` is what makes it land on the frame rather than the
keyframe; the clip is re-encoded, at the source's own resolution, frame rate and bitrate,
because the encoding profile is read off the source file. That profile is only reused
when the output container matches the input, since handing the renderer an MKV profile
while writing an `.mp4` fails.

Verified end to end through the real UI: selecting 0:58 to 1:08 and saving produced a
file Windows reports as exactly `00:00:10`, whose first frame is the same street corner
as the source at 0:58 — while the source at 0:53 is a different place entirely.

Two things this costs, both worth knowing:

- **`Microsoft.Windows.SDK.NET.dll` is 23.7MB**, which took the published folder from
  102MB to 127MB. It is the WinRT projection, and it is the price of not bundling a
  separate encoder. It loads lazily, so a launch that never trims never touches it.
- **Windows 10 version 2004 (build 19041) or newer** is needed to trim. Nothing else in
  Iris requires it, so `SupportedOSPlatformVersion` stays at 1809 and the trim button
  explains itself on older builds rather than the app refusing to start.

## Layout

The root holds the project file, the manifest, this README and the WPF entry point.
Everything else is grouped by what it is for.

| Folder | Holds |
| --- | --- |
| `Shell/` | The windows: `MainWindow` and its partials, the settings and association dialogs, saved settings |
| `Media/` | `PlaybackEngine` (libvlc), `TrimExport` (rendering a clip), `MediaTypes` |
| `Interop/` | Win32: dark caption, true fullscreen bounds, sleep suppression, file associations and defaults |
| `Startup/` | Single-instance claim and launch tracing |
| `Theme/` | Palette, icon geometry, control templates |
| `Localization/` | `TextKey` (the keys), `Loc` (current language + the `{loc:Str}` binding), one file per language |
| `Assets/`, `Properties/` | Icon; assembly metadata |

`MainWindow` is one class split across files by concern rather than one long one, because
a video player's code-behind grows in five directions at once:

| File | Role |
| --- | --- |
| `MainWindow.xaml` | Video surface and the overlay chrome |
| `MainWindow.xaml.cs` | Construction, wiring, window integration, shutdown |
| `MainWindow.Playback.cs` | Opening a file, transport, seeking, volume, the frame loop |
| `MainWindow.Chrome.cs` | Fullscreen, auto-hide, mouse gestures, the centre OSD |
| `MainWindow.Input.cs` | Keyboard shortcuts and drag-and-drop |
| `MainWindow.Folder.cs` | Listing the folder and stepping between its files |
| `MainWindow.Trim.cs` | Trim mode: the range control, and saving a clip |

**`PlaybackEngine` is deliberately not part of the window.** Starting libvlc, prewarming
its plugins and tearing it down again is the single largest thing Iris does, it dominates
a cold launch, and none of it needs a window. Keeping it in its own class means that cost
is described in one place instead of threaded through the shell, and the window is left
holding only the parts that genuinely touch the UI — attaching the player to the view and
saying so when the engine cannot start.

## Four things worth knowing before you change the UI

**Every icon lives in a 24x24 box.** A `Path` with `Stretch="None"` is sized to its own
geometry bounds, so empty space inside the geometry becomes an offset: the glyph drifts
down and right of the button centre by half that margin. Measured on the transport row,
that left the play/pause glyph sitting 3px below the skip rings. So each icon is wrapped
in a `<Grid Width="24" Height="24">`, which pins the geometry's own coordinates to the
button, and every geometry is drawn on that same 24 grid centred on (12,12). Two paths in
one such box stay registered with each other, which is how the volume icon keeps its
waves attached to the speaker, and how the skip rings carry a filled arrowhead.

`IconButton` also sets `VerticalAlignment="Center"`. A horizontal `StackPanel` tops out
children of differing heights, and the row mixes 38px buttons with the 46px play button.

**The chrome lives in a second window.** `VideoView` puts the video in a child HWND,
and WPF cannot draw over one. LibVLCSharp works around this by hosting whatever you put
in `VideoView`'s content in a separate transparent window pinned over the video. That
window is real, which has one sharp edge:

**Chrome text is read over video, not over the background it was coloured for.** The
greys in the palette were chosen against Iris's near-black window, which made them far
too dark once they were sitting on a bright frame with only a scrim between. `Cancel` was
the worst of it at `#5F6670`: measured against its own background it came out at
**1.8:1**, which is invisible, and it was barely better on black. Two things fix it, and
both are needed:

- `TextSecondaryBrush` and `TextTertiaryBrush` are lifted well clear of where they were.
- Every text style drawn over the picture carries `ChromeTextShadow`, a tight black halo.
  No fixed colour is safe against arbitrary video, so the halo is what actually keeps the
  readout legible over a white sky without blacking out half the frame with scrim.

Same reasoning made `Cancel` an outlined pill rather than a text link: it is one half of a
decision whose other half is a filled button, and a dim link next to a filled pill reads
as decoration. Anything new that sits over the picture wants the same treatment.

**Never give the overlay a fully transparent background.** It is a layered window, and
Windows routes the mouse *through* any alpha-0 pixel of a layered window to whatever is
beneath. WPF's `Transparent` is alpha 0, so using it silently kills hover, click,
double-click and wheel everywhere except directly on top of a button. `OverlayRoot` and
the bars use `#01000000` instead — one step of alpha, invisible, and fully hit-testable.

For the same reason `OverlayRoot` paints an opaque backdrop until the first file loads:
before there is any video, the child HWND has nothing in it and would otherwise show
whatever happened to be behind the window.

## Deliberately not included

A media library, playlists, streaming, codec packs, auto-update, telemetry, and any kind of
account. Each of those is what turns a video player into an application you have to manage.

## Known limitations

- **No reverse frame step.** libvlc has a forward step and no reverse one, and a seek is not a
  substitute: a seek moves the position but does not reliably repaint the video — measured, the
  picture changed on two of eight single-frame seeks while the clock advanced on every one.
  Doing it properly means caching decoded frames in memory, which is a real feature rather than
  a small fix.
- **No subtitle or audio track selection yet.** libvlc has already parsed those tracks; Iris
  does not yet ask for them. This is the biggest gap.
- **Trimming needs Windows 10 version 2004 or newer**, because that is where
  `Windows.Media.Editing` arrived. Everything else runs on older builds.
- **The controls are custom-drawn**, so there is no keyboard focus traversal and screen readers
  see very little of the chrome.

## Startup tracing

`IRIS_TRACE=1` writes a startup phase breakdown to `%TEMP%\iris-trace-<pid>.log`.
