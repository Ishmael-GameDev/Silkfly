# Package layout rules

How Silkfly places the files of a Thunderstore package into a profile. Implemented in
`src/Silkfly.Core/Install/PackageLayout.cs`; `<FN>` is `Namespace-Name`.

| In the archive | In the profile |
|---|---|
| `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, `LICENSE*` at the root | `BepInEx/plugins/<FN>/` |
| `BepInEx/…` | prefix dropped, then the rules below |
| `plugins/**` | `BepInEx/plugins/<FN>/**` |
| `patchers/**` | `BepInEx/patchers/<FN>/**` |
| `monomod/**` | `BepInEx/monomod/<FN>/**` |
| `config/**` | `BepInEx/config/**`, never overwriting an existing file |
| `core/**` | `BepInEx/core/**` (see below) |
| `*.mm.dll` at the root | `BepInEx/monomod/<FN>/` |
| any other root file or folder | `BepInEx/plugins/<FN>/…` keeping the structure |
| everything inside a single wrapper folder that contains `manifest.json` | wrapper removed first |
| folders, `__MACOSX/`, `.DS_Store` | skipped |

## Checked on popular packages (2026-10-14)

| Package | Archive layout | Result |
|---|---|---|
| Hamunii-DetourContext_Dispose_Fix 1.0.9 | `patchers/*.dll` | patchers/<FN> |
| Hamunii-AutoHookGenPatcher 1.1.1 | `BepInEx/patchers/*.dll`, `BepInEx/core/MonoMod*.dll` | patchers/<FN>, core |
| MonoDetour-MonoDetour 0.7.16 | `core/*.dll` | core |
| MonoDetour-MonoDetour_BepInEx_5 0.7.16 | `core/*.dll`, `patchers/*.dll` | core, patchers/<FN> |
| cometcake575-Architect 3.41.0 | `Architect/*.dll` | plugins/<FN>/Architect |
| silksong_modding-ModMenu 0.8.0, I18N, UnityHelper, FsmUtil; SFGrenade-WavLib | dll/pdb/xml at the root | plugins/<FN> |
| XiaohaiMod-ShowDamage_HealthBar 2.1.1 | dll + `Texture/*.png` at the root | plugins/<FN>, plugins/<FN>/Texture |
| silksong_modding-SilksongPrepatcher 1.4.0 | `patchers/*`, `plugins/*` | patchers/<FN>, plugins/<FN> |

### Libraries in `core/`

Ordinary mods don't ship `core/`, but two of the most downloaded libraries (MonoDetour, which ModMenu
needs through MonoDetour_BepInEx_5, and AutoHookGenPatcher) ship their assemblies in `core/`, so skipping it would break
them. Silkfly installs these files into the profile's `BepInEx/core/` and refuses to overwrite any file there that the
mod does not own (the loader's own files, other mods' files): such a package fails with an install conflict instead.
