# Changelog

All notable changes to Silkfly. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [SemVer](https://semver.org/). The release workflow uses the section of a version as its release notes.

## [Unreleased]

## [0.1.0]

First public release.

### Added

- Thunderstore catalog for Hollow Knight: Silksong with an offline cache, search, filters (installed, enabled,
  out of date, what's new) and advanced search (categories, author, sorting, deprecated and NSFW mods).
- Install, update, enable/disable and remove mods together with their dependencies; choose a specific version;
  README, changelog and "required by" in the mod card; manual install of .zip/.dll by drag and drop;
  update, enable/disable or remove all mods of a profile at once.
- BepInEx loader installed into the game folder once; mods live in profiles and are switched without reinstalling.
- Profiles: create, rename, duplicate, delete, icons, open the folder, export/import `.r2z` files and Thunderstore
  profile codes; the profiles folder can be moved.
- Manually added mods can be linked to their Thunderstore package (installed properly, then the manual copy goes);
  "open configs" on installed mods.
- Play with or without mods; launch directly, through Steam, or with Proton launch options on Linux.
- Info screen with news, quick links and the game's saves and mods folders.
- Russian and English interface, interface scale 90–130%, embedded Noto Sans font, keyboard navigation
  (Up/Down between mod cards, Enter/Space to expand).
- Unexpected errors and repaired profile files are reported instead of passing silently; optional verbose log.
- Automatic updates of Silkfly itself from GitHub Releases (stable and beta channels).
