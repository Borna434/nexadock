# NexaDock

A Liquid Glass–style application dock for Windows 11, built with .NET 10 and WPF.

NexaDock sits at the bottom (or top) of any display as an always-on-top, non-activating glass bar.
It shows real application icons, a live clock and date, and Settings and Exit controls. It never
takes keyboard focus and leaves the Windows taskbar untouched.

> **Status:** Phase 1, the working dock. See [ROADMAP.md](ROADMAP.md) for later phases and
> [ARCHITECTURE.md](ARCHITECTURE.md) for the design.

## Requirements

- Windows 11 build 22621 (22H2) or newer for the native acrylic backdrop. Older builds get the
  translucent fallback automatically.
- .NET SDK 10.0 (verified with 10.0.404) on x64.

## Build, test, run

From the repository root (`E:\00_Codex\Projects\Lumabar`):

```powershell
dotnet build NexaDock.slnx
```

```powershell
dotnet test tests/NexaDock.Tests/NexaDock.Tests.csproj
```

```powershell
dotnet run --project src/NexaDock.App
```

Or start the built executable directly:

```powershell
.\src\NexaDock.App\bin\Debug\net10.0-windows\NexaDock.exe
```

### Release build (framework-dependent, needs the .NET 10 Desktop Runtime)

```powershell
dotnet publish src/NexaDock.App -c Release -r win-x64 --self-contained false -o publish
```

### Options

| Option | Effect |
| --- | --- |
| `NexaDock.exe --settings` | Opens Settings at startup. |
| `NEXADOCK_BACKDROP=fallback` | Forces the translucent (no-blur) fallback surface, for testing. |
| `NEXADOCK_DATA_DIR=<path>` | Stores the database and logs in `<path>` instead of `%LOCALAPPDATA%\NexaDock`. |

Starting a second instance does not open a second dock. It tells the running instance to show its
dock.

## Using the dock

- **Click an icon** to launch the app. The app comes to the front; the dock never takes focus.
- **Hover** over an icon to lift and scale it and show its name.
- **⚙ / ⏻** open Settings / exit NexaDock.
- **Right-click the dock** for Settings…, Hide dock and Exit NexaDock.
- **Tray icon:** left-click shows the dock, double-click opens Settings, and right-click opens a
  menu with Show Dock, Settings… and Exit.

### Settings (changes apply immediately and save automatically)

- **Theme:** Frost, Midnight, Aurora, Ember, Sage
- **Size:** Small, Medium, Large
- **Width, icon spacing, glass opacity, reduce motion**
- **Screen edge:** bottom or top; **Display:** primary or a specific monitor; **distance from edge**
- **Shortcuts:** add (`.exe`, `.lnk`, `.url`, scripts, documents), remove, reorder, restore defaults
- **Reset appearance** and **Open data folder**

On first run, NexaDock seeds shortcuts **only for apps that exist on the machine**. Candidates
include File Explorer, Edge, Chrome, Firefox, Terminal, Notepad, Calculator, Paint, Windows
Settings and Task Manager. If Windows animations are turned off, Reduce motion is enabled on first
run.

## Data

- Settings: `%LOCALAPPDATA%\NexaDock\nexadock.db` (SQLite, one JSON document in a key/value table)
- Log: `%LOCALAPPDATA%\NexaDock\logs\nexadock.log` (rolls over at 1 MB)

If the database is unreadable, NexaDock moves it aside as `nexadock.db.corrupt-<timestamp>`, starts
with defaults and shows a tray notification. Invalid individual values (such as an unknown theme or
an out-of-range width) are corrected one field at a time, and the rest of the configuration is kept.

## Known limitations (Phase 1)

- **Corner radius in native mode is fixed at the DWM value (~8 px).** Windows clips system-backdrop
  windows to its own rounded corners. The fully rounded pill shape is only available in the
  translucent fallback, which has no blur.
- **Keeping the acrylic active** relies on making DWM draw the frame as active (`WM_NCACTIVATE`),
  because the dock is never activated. This is documented window-message behavior, but it is not a
  dedicated backdrop API.
- If **Transparency effects** are turned off in Windows settings, DWM draws the backdrop as solid.
  This follows the system setting.
- Packaged Store apps can be added through their Start menu shortcut. Browsing the Apps folder
  directly is not supported yet.
- No auto-hide, running-app indicators, drag-and-drop reordering on the dock, or start-with-Windows
  option yet (see the roadmap).
- Tested on one machine: Windows 11 build 26300, a 2560×1440 primary display and a 1920×1080
  secondary display to its left, **both at 100% scaling**. Mixed-DPI and high-DPI setups, monitor
  hot-unplug while running, vertical taskbars and touch/pen input have **not** been tested on
  hardware (the placement math for them is unit-tested).
