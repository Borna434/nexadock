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

## Phase 3.5: Ready for everyday use ✅

- [x] **Claude widget:** Claude Code usage from local logs (5-hour window, today, 7 days, models)
- [x] Overflow: tiles that don't fit move behind a "›" button and stay usable in its card
- [x] `tools/install.ps1` and `tools/uninstall.ps1`; `NexaDock.exe --exit`
- [x] Full-screen detection uses Windows' own notification state

## Phase 4: A real taskbar replacement ✅

- [x] **Centred like Windows 11:** the apps stay in the middle; widgets before them start at the
      left edge, widgets after them end at the right edge; overflow hides outer tiles first
- [x] **Automatic arrangement** in a logical order (right-click › Arrange widgets); new widgets
      land in their place; Day Number is dropped when the Clock already shows the date
- [x] **Edge to edge:** flush with the screen edge by default (square corners, single rim line);
      maximised windows stop exactly at the glass
- [x] **Notification icons** of background apps (via ManagedShell): pinned icons on the bar, the
      rest in a card; clicks and right-clicks go to the apps
- [x] **A bar on every display**, each with its own layout; its apps show that display's windows
- [x] **Calendars:** Google account sign-in (OAuth with PKCE, read-only), every calendar in its
      own colour with a tick to show it; iCal links (several at once); agenda in the Clock card;
      Up Next widget; Pulse before meetings and when the Google sign-in expires
- [x] **Liquid glass:** specular hairline, lens depth, edge light, pointer-following light, and
      opt-in refraction of what lies just beyond the edge (redrawn only when it changes)
- [x] **Backup:** export and import of layout, profiles, pinned apps, look and widget content
- [x] **Updates** from GitHub releases with a SHA-256 check (`tools/package.ps1` builds the package)
- [x] Sharp app icons at their exact pixel size, with correct alpha (no white halo)

## Phase 4.5: Everyday tools ✅

- [x] **Search** (Alt+Space): apps, files, calculator, unit conversion and the web
- [x] **Clipboard history** (widget and Win+Alt+V) that skips content marked private
- [x] **Now Playing:** seek, the playing app's own volume, album-art colours
- [x] **Accent from the wallpaper**
- [x] **System icons** with their own Windows flyouts; wheel for volume
- [x] Automatic releases from version tags; in-app updates verified with SHA-256

## Phase 5: Polish and distribution

- [x] **System panels:** network (connections, Wi-Fi list, join with a password), sound (output
      device, app mixer) and battery or power (power mode), each opening above its icon
- [x] **Game mode:** widgets stop sampling while a full-screen game hides the bar
- [x] Claude widget: the session resets exactly five hours after the first message
- Per-display edge and size for extra bars
- MSIX package and signed releases
- More calendar sources (Outlook via Microsoft account)
