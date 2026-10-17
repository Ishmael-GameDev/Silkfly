# Releasing Silkfly

Releases are built by `.github/workflows/release.yml` with [Velopack](https://velopack.io) and published to
GitHub Releases, which is also where installed copies look for updates.

## Making a release

1. Move the `[Unreleased]` notes in `CHANGELOG.md` under a new `## [X.Y.Z]` heading. For the release notes (the GitHub
   release and Velopack's update package) write a short text for users in `.github/release-notes/X.Y.Z.md`; without it
   the `CHANGELOG.md` section is used.
2. Set `<Version>X.Y.Z</Version>` in `Directory.Build.props` so local builds show the same version.
3. Optionally add an announcement to `news.json` (see below).
4. Commit, then tag and push:

   ```bash
   git tag vX.Y.Z
   git push origin main vX.Y.Z
   ```

The workflow takes the version from the tag (`v0.1.0` → `0.1.0`). A tag with a suffix (`v0.2.0-beta.1`) becomes a
GitHub pre-release; only users who chose the **Beta** channel in Settings get it.

## What the workflow does

- `pack` (Windows, Linux, macOS arm64, macOS x64): tests (on Windows; `build.yml` runs them on every OS),
  `dotnet publish` self-contained, then `vpk pack` (full packages only, no deltas).
- `publish`: renames the downloads (`Silkfly-Windows-Setup.exe`, `Silkfly-Windows-Portable.zip`, `Silkfly-Linux.AppImage`,
  `Silkfly-macOS-AppleSilicon.zip`, `Silkfly-macOS-Intel.zip`; the macOS `.pkg` installers are dropped), uploads the
  update feed of all four channels to one GitHub release (`vpk upload github --merge`) and the downloads next to it, then
  appends a short note on SmartScreen and antivirus heuristics to the release notes. With a `VT_API_KEY` repository
  secret (a free VirusTotal API key) it also uploads the Windows downloads to VirusTotal and links the reports.

The download names (`Silkfly-Windows-Setup.exe`, `Silkfly-Windows-Portable.zip`, `Silkfly-Linux.AppImage`,
`Silkfly-macOS-AppleSilicon.zip`, `Silkfly-macOS-Intel.zip`) must not change: the buttons of the website (`site/`) link
to `releases/latest/download/<name>`.

## Thunderstore

Stable tags (no suffix) are also published to Thunderstore as `Ishmael-Silkfly`, after the GitHub release. The package
holds the README from `thunderstore/`, `CHANGELOG.md`, the icon and the Windows installer; `thunderstore/thunderstore.toml`
is the Thunderstore CLI (tcli) config. Silkfly hides its own package from the in-app catalog by the `ThunderstoreNamespace`
setting (`Ishmael` by default), so if the team name ever changes, change it there and in `thunderstore.toml`.

The upload needs a `THUNDERSTORE_TOKEN` repository secret; without it the step is skipped with a warning. To get one:
on thunderstore.io open **Teams → Ishmael → Service Accounts**, add a service account and copy its token (it is shown
once), then add it under **Settings → Secrets and variables → Actions** of the repository.

Thunderstore never accepts the same version twice and a published version can't be replaced. So a stable release can no
longer be deleted and tagged again: the rebuild would fail at the Thunderstore step and the files there would differ
from GitHub. Fix mistakes in a new version instead.

Velopack channels are the runtime identifiers: `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`. Each installed copy
updates from the channel it was packed with.

The package id is `SilkflyApp`, not `Silkfly`: on Windows Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes
that folder on uninstall, while Silkfly keeps settings and profiles in `%LOCALAPPDATA%\Silkfly`. Never change the
package id after the first release, or installed copies stop receiving updates.

## Testing an update locally (Windows)

`vpk` is a .NET tool: `dotnet tool install -g vpk --version 1.2.161` (keep it equal to the Velopack package version).

```bash
dotnet publish src/Silkfly.App -c Release -r win-x64 --self-contained -p:Version=0.1.0 -o out/pub-0.1.0
vpk pack -u SilkflyApp -v 0.1.0 -p out/pub-0.1.0 -e Silkfly.exe -i build/icons/silkfly.ico -o out/releases
# copy out/releases/SilkflyApp-win-Setup.exe somewhere, then build and pack 0.1.1 the same way
```

Install the 0.1.0 setup, then start the installed app with `SILKFLY_UPDATE_SOURCE=<full path to out/releases>`:
it takes updates from that folder instead of GitHub. Settings → Silkfly updates → **Check now** shows the banner;
**Update and restart** installs 0.1.1. `SILKFLY_DATA_DIR` keeps the test away from your real settings and profiles.
Uninstall with `%LOCALAPPDATA%\SilkflyApp\Update.exe uninstall`.

A copy started from the build output (not installed) says that updates work only in the installed version.

## Signing

Nothing is signed yet. Windows SmartScreen and macOS Gatekeeper warn on first start; the README FAQ explains what
to do. Once certificates exist, `vpk pack` can sign: `--signParams` on Windows, the macOS options are listed by
`vpk pack -H` on a Mac.

## news.json

The Info screen shows the first entry of `news.json` on the default branch that has not expired
(fetched at most once an hour):

```json
{ "items": [ { "id": "2026-10-14-release", "text": { "ru": "Silkfly 0.1 вышел!", "en": "Silkfly 0.1 is out!" },
  "link": "https://github.com/Ishmael-GameDev/Silkfly/releases", "until": "2026-11-14" } ] }
```

`until` is the last day the entry is shown; `link` must be https.

## App icon

`build/icons/silkfly.{ico,png,icns}` and `src/Silkfly.App/Assets/silkfly.ico` are made from the first silk fly of the
Silksong credits art on a crimson rounded square.
