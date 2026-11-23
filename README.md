# NexaDock

A smart Liquid Glass bar for Windows 11, built with .NET 10 and WPF: Start, your pinned and running
apps, and live widgets in one glass strip, optionally replacing the Windows taskbar.

![NexaDock bar](docs/images/bar-liquid-glass.jpg)

![NexaDock bar in Croatian with Phase 3 widgets](docs/images/bar-phase3-hr.jpg)

> **Status:** Phase 3.5 (Claude usage widget, overflow menu, installer), on top of Phase 3
> (productivity and system widgets, profiles, Pulse, Croatian and English).
> See [ROADMAP.md](ROADMAP.md) for what comes next and [ARCHITECTURE.md](ARCHITECTURE.md) for the
> design.

## What it does

- **Apps widget:** a Start button, pinned apps and every open window, merged like a taskbar.
  - A pinned app lights up while it runs; other running apps appear after the pinned ones.
  - Click to launch, switch to, minimize or cycle through an app's windows.
  - **Hover a running app for live window previews** (DWM thumbnails); click a preview to switch
    to that window, or press × to close it.
  - Right-click an app to pin or unpin it, open a new window, or close its windows.
- **Live widgets.** Each widget is a glass tile on the bar; clicking a tile opens a card with
  details.

  | Widget | Tile | Card |
  | --- | --- | --- |
  | Clock | Time and date (12/24 h) | Seconds, week number, month calendar, Date & time settings |
  | Day Number | Calendar page | Month calendar with navigation |
  | Day Progress | Ring with today's progress | Day, week, month and year progress |
  | CPU & Memory | Two rings | Busiest apps by CPU and memory, uptime, Task Manager |
  | Weather | Icon and temperature | Conditions, feels-like, humidity, wind, 4-day forecast |
  | Now Playing | Cover art, title, play/pause | Large art, progress, previous/play/next |
  | Control Center | Network, sound, battery | Opens Quick Settings; the bell opens notifications |
  | Show Desktop | — | Hides or restores all windows |
  | Flexible Space | — | Pushes the following widgets to the far end of the bar |
  | **Pulse** | Appears only when something needs you | All alerts, with Dismiss |
  | **Focus** | Pomodoro ring, time left, start/pause | Big ring, skip, reset, focus and break length |
  | **Timer** | Countdown | Presets (1–60 min), +1/+5 min, start/pause, reset |
  | **Stopwatch** | Elapsed time | Hundredths of a second, laps |
  | **To-dos** | Next open task, progress ring | Add (type + Enter), tick off, delete, clear completed |
  | **Notes** | First line of your note | Multi-line note, saved as you type |
  | **Volume** | Level; scroll the mouse wheel to change it | Slider, mute, microphone mute, device name |
  | **Microphone** | Mic state; click to mute or unmute | — |
  | **Network Speed** | Download and upload rates | 60-second live chart |
  | **Storage** | Free space on the system drive | All drives with usage bars, Open This PC |
  | **Recycle Bin** | Item count | Open, Empty (Windows asks for confirmation) |
  | **Keyboard Layout** | Current layout (HR, EN…); click to switch | — |
  | **Quick Toggles** | Wi-Fi and Bluetooth on/off, Quick Settings | — |
  | **Brightness** | Level; scroll the mouse wheel to change it | Slider per display (laptop panel or DDC/CI monitor) |
  | **Claude** | Tokens in the current 5-hour session and time until it resets | Session details, today, 7-day chart, models |

- **Profiles:** separate widget layouts such as Work, Gaming and Study (built-in templates).
  - Switch from the bar's right-click menu › **Profile**.
  - Or switch automatically: an app in front (e.g. `steam.exe`) wins, then a schedule (time window
    and weekdays, overnight windows supported), otherwise the profile you picked last.
  - A switch waits until the result has been stable for 3 seconds, so a quick Alt+Tab past a
    trigger app does not reshuffle the bar.
- **Pulse:** the bar asks for attention only when needed.
  - Triggers: battery at or below 15% (critical at 7%), the system drive almost full, CPU busy for
    half a minute, or a finished Focus round or timer (with a sound and a notification).
  - The owning tile glows (steady with Reduced motion), and the Pulse capsule shows the alert.
  - A dismissed alert stays quiet until its condition clears and comes back.
- **Claude usage** is read from Claude Code's own logs on this PC (`~/.claude/projects`), with no
  sign-in and nothing sent anywhere.
  - Shows the current 5-hour session window (when it started and when it resets), today, the last
    seven days and the models used.
  - Anthropic does not publish exact plan limits, so the bar compares the session with your
    busiest session of the last 30 days. Pulse tells you when you pass 90% of that.
- **Overflow:** widgets that don't fit on the bar move behind a "›" button at its end. They stay
  live in that card.
- **Language:** English or Croatian (Hrvatski), or follow Windows. Switches live everywhere: the
  bar, cards, menus, Settings, the picker and the tray.

- **Widget picker:** right-click the bar › **Widgets…** to browse by category or search, and add or
  remove widgets. Changes apply instantly.
- **Arrange:**
  - Drag a tile along the bar to reorder it.
  - Right-click a tile › **Remove**.
  - Drag the bar by an empty spot to the top or bottom edge, or onto another display.
