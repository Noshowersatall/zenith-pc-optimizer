# Zenith PC Optimizer

A sleek, dark Windows 11 optimizer built with C# and WPF (.NET 8). It cleans junk, manages startup apps, applies real performance and privacy tweaks, boosts your games, and shows what's actually using your PC, all from one app.

> **Heads up:** Zenith changes Windows system settings and needs administrator rights. Every toggle can be switched back to the Windows default, but use it at your own risk.

## Features

| Page | Features |
|---|---|
| **Dashboard** | Live CPU (with graph), RAM, disk and uptime · PC health score · system info · one-click **Optimize now** |
| **Cleaner** | Temp files, Windows Update cache, Delivery Optimization cache, thumbnail cache, error reports & crash dumps, logs, browser caches (Chrome / Edge / Brave / Opera / Vivaldi / Firefox), GPU shader caches, Recycle Bin — sizes shown before cleaning |
| **Startup Apps** | Every startup app with its icon and publisher; enable/disable exactly like Task Manager does |
| **Performance** | Ultimate Performance plan, max CPU (no core parking), power throttling off, USB/PCIe power saving off, hibernation off, startup delay removal, NTFS last-access off, Edge background off, Game Mode, GPU scheduling, Game DVR off, gaming priority (MMCSS), mouse acceleration off, foreground boost, animations/transparency/visual effects, plus advanced: Memory Integrity, SysMain, Search indexing |
| **Privacy** | Telemetry, tailored experiences, activity history, error reporting, advertising ID, tips & Start ads, Bing in Start search, Copilot, Recall, Widgets, P2P update sharing |
| **Debloat** | Detects installed bloatware (News, Solitaire, Clipchamp, Copilot, Teams, TikTok, Candy Crush, OneDrive…) and removes it for all users |
| **Game Booster** | Finds your Epic & Steam games (or add any .exe). Each boosted game always launches with High CPU priority and your high-performance GPU. While Zenith runs (even in the tray) it frees RAM when a game starts, keeps RAM available during play, and lowers background apps like OneDrive and Teams until you quit. Optional start with Windows |
| **Uninstaller** | Every installed desktop program with icon, size and install date; search, sort and run its real uninstaller |
| **Disk Space** | Scans a drive and shows which folders and files take up space, with drill-down and the 100 largest files |
| **Processes** | Live CPU/RAM per app (grouped like Task Manager), search, sort, end task |
| **Benchmark** | CPU single/multi-core, memory speed, disk read/write/4K and boot time. First run is the baseline so you can see real before/after changes |
| **Network** | Switch DNS to Cloudflare, Google, Quad9 or back to automatic, with a latency test to find the fastest for you |
| **Tools** | Free RAM (standby list purge), flush DNS, DISM + SFC repair, TRIM/defrag, WinSxS cleanup, network reset, restart Explorer, restore point, activity log |

Every toggle reads the real current state from Windows, so it's always accurate, and switching a toggle off puts that setting back to the Windows default. Every change is logged to `%LocalAppData%\Zenith\zenith.log`.

## Build it

**Requirements:** Windows 10 or 11 (x64). The .NET 8 SDK is installed automatically if you don't have it.

1. Download or clone this repository.
2. Double-click **`build.bat`**.
   - If the .NET 8 SDK is missing, it installs it with `winget` (you'll get one admin prompt).
   - The first build downloads NuGet packages and takes 1–2 minutes.
3. Run **`dist\ZenithOptimizer.exe`**. It's a single self-contained file: no install and no .NET runtime needed on the PC that runs it.

Or build it manually:

```
dotnet publish ZenithOptimizer\ZenithOptimizer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

To work on the code in Visual Studio 2022, open `ZenithOptimizer\ZenithOptimizer.csproj`.

## What it deliberately doesn't do

Some "optimizer" tricks are placebo or harmful, so they're left out: disabling Windows Defender or Windows Update, deleting Prefetch, registry "cleaning", and network tweaks like disabling Nagle's algorithm. They either don't help on Windows 11 or they make the PC less secure or stable.

## Project layout

```
ZenithOptimizer/
  Core/     engine: tweaks, cleaner, startup manager, debloater, game booster,
            benchmark, processes, DNS, uninstaller, disk analyzer, power plans
  Views/    pages (XAML + code-behind)
  Themes/   colors, buttons, toggle switches, scrollbars
  UI/       converters, icon loading, tray icon
build.bat   one-click build
```

## License

[MIT](LICENSE)
