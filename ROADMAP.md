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
- [x] Live window previews on hover (DWM thumbnails) with click-to-switch and close
- [x] Widget framework: tiles plus cards; picker; drag to reorder; right-click to remove
- [x] Clock, Day Number, Day Progress, CPU & Memory, Weather, Now Playing, Control Center,
      Show Desktop, Flexible Space
- [x] Drag the bar to another edge or display; five styles; S/M/L sizes; 12/24-hour clock
- [x] Opt-in taskbar replacement (crash-safe restore), hide in full screen, start with Windows

## Phase 3: More widgets and NexaDock originals ✅

- [x] **Context profiles:** Work, Gaming and Study templates with their own layouts, switched
      manually or automatically by trigger apps and schedules (including overnight windows),
      with a stability delay so Alt+Tab does not cause flicker
- [x] **Pulse:** a capsule plus a glowing tile when something needs you (low battery, almost full
      disk, busy CPU, finished Focus round or timer); dismiss until the condition recurs
- [x] **Productivity:** Focus (Pomodoro), Timer, Stopwatch with laps, To-dos, Notes; cards accept
      keyboard input on demand
- [x] **System:** Volume (wheel and slider), Microphone mute, Network Speed chart, Storage,
      Recycle Bin, Keyboard Layout, Quick Toggles (Wi-Fi, Bluetooth), Brightness (WMI and DDC/CI)
- [x] **Language:** Croatian and English interface, switched live, with a test that keeps both
      complete
- [x] Settings reorganized into tabs, including a profile editor

## Phase 4: Accounts, AI and developer widgets

- Calendar events from Outlook and Google (OAuth, read-only), and a meeting-soon Pulse
- Claude Code and Codex usage and limits, read from local session files (nothing leaves the PC)
- GitHub notifications and contribution streak; status of the current git repository
- Stocks and crypto watchlist
- System tray widget (icons of background apps), if a reliable documented approach exists

## Phase 5: Polish and distribution

- A bar on every display, with per-display layouts
- Auto-hide with reveal at the screen edge
- Overflow menu for widgets that don't fit
- Refractive "liquid" glass through a GPU shader path
- MSIX installer, auto-update, and settings import/export