- **Styles:** Liquid Glass, Onyx, Pearl, Midnight and Aurora, on a native acrylic blur.
- **Sizes and layout:** S, M or L sizes. Full width (like a taskbar) or compact (fits the widgets).
  Adjustable spacing, opacity and edge gap, plus reduced motion.
- **Behavior:**
  - **Replace the Windows taskbar** (opt-in).
  - Hide during full-screen apps.
  - Start with Windows.

## Requirements

- Windows 11 build 22621 (22H2) or newer for the native acrylic backdrop. Older builds get a
  translucent fallback.
- .NET SDK 10.0 (verified with 10.0.404) on x64.

## Build, test, run

From the repository root:

```powershell
dotnet build NexaDock.slnx
```

```powershell
dotnet test tests/NexaDock.Tests/NexaDock.Tests.csproj
```

```powershell
dotnet run --project src/NexaDock.App
```

Or start the built executable:

```powershell
.\src\NexaDock.App\bin\Debug\net10.0-windows10.0.19041.0\NexaDock.exe
```

Release build (framework-dependent, needs the .NET 10 Desktop Runtime):

```powershell
dotnet publish src/NexaDock.App -c Release -r win-x64 --self-contained false -o publish
```

### Install for everyday use

`tools\install.ps1` installs NexaDock for the current user:
- builds a Release version into `%LOCALAPPDATA%\Programs\NexaDock`
- closes a running copy cleanly
- adds a Start menu shortcut
- starts it

```powershell
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

`tools\uninstall.ps1` removes the program, the shortcut and the startup entry. Settings, to-dos and
notes are kept unless you add `-RemoveData`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1
```

### Options

| Option | Effect |
| --- | --- |
| `NexaDock.exe --exit` | Closes a running NexaDock cleanly (restores the taskbar if it was replaced). |
| `NexaDock.exe --settings` | Opens Settings. Also works while NexaDock is running: the running instance opens it. |
| `NEXADOCK_BACKDROP=fallback` | Forces the translucent (no-blur) surface, for testing. |
| `NEXADOCK_DATA_DIR=<path>` | Stores the database and logs in `<path>` instead of `%LOCALAPPDATA%\NexaDock`. |
| `NEXADOCK_LOG=debug` | Writes verbose diagnostics to the log. |

Starting NexaDock again while it runs only brings its bar back.

## Replacing the Windows taskbar

Settings › Behavior › **Replace the Windows taskbar**. While it is on:

- The Windows taskbar is set to auto-hide and its windows are hidden.
- NexaDock registers as an *app bar*, so maximized windows stop above it.
- The Windows key still opens Start.

When you turn it off or quit NexaDock, the taskbar's previous state is restored exactly. That state
is saved before anything changes, so the taskbar is also restored after a crash: on the next start,
or from the crash handler. NexaDock never modifies Windows files.

## Privacy

- **Weather:** only the city name you enter, and its coordinates, are sent to
  [Open-Meteo](https://open-meteo.com) (free, no account).
- **Everything else is read locally:** the window list, media sessions, CPU, memory, battery,
  audio devices, network counters, drives and brightness.
- **To-dos and notes** are stored only in the local database.
- No telemetry and no account. Settings live in
  `%LOCALAPPDATA%\NexaDock\nexadock.db` (SQLite) and logs in `%LOCALAPPDATA%\NexaDock\logs\`.

## Known limitations (Phase 3)

- **Claude usage:** only Claude Code on this PC is counted. claude.ai in the browser and other
  devices are not included, because their usage is not stored locally.
- **Calendar events (Outlook/Google) are not shown yet.** They need an account sign-in (OAuth) and
  are planned for Phase 4.
- **Audio output device:** the Volume widget changes the default device but cannot switch to
  another device. Windows has no documented API for that.
- **Brightness:**
  - External monitors need DDC/CI switched on in their own on-screen menu.
  - Some monitors answer slowly.
- **Untested on this machine:**
  - Brightness changes: the widget lists displays, but I did not move the sliders on your monitor.
  - Toggling the Wi-Fi/Bluetooth radios.
  - Emptying the Recycle Bin: it deletes data, so I left it for you.
  - Pulse alerts for battery and disk: there is no battery here and the disk is not full. The rules
    are unit-tested.

- **Native corners:** with the native blur, the bar uses the DWM corner radius (~8 px). The fully
  rounded pill shape exists only in the translucent fallback.
- **Notification-area icons:** the classic system tray (icons of background apps) is not shown on
  the bar yet. With the taskbar replaced, reach them through the Control Center or Quick Settings.
- **One bar:** a bar appears on one display only. Replacing the taskbar hides it on every display.
- **Untested on hardware:**
  - mixed-DPI and high-DPI displays (both test displays were at 100%)
  - unplugging a monitor while the bar runs
  - touch and pen input
  - battery display (no battery on the test machine)
  - the **Replace the Windows taskbar** switch itself: it changes a Windows setting, so it was left
    for you to try
- **Store apps:** they are pinned through their Start menu shortcut, or by right-clicking a running
  app › **Pin to bar**.
