# Changelog

All notable changes to this project will be documented in this file.

## [3.0.0] - 2026-05-14

### Added
- **New WPF UI:** Completely rewritten frontend with modern styling, dark theme, and glassmorphism effects.
- **Persistent Sniffer Extension:** Browser extension now tracks up to 100 detected streams using `MutationObserver` for dynamic site support.
- **HLS Fragment Tracking:** Real-time visualization of fragment-based downloads (X/Y fragments).
- **Size Estimation:** Real-time calculation of download sizes and total queue size.
- **Global Error Handling:** Implemented robust crash logging to `crash_log.txt`.
- **System Tray:** Support for minimizing to tray with a custom icon.
- **Private Network Access:** Support for Chrome's secure communication standards on port 18888.

### Changed
- **Communication Port:** Switched default port from 19999 to 18888 for better compatibility.
- **Detection Logic:** Extension now passes page titles and metadata to the desktop app to avoid "Unknown" labels.
- **Tool Detection:** Improved `yt-dlp` and `ffmpeg` search logic (now checks `bin/` and `tools/` folders).

### Fixed
- Fixed `NullReferenceException` in system tray initialization.
- Fixed `ReferenceError` in extension popup connection logic.
- Fixed `ApplicationException` when releasing Mutex during shutdown.
- Fixed CORS and Private Network security blocks in Chrome.
