# Thunderstore API: verified behaviour

Checked with curl on 2026-10-14 against the `hollow-knight-silksong` community. Sample responses are in
`tests/Fixtures/thunderstore/` (trimmed listing, README).

## Package listing

| Request | Result |
|---|---|
| `GET https://thunderstore.io/c/hollow-knight-silksong/api/v1/package-listing-index/` | `302` → `https://ccdn.thunderstore.io/live/blob-storage/sha256/<hash>.blob`; body is **gzip** (`application/octet-stream`, no `Content-Encoding`) of a JSON array of chunk URLs (one chunk at the time of writing) |
| `GET <chunk url>` (on `ccdn.thunderstore.io`) | gzip blob, JSON array of v1 packages (~2.1 MB uncompressed, 471 packages) |
| `GET https://thunderstore.io/c/hollow-knight-silksong/api/v1/package/` | `200`, `Content-Encoding: gzip`, the same JSON array |

Both strategies return identical data. Silkfly uses the index first and falls back to `api/v1/package/`.
The client detects the gzip magic number (`1f 8b`) because chunk blobs carry no `Content-Encoding`.

Versions are newest first in practice, but Silkfly sorts them by SemVer.

## Loader packages and dependencies

- `silksong_modding-BepInExPack_Silksong` 1.0.3: the loader Silkfly installs.
- `BepInEx-BepInExPack_Silksong` 5.4.75304: **deprecated** wrapper that depends on the one above.
- Mods depend on either of them, and some on the generic `BepInEx-BepInExPack-5.4.2100`.

Rule used: any dependency whose name starts with `BepInExPack` is the loader and is never installed as a mod.

Mod managers published as packages (`ebkr-r2modman`, `Kesomannen-GaleModManager`) are hidden from the mod list.

Category names may carry a `(Deprecated category) ` prefix; Silkfly strips it.

## README / CHANGELOG

| Request | Result |
|---|---|
| `GET https://thunderstore.io/api/experimental/package/{ns}/{name}/{version}/readme/` | `200` `{"markdown": "..."}` |
| `GET …/{version}/changelog/` | `200` `{"markdown": null}` when the package has no changelog (not `404`) |
| `GET https://thunderstore.io/api/experimental/package/{ns}/{name}/` | `200` package with `latest` |

## Downloads and hosts

`GET https://thunderstore.io/package/download/{ns}/{name}/{version}/` → `302` →
`https://ccdn.thunderstore.io/live/repository/packages/{ns}-{name}-{version}.zip`.

Icons: `https://ccdn.thunderstore.io/live/repository/icons/{ns}-{name}-{version}.png`.

Allowed hosts (also after redirects): `thunderstore.io` and `*.thunderstore.io`, HTTPS only.
