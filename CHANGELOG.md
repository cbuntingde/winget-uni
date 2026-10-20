# Changelog

All notable changes to Winget Uni are recorded here, newest first.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.0.0] - 2026-10-20

First release: one window for WinGet, Chocolatey, Scoop, pip, npm,
.NET tools, and the PowerShell Gallery.

### Added

- Installed, Discover, Updates, Operations, and Settings pages with light
  and dark themes, tray icon, and configurable close behaviour.
- Operation queue running installs, updates, and removals one at a time
  with live output and cancellation.
- Export of the installed set to JSON and import from a file written on
  another machine. Import installs recorded versions; a partial export
  names the managers that could not be read.
- Per-manager availability reporting, enable switches, and search-result
  caching.

### Fixed

- One manager failing no longer discards every other manager's results;
  parser failures are reported per manager and cancellation propagates.
- Managers without a search command (pip) are skipped silently in
  multi-manager searches instead of attaching an error to each one.
- A single failed .NET tool lookup no longer aborts the whole update list.
- Search rows without an id are never shown as actionable.
- Winget Match/Source columns are detected by heading, with a last-column
  fallback for localized output.
- Package lists are written atomically, oversized files are rejected, and
  the queue shutdown waits out the process kill grace.

[1.0.0]: https://github.com/cbuntingde/winget-uni/releases/tag/v1.0.0
