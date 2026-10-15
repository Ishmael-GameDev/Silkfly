# BepInExPack_Silksong layout

`silksong_modding-BepInExPack_Silksong` 1.0.3 (BepInEx 5.4.23.4, UnityDoorstop 4.4.1), checked 2026-10-14.

```
icon.png  README.md  manifest.json               Thunderstore metadata
BepInExPack/
├─ winhttp.dll                                  Doorstop proxy (Windows)
├─ doorstop_config.ini                          Doorstop config (Windows)
├─ .doorstop_version                            "4.4.1"
├─ libdoorstop.so                               Doorstop (Linux)
├─ libdoorstop.dylib                            Doorstop (macOS)
├─ run_bepinex.sh                               launcher script (Linux/macOS)
├─ steam_appid.txt                              "1030300"
├─ changelog.txt
└─ BepInEx/
   ├─ config/BepInEx.cfg                        preconfigured
   ├─ core/                                     BepInEx.Preloader.dll, BepInEx.dll, 0Harmony*.dll, Mono.Cecil*.dll,
   │                                            MonoMod.*.dll, HarmonyXInterop.dll (+ .xml docs)
   └─ plugins/                                  empty
```

## doorstop_config.ini (Doorstop 4.4.1)

`[General]`: `enabled`, `target_assembly` (default `BepInEx\core\BepInEx.Preloader.dll`), `redirect_output_log`,
`boot_config_override`, `ignore_disable_switch`. `[UnityMono]`: `dll_search_path_override`, `debug_enabled`,
`debug_address`, `debug_suspend`.

## run_bepinex.sh

`executable_name` defaults to `Hollow Knight Silksong` on Linux and `Hollow Knight Silksong.app` on macOS;
`enabled="1"` and `target_assembly="BepInEx/core/BepInEx.Preloader.dll"` can be overridden by `--doorstop-*` arguments.

## What Silkfly places where

| Build | Into the game folder | Into every profile |
|---|---|---|
| Windows | `winhttp.dll`, `doorstop_config.ini` (with `enabled = false`), `.doorstop_version`, `steam_appid.txt` | `BepInEx/core/**`, `BepInEx/config/BepInEx.cfg` (only if missing) |
| Linux / macOS | `run_bepinex.sh` (+x), `libdoorstop.so` / `libdoorstop.dylib`, `.doorstop_version`, `steam_appid.txt` | same |

The pack's `BepInEx/` folder is never copied into the game.

Existing files are moved (not copied) to `<game>/.silkfly-backup/` before being replaced, so links created by other
mod managers stay intact and can be restored. A file with identical content is left alone and not claimed.

## Game folder (Windows, checked on a GOG install)

`Hollow Knight Silksong.exe`, `Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll`, `UnityPlayer.dll`,
`MonoBleedingEdge/`. GOG registry: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\*` → `gameName` "Hollow Knight: Silksong",
`path`.

## Launch arguments (checked on Windows)

Checked 2026-10-14 on the GOG build 1.0.29315 with BepInExPack_Silksong 1.0.3 (BepInEx 5.4.23.4, Doorstop 4.4.1):

- With `enabled = false` in `doorstop_config.ini`, starting the game with
  `--doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Preloader.dll` loads BepInEx:
  the command-line arguments take precedence over the ini file.
- BepInEx takes its root from the preloader's location, so plugins, patchers and config are read from the profile
  folder and `LogOutput.log` is written there; the game folder needs only the Doorstop files.
- Silkfly keeps `enabled = false` in the ini, so a game started outside Silkfly runs without mods.

Not checked yet: `run_bepinex.sh` on Linux and macOS, launching through Steam, and Proton.

