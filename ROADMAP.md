# NexaDock roadmap

## Phase 1: Liquid Glass foundation ✅

- [x] Layered solution (Core, Infrastructure, Windows, UI, App) and a test project; .NET 10, x64
- [x] Native DWM acrylic that stays vivid while the window is inactive, with a translucent fallback
- [x] Per-Monitor-V2 placement and a fallback when the chosen monitor is missing
- [x] SQLite settings with corruption recovery
- [x] Tray icon, single instance, clean shutdown

## Phase 2: Smart bar and widgets ✅

- [x] Bar layout: full width or compact, with glass tiles and flexible space
- [x] Apps widget: Start, pinned and running apps merged, running and active indicators,
      click to switch, minimize or cycle windows
- [x] **Live window previews** on hover (DWM thumbnails) with click-to-switch and close
- [x] Widget framework: tiles plus pop-up cards; picker with categories and search; drag to
      reorder; right-click to remove
- [x] Widgets: Clock, Day Number, Day Progress, CPU & Memory, Weather (Open-Meteo), Now Playing
      (Windows media sessions), Control Center, Show Desktop, Flexible Space
- [x] Drag the bar to the top or bottom edge, or to another display
- [x] Styles: Liquid Glass, Onyx, Pearl, Midnight, Aurora; S/M/L sizes; 12/24-hour clock
- [x] Opt-in taskbar replacement (app bar, crash-safe restore); hide in full screen; start with Windows

## Phase 3: More widgets and NexaDock originals

- **Context profiles:** separate widget sets for Work, Gaming and Study that switch by time of day
  or by the app in front (a game makes the bar minimal)
- **Pulse:** the bar raises a tile when something needs you: a meeting in 10 minutes, battery at
  15%, a finished timer, an almost full disk, an AI limit running out
- **Productivity:** Pomodoro, timer, stopwatch, todos, notes
- **Calendar events:** Outlook and Google
- **System:** volume slider, brightness, network speed, storage, Quick Toggles, Recycle Bin,
  keyboard layout
- **Media:** audio output and microphone mute
- **Language:** Croatian and English user interface
- **System tray widget:** icons of background apps

## Phase 4: AI and developer widgets

- Claude Code and Codex usage and limits, read from local session files (nothing leaves the PC)
- GitHub notifications and contribution streak; status of the current git repository
- Stocks and crypto watchlist

## Phase 5: Polish and distribution

- A bar on every display, with per-display layouts
- Auto-hide with reveal at the screen edge
- Refractive "liquid" glass through a GPU shader path
- MSIX installer, auto-update, and settings import/export
