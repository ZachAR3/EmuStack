# EmuStack

EmuStack is a desktop utility for downloading, updating, launching, and managing supported Nintendo Switch emulator builds from multiple providers.

Current provider targets:
* Eden through the official Eden release site
* Ryubing through the Ryubing update server
* Legacy Yuzu/PineappleEA support while the compatibility path remains useful

Current tool areas:
* Cross-platform installs for Windows and Linux, with macOS downloads detected where providers expose them
* Updating with replacement of previous install files
* Shortcut creation and automatic unpacking where supported by the provider package
* Save backup and restore helpers
* Mod management for installed games from the existing mod sources

# Donate
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/R5R4NFO8V)

# Installing
The recommended install method is through Itch.io because it provides updating features and easier launching: https://zachar3.itch.io/emustack
To install without Itch, use the [releases page](https://github.com/ZachAR3/EmuStack/releases), download the zip file for your OS, and extract it into its own folder.

# Usage
Select a provider, choose an install location, then click download and wait for the install to finish. Optionally, launch the program with `--launcher` after an emulator has been installed to check for updates, install one if available, launch the selected emulator, and close EmuStack.

Warning: many GameBanana mods are meant for Nintendo Switch hardware rather than emulators, so they may not work when installed through EmuStack.


![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/DarkInstaller.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/DarkTools.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/DarkModManager.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/DarkSettings.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/LightInstaller.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/LightTools.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/LightModManager.png?raw=true)
![](https://github.com/ZachAR3/EmuStack/blob/main/DemoImages/LightSettings.png?raw=true)


# Resources:
Big thanks to the repo owners whose projects helped inform the original mod-management implementation:
* https://github.com/pilout/YuzuUpdater/blob/master/YuzuEAUpdater/

Their work was especially helpful around mod-management behavior and source parsing.
