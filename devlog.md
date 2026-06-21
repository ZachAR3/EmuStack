## Big update time!:

This is a big one — YuzuToolbox is now EmuStack. The whole project has been rewritten on top of a pluggable provider architecture, meaning the app is no longer tied to one emulator. It now supports Eden and Ryubing (with legacy Ryujinx targets), and runs on Windows, Linux, AND macOS. Existing Yuzu Toolbox installs are migrated into the Eden provider automatically.

The UI got a full refresh with proper scaling for different display sizes, a new icon and splash, and light/dark themes. A lot of bugs that were breaking basic flows have been hunted down and fixed — the confirmation popup was tiny and basically unusable, the download screen would get stuck and never go away, and on macOS the Eden download would silently do nothing after the popup. All of that is sorted now.

### Changes

- Rebranded from YuzuToolbox to EmuStack with new icon, splash, and itch art
- Added pluggable provider architecture (Eden + Ryubing/Ryujinx)
- Added macOS support across the board
- Added architecture-aware download selection (arm64 vs amd64)
- Updated to Godot 4.6
- Refactored per-provider settings (separate install, mod, save, and app-data dirs per emulator)
- Added responsive UI scaling from a 1080p base
- Added native file dialogs for folder picking
- Fixed confirmation popup being tiny and not working properly (switched to a real dialog)
- Fixed download overlay never going away after a successful download
- Fixed Eden macOS downloads silently failing (.dmg wasn't being handled correctly)
- Fixed unnecessary re-fetching of release lists before each download (downloads now start instantly)
- Fixed mod manager crashes when no game is selected
- Fixed download progress label not scaling with window size
- Removed dead yuzu mod source code
- Bug fixes / Cleanup
