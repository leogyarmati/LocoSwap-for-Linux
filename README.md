# LocoSwap for Linux

A native Linux port of [LocoSwap](https://github.com/flicard/LocoSwap) (swap rolling stock in
Train Simulator Classic scenarios). The WPF/.NET Framework app was ported to **.NET 8 + Avalonia 11**.

> **Status:** the non-UI core (scenario/route/vehicle logic, settings, serz handling) is compiled and
> tested headlessly (`LocoSwap.SmokeTest`, 31 checks). The Avalonia UI layer was written without being
> able to build it, so expect to fix a few compile errors on first build. See "First build" below.

## How it works on Linux

Train Simulator Classic has no native Linux build, so it runs under Proton/Wine. LocoSwap needs DTG's
`serz.exe` (shipped in the game folder) to convert `.bin` <-> `.xml`. This port runs `serz.exe` through
Wine/Proton, converting paths to `Z:\...` form automatically. Everything else is native.

Linux-specific changes:
* Case-insensitive file lookup (TS data uses Windows casing; Linux file systems are case-sensitive).
* Windows `\` blueprint paths are kept as data but converted for disk access.
* Settings in `~/.config/LocoSwap/settings.json`, log in `~/.config/LocoSwap/debug.log`,
  temp files in `~/.cache/LocoSwap/temp`.
* Steam library auto-detection for the TS folder on first start.
* DotNetZip -> `System.IO.Compression`; Serilog -> a small built-in logger; WindowsAPICodePack -> Avalonia folder picker.
* serz runs with at most 4 concurrent processes and stdout/stderr are drained (avoids a pipe deadlock under Wine noise).

## Requirements

* .NET 8 SDK (`dotnet-sdk-8.0`), with NuGet access for Avalonia
* Wine or Proton, and Train Simulator Classic installed (Steam: appid 24010)
* `libicu` (present on most desktop distros)

## First build

```
dotnet build LocoSwap.sln -c Release
dotnet run --project LocoSwap -c Release
```
Avalonia is referenced as `11.*`; pin an exact version in `LocoSwap/LocoSwap.csproj` once it works for you.
To publish a self-contained binary: `dotnet publish LocoSwap -c Release -r linux-x64 --self-contained`.

Quick core check without Avalonia/NuGet: `dotnet run --project LocoSwap.SmokeTest`.

## Configuring Wine / Proton (Settings window)

serz.exe only has to convert files, so plain Wine is usually enough:

* **Wine command:** empty (= `wine`)

If it fails to start (missing runtime DLLs), run it inside the game's own Proton prefix. Typical Steam values:

* **Wine / Proton command:** `/path/to/steamapps/common/Proton - Experimental/proton run`
* **Environment variables:**
  ```
  STEAM_COMPAT_DATA_PATH=/path/to/steamapps/compatdata/24010
  STEAM_COMPAT_CLIENT_INSTALL_PATH=/home/you/.local/share/Steam
  ```
  or for system Wine with the game's prefix: `WINEPREFIX=/path/to/steamapps/compatdata/24010/pfx`
  (using a newer system Wine on a Proton prefix can modify it - back it up first).

Test by hand first: `wine "<TS folder>/serz.exe" "Z:\\tmp\\Scenario.bin"` should create `Scenario.xml`.
If saving works but the game rejects the scenario, check `debug.log` for the serz output.

## Differences from the Windows version

* The scenario list always has the status-dot column (it stays empty unless "check all scenario consists" is on).
* Message boxes are custom dialogs; the replacement-rules list toggles entries on click instead of using check boxes.
* New settings: Wine/Proton command and environment.
* Newly found vehicles are de-duplicated when you scan several times.
* `LocoSwap_manual.docx` is copied next to the binary; the help button opens it with `xdg-open`.
