# Changelog

All notable changes to Silkfly. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [SemVer](https://semver.org/). The release workflow uses the section of a version as its release notes.

## [Unreleased]

## [0.1.1]

### Added

- Silkfly is now also published on Thunderstore.

### Changed

- Silkfly's own Thunderstore package is hidden from the mod list.

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
  profile codes (the profile icon goes along); the profiles folder can be moved.
- Manually added mods can be linked to their Thunderstore package (installed properly, then the manual copy goes);
  "open configs" on installed mods.
- Play with or without mods; launch directly, through Steam, or with Proton launch options on Linux. On Apple Silicon
  Macs the game runs under Rosetta with mods, since the loader is Intel-only.
- Info screen with news, quick links and the game's saves and mods folders.
- Each tab keeps its scroll position; expanded mod cards close when you switch tabs, filters or search.
- Russian and English interface, interface scale 90–130%, embedded Noto Sans font, keyboard navigation
  (Up/Down between mod cards, Enter/Space to expand).
- Unexpected errors and repaired profile files are reported instead of passing silently; optional verbose log.
- Automatic updates of Silkfly itself from GitHub Releases (stable and beta channels).

### Changes since 0.1.0-beta.2

- Expanded mod cards close when you switch tabs, filters or search, and when you jump to a dependency (only that
  mod stays open).
- Mods, Profiles and Settings keep their scroll position when you switch tabs and come back.
- Exported profiles (file and share code) include the profile icon, and importing restores it.
- Mod and profile icons are scaled smoothly instead of looking jagged.
- Windows: all Silkfly windows have slightly rounded corners.
- Scroll bars have no arrow buttons, their track has rounded ends and sits 2 px off the window edge; scrolling no
  longer leaves stray dots above the thumb.
- macOS on Apple Silicon: mods start. The loader's Doorstop library is Intel-only, so in the Direct launch mode Silkfly
  runs the game's Intel version under Rosetta with the loader injected, or shows a clear error if there is none.
- Reopening Silkfly while the game is running shows it as running (with the profile it was started with) instead of
  offering to play again; the running indicator on the Play button no longer freezes.
- Author filter: "Reset filters" always clears it, also on the first click while the list is open; picking an author
  closes the list and moves focus off the box; the list opens again on a click into the box (the first click selects
  the whole name) and offers every author; the author search ignores case.
- The advanced search panel no longer jumps when a category is toggled.
- The README window has the same thin outline as the main window on Windows.

## [0.1.0-beta.2]

### Changed

- Dependencies that are already installed are shown in a calm color in the mod card; missing ones stay highlighted.
- Scroll bars are rounder, and the thin one is brighter, so it is easier to find.
- The Info screen no longer shows the profile and loader line under the Play buttons.

### Fixed

- The window can be dragged by its header again.
- The Play button in the header is less bright; it lights up on hover.
- Scroll bars everywhere stay a thin line and widen only while the pointer is right next to them (also at the very
  window edge); mod cards no longer change width.
- Wheel scrolling no longer jumps back with many expanded cards (their sizes are kept across list refreshes), and keeps
  working after opening a dependency from a mod card.
- Collapsing a card near the end of the list no longer leaves the list empty until it is scrolled.
- Changing the interface scale resizes the window right away, also after it was maximized and restored.
- Info screen: in large windows the silk flies are no longer cut off at the window edge.
