# EmuStack Repository Evaluation

Review date: 2026-07-14<br>
Reviewed revision: `5f71d85` (`main`, matching `origin/main`)<br>
Review type: static review followed by implementation and validation.

## Final remediation status

The original evidence below is retained as the pre-remediation baseline. Each finding was re-verified against the current call paths before implementation.

| ID | Verification | Final status | Concise disposition |
|---|---|---|---|
| F-01 | Valid | Fixed | Destructive clearing now requires a canonical EmuStack ownership marker and refuses links, roots, home, and non-owned directories. |
| F-02 | Valid | Fixed | Mod delete validates provider/game containment and an exact per-mod ownership marker; unmanaged folders are read-only. |
| F-03 | Valid | Fixed | Mod downloads use unique staging files and validate transport result, HTTP status, existence, and bounded size before archive access. |
| F-04 | Partially valid | Partially fixed | Downloads stage to `.part`, have size/format checks, and commit atomically; provider APIs expose no trusted digest/signature, so authenticity verification remains unavailable. |
| F-05 | Valid | Fixed | Emulator releases are downloaded/extracted/verified in same-volume staging, then swapped with rollback and an interruption journal. |
| F-06 | Valid | Fixed | Version and executable path are persisted together only after a verified deployment; manual downloads are not recorded as installed. |
| F-07 | Valid | Fixed | Mod update stages and validates the replacement, swaps only after success, persists before cleanup, and rolls back filesystem/metadata failures. |
| F-08 | Valid | Fixed | Installed-mod indexes are provider-scoped; legacy entries are retained only when contained by the current provider/game root. |
| F-09 | Valid | Fixed | Corrupt indexes are quarantined/recovered from backup and owned folder markers reconcile missing entries; reset now rebuilds instead of orphaning. |
| F-10 | Partially valid | Partially fixed | Settings/JSON writes are atomic with backups and legacy copy is reachable; historical per-version schema migrations cannot be reconstructed from repository data. |
| F-11 | Valid | Fixed | Search results are guarded by query, game, operation generation, cancellation, and scene lifetime before UI mutation. |
| F-12 | Valid | Fixed | Refresh resets pagination and invalidates prior browse/search operations. |
| F-13 | Valid | Fixed | Malformed title rows with fewer than three fields are skipped without aborting discovery. |
| F-14 | Valid | Fixed | Windows shortcut creation is enabled and uses the existing `.lnk` implementation. |
| F-15 | Valid | Fixed | Linux shortcuts use an escaped desktop entry and atomically install to the user XDG applications directory without privilege elevation. |
| F-16 | Valid | Fixed | A shared extractor rejects traversal, absolute paths, links, duplicates, reparse points, excessive entries/sizes, and extracts only to private staging. |
| F-17 | Valid | Fixed | Scene-owned cancellation, request cancellation, dialog completion, event disconnection, generation checks, and durable operation recovery were added. |
| F-18 | Valid | Fixed | Async signal boundaries now restore controls/loading state and report contextual terminal failures; cancellation is handled separately. |
| F-19 | Valid | Fixed | Installer and mod destructive actions acquire an operation gate before confirmation and reject re-entry until terminal cleanup. |
| F-20 | Valid | Fixed | Custom versions, release filenames, extracted folder names, and executable names are constrained and containment-checked. |
| F-21 | Valid | Fixed | macOS `.app` bundles launch through Launch Services; DMG/manual downloads are no longer recorded as runnable installs. |
| F-22 | Valid | Fixed | Backup/restore rejects overlapping/linked roots, requires backup ownership, stages snapshots, atomically swaps, and preserves unrelated restore files. |
| F-23 | Valid | Partially fixed | Network calls now have bounded timeouts, cancellation, and response/request disposal; automatic retry/backoff remains intentionally unimplemented. |
| F-24 | Valid | Deferred | Profiles, dependency solving, conflict policy, enable/disable, and load order are new product features requiring a domain/UI specification, not bounded defect fixes. |
| F-25 | Valid | Fixed | Stable IDs drive UI rows and unique directories; a marker, not a name prefix, establishes management ownership. |
| F-26 | Valid | Deferred | Safe archive/filesystem/persistence boundaries were extracted, but a broad controller rewrite was not required for correctness and remains architectural work. |
| F-27 | Valid | Partially fixed | Archive extraction and save copying run on workers and source results are deduplicated, but large discovery scans and per-mod API expansion remain sequential. |
| F-28 | Valid | Partially fixed | A deterministic disposable safety suite now covers paths, links, archives, ownership, atomic writes, backup/restore, identity, and OS parsing; CI is still absent. |
| F-29 | Valid | Partially fixed | Errors now include operation/mod/game/provider context and preserve rollback outcomes; a typed result model and persistent structured log remain architectural work. |
| F-30 | Partially valid | Partially fixed | Unknown platforms now fail explicitly and documentation targets Godot 4.7; macOS signing/notarization remains a release-credential policy. |

### Directly related issues discovered

| ID | Final status | Resolution |
|---|---|---|
| D-01 | Fixed | Interrupted installer/mod swaps could orphan the retained previous directory; phase journals now deterministically roll forward or restore on startup. |
| D-02 | Fixed | The scene-owned `StandardModManagement` node was never parented and leaked at shutdown; it is now owned by `ModManager`. |
| D-03 | Fixed | Single-file AppImage deployments cannot use archive executable discovery; the staged artifact itself is now verified, permissioned, and committed as the executable. |
| D-04 | Fixed | Transactional save-copy journals now mark replacement destinations and refuse to discard an unowned destination during rollback or recovery. |
| D-05 | Fixed | Mod update/delete and settings/theme rollback now retain prior metadata and recoverable journals when persistence or cleanup fails. |
| D-06 | Fixed | Per-game mod pagination, startup recovery with unset save paths, and clear-mods exception boundaries now preserve UI and state consistency. |
| D-07 | Fixed | macOS bundle installs now validate `Contents/MacOS`, set executable permissions before commit, and reject reparse points when launching. |
| D-08 | Fixed | Managed install/mod/save trees are now preflighted recursively so nested links are rejected before ownership-based moves or cleanup. |
| D-09 | Fixed | Resetting the installed-mod index now persists only after every game scan succeeds and restores the prior in-memory index on failure. |
| D-10 | Fixed | Archive directory creation and post-extraction executable discovery now preflight reparse points and enforce deployment containment; startup settings-save failures now reach the engine log before UI autoloads exist. |
| D-11 | Fixed | The disposable .NET test tree is now excluded from Godot import, preventing generated `.uid` files and import warnings for non-project scripts. |
| D-12 | Fixed | Mod-manager shutdown no longer snapshots the installed index during an active transaction; the operation journal now remains the source of truth for interruption recovery. |

### Implementation summary

- Added canonical containment/link checks, exact ownership markers, safe archive extraction, bounded staged downloads, atomic writes, transactional install/update/delete/backup flows, rollback, and startup recovery journals.
- Added provider-scoped mod persistence, conservative legacy migration/reconciliation, stable identity, stale-result/lifecycle guards, operation serialization, bounded network policy, and platform/input fixes.
- Added `Tests/EmuStack.SafetyTests.csproj` with 19 disposable safety tests; no real emulator, game, mod, save, or user directory is touched.
- Validation: the final review build passed with 0 warnings/errors; safety tests passed 19/19; Godot 4.7 headless scene smoke passed; `git diff --check` passed.
- Remaining significant limitations: no provider-backed artifact signatures/checksums, profile/dependency/conflict/load-order features, CI, persistent structured operation log, or runtime platform validation on Windows/macOS. Godot editor import completed, with only the sandbox-blocked editor IDE socket error.

## Final independent review (2026-07-14)

Reviewed the complete remediation diff and relevant history, then re-traced filesystem ownership, archive extraction, emulator install/update, mod install/update/delete, backup/restore, persistence, async generation/lifetime, pagination, shortcut creation, and macOS launch paths. The review also checked generated-file boundaries, solution wiring, tests, analyzer output, and Godot headless/editor startup.

Issues found and fixed:

- Transaction journals treated some staging/rollback phases as committed. A crash during rollback could delete the retained install or restore the wrong metadata. Install, mod-update, and save-copy journals now record ownership and prior state, write an explicit rollback phase before mutation, refuse unowned replacement destinations, restore metadata consistently, and leave recoverable journals when cleanup is blocked.
- Root and broken-link checks were incomplete for owned deletion, archive destinations, atomic replacements, selected folders, and shortcut paths. Canonical/root/reparse validation now runs before those writes or deletes, and release/mod URLs are restricted to HTTP(S).
- Mod-update cancellation/exception paths could leave the in-memory installed path pointing at the replaced directory; rollback now restores the old object and persists it. Pagination now tracks each game and preserves an exhausted-page state, stale title/mod results are generation-checked, and save-copy recovery runs during tools-page startup without treating unset paths as failures.
- Shortcut and settings failures could surface as an install failure after the install was already committed, or leave temporary files behind. Shortcut creation is now non-fatal and contained, settings/theme rollback is guarded, clear-mods errors are observed, and macOS bundles are permissioned and reparse-checked before launch.
- Archive directory entries and extracted executable discovery now perform an additional reparse/containment preflight, and initial settings persistence failures are reported through `GD.PushError` even before the UI error console exists.
- Added `Tests/.gdignore` so Godot does not import the separate disposable test harness or generate project metadata for it.
- Shutdown saves now defer while a mod transaction is active, preventing a partial in-memory snapshot from racing journal rollback/recovery.
- Added focused regressions for filesystem roots, broken-link atomic writes, and staging/uncommitted copy recovery; the disposable suite now has 19 tests.

The previous remediation summary was incomplete: its 18-test count was stale (the suite is now 19), and its “durable recovery” claim did not cover the staging/rollback phase that could remove an existing install or an unowned replacement destination. It also did not include automatic save-copy startup recovery, per-game pagination state, theme persistence rollback, or the mod-update in-memory metadata rollback now covered here.

Remaining risks and intentional deferrals: provider artifacts still have no trusted checksum/signature; HTTP download limits are enforced at progress/completion boundaries rather than by a streaming byte cap; ownership markers are local and forgeable, and broad filesystem TOCTOU races cannot be eliminated by these checks alone. Retry/backoff, CI, persistent structured operation logs, profiles/dependencies/conflicts/load order (F-24), and the broad controller redesign (F-26) remain deferred. Runtime validation on Windows/macOS was not available. The Godot editor smoke test exits successfully but reports the sandbox-blocked IDE socket error.

Exact final validation:

- `DOTNET_CLI_HOME=/tmp/emustack-dotnet dotnet restore Tests/EmuStack.SafetyTests.csproj --nologo -v:minimal -p:NuGetAudit=false` — passed.
- `DOTNET_CLI_HOME=/tmp/emustack-dotnet dotnet build EmuStack.sln --no-restore --nologo -v:minimal -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=true -p:TreatWarningsAsErrors=true` — passed, 0 warnings/errors.
- `DOTNET_CLI_HOME=/tmp/emustack-dotnet dotnet run --project Tests/EmuStack.SafetyTests.csproj --no-build` — passed, 19/19.
- `HOME=/tmp/emustack-godot-review-final/home XDG_DATA_HOME=/tmp/emustack-godot-review-final/data XDG_CONFIG_HOME=/tmp/emustack-godot-review-final/config XDG_CACHE_HOME=/tmp/emustack-godot-review-final/cache godot-mono --headless --path . --quit-after 2` — passed.
- The same isolated HOME/XDG environment with `godot-mono --headless --path . --editor --quit` — exit 0; only the sandbox IDE socket error was reported.
- `git diff --check` — passed.

## 1. Executive Summary

EmuStack is a compact Godot 4 desktop utility with a sensible high-level provider abstraction for downloading and launching emulator builds. The scene structure is understandable, the provider-specific release code is separated from the main installer, and the UI communicates the most destructive emulator-install action with warnings and confirmation dialogs.

The current implementation is not safe enough for a release that manages user game installations and mod files. The most serious problems are concentrated in the filesystem and state boundaries:

- Destructive operations recursively delete user-selected directories, with no application-owned-root or ownership manifest. Mod removal also trusts `InstalledPath` loaded from JSON.
- Emulator updates delete the existing installation before the replacement has downloaded, been validated, or been extracted. There is no backup, journal, atomic swap, or rollback.
- Mod updates delete the old mod before installing the replacement, and the install result is ignored. A failed update can be reported as successful while the old mod and its metadata are gone.
- Archives are extracted with `ExtractFullPath = true` but the application does not itself reject traversal, absolute paths, links, duplicate entries, decompression bombs, or excessive resource use. The exact path-traversal behavior of SharpCompress 0.49.1 could not be runtime-verified in this environment.
- Downloaded files are written directly to final paths. Mod downloads do not inspect the completion result at all, and no download has checksum/signature verification or an atomic `.part`-to-final transition.
- `InstalledMods.json` is global even though mod roots are per provider. Switching providers can load entries whose paths belong to another provider. Resetting the index makes managed directories invisible without deleting them.
- Many operations are `async void` with no cancellation or scene-lifetime ownership. Reloading the scene during network, download, confirmation, or archive work can leave stale callbacks, stranded tasks, stale UI, or partial filesystem state.

The code quality is appropriate for an early prototype, not yet for a safety-critical file-management workflow. `ModManager.cs` is a 1,027-line UI/controller object, persistence and filesystem effects are directly embedded in UI-facing code, and global singletons are used as the main coordination mechanism. There is a useful `Mode`/provider seam, but there is no corresponding transactional domain layer for installation, ownership, conflicts, or recovery.

Confidence is high for findings based on direct control flow and file contents. Confidence is medium for SharpCompress link-extraction behavior and macOS process-launch behavior because the required runtime and dependencies were unavailable. No real game installation, user save directory, or external download was touched.

Overall assessment: **featureful prototype with high data-integrity and lifecycle risk; not ready for unattended or safety-sensitive destructive operations without a transactional filesystem/metadata redesign and failure-path tests.**

## 2. Repository and Application Overview

### Purpose and technology

The README describes EmuStack as a desktop utility for downloading, updating, launching, and managing Nintendo Switch emulator builds from Eden and Ryubing/Ryujinx sources, plus browsing and installing GameBanana mods. The repository contains:

- Godot 4.7 with the .NET project format (`project.godot`, `EmuStack.csproj`).
- .NET 8 C# code and NuGet dependencies: HtmlAgilityPack, Mono.Posix.NETStandard, Newtonsoft.Json, SharpCompress, and WindowsShortcutFactory.
- One main scene, `Scenes/Home.tscn`, with embedded page scenes for installation, tools, mod management, settings, and the error console.
- 23 C# source files, six scenes, themes, imported images/fonts, and export presets for Windows, Linux, and macOS.
- No `.gd` files. The requested C#↔GDScript interoperability review is therefore not applicable to the committed code; all runtime scripts inspected are C#.
- No test project, test fixture, CI workflow, analyzer configuration, or package lock file was found.

### Main runtime and data flows

1. `project.godot` starts `Scenes/Home.tscn` and autoloads `Scripts/Globals.cs`.
2. `Globals._Ready()` loads `user://InternalSave.tres` in debug or `user://InternalSave.res` in non-debug builds, chooses a provider, migrates legacy fields, applies defaults, and persists settings.
3. `Home._Ready()` defers a `call_group("Initiate", "Initiate")`. Because all pages are already instantiated in the main scene, hidden pages also initialize. The installer fetches provider releases and the mod manager starts title/mod discovery during startup.
4. The installer downloads a provider release into the selected install directory, optionally clears that directory, optionally extracts it, finds an executable, and persists provider settings.
5. The mod manager ensures the provider's `ModsLocation` exists, fetches the Switchbrew title page, scans immediate game directories, loads `user://InstalledMods.json`, and fetches GameBanana records. Online mods are downloaded into `ModsLocation/<title-id>/<mod-name>-Download`, extracted into `Managed<mod-name>-temp`, then moved to `Managed<mod-name>`.
6. Tools perform directory clearing, recursive save backup/restore, process launch, confirmation dialogs, folder picking, and error-console updates.

The most important data invariant—“the filesystem, persisted metadata, and current provider all describe the same installed content”—is not represented explicitly or checked before destructive operations.

## 3. Architecture Map

| Subsystem | Main files and entry points | State owned | Dependencies and outputs | Assessment |
|---|---|---|---|---|
| Application startup | `Globals._Ready`, `Home._Ready`, `Home.Initiate` | Active provider, settings singleton, HTTP client | Resource save, provider registry, all page `Initiate` methods | Simple startup path, but hidden pages perform work immediately and global lifetime exceeds page lifetime. |
| Settings and migration | `ResourceSaveManager`, `SettingsResource`, `ProviderSettingsResource`, `Globals` | Provider paths, executable, release channel, display/theme state | Godot Resource serialization | Provider settings are separated, but writes are non-atomic and legacy loading has an unreachable branch. |
| Provider/release discovery | `Mode`, `ModeEden`, `ModeRyubing`, `ProviderRegistry` | Release lists and selected release in `Installer` | Eden HTML, Ryubing update server/Forgejo | The provider seam is the strongest architectural area; validation, timeout, and trust policy are missing. |
| Emulator installation | `Installer`, `FsHelpers`, `ShortcutFactory` | Selected release, install path, executable path, transient download state | HTTPRequest, SharpCompress, Unix permissions, OS processes | Destructive and non-transactional; UI and filesystem effects are tightly coupled. |
| Mod discovery | `ModManager`, `BananaManager`, `Mod` | Title map, browse/search lists, current selection | Switchbrew HTML, GameBanana API, JSON | Works as a browse prototype, but parsing and async state are fragile. |
| Mod deployment | `StandardModManagement`, `ModPaths`, `FsHelpers` | Installed/source lists and per-mod directory path | HTTPRequest, SharpCompress, recursive delete/move | No ownership, conflict, dependency, enable/disable, or rollback model. |
| Tools and save management | `Tools`, `ToolsPage` | Dialog TCS objects, error UI, selected save paths | Godot dialogs, `System.Diagnostics.Process`, recursive copy/delete | Useful central helpers, but arbitrary paths and unstructured async completion create risk. |
| UI/navigation | `Home`, page scenes, scene signal connections | Visible controls and selected provider/game/mod | Godot scene tree and signals | Scene wiring is legible; lifecycle ownership and stale callbacks are not. |

The main architectural boundary is between provider release selection and the rest of the app. The missing boundary is between UI intent and a transactional operation service: UI handlers currently decide when to delete, write, persist, refresh, and report success.

## 4. Review Scope and Methodology

### Inspected

- Git status, branches, recent history, and recent feature/refactor diffs.
- `project.godot`, `EmuStack.csproj`, `EmuStack.sln`, `export_presets.cfg`, `.gitignore`, README, devlog, and contribution files.
- Every committed C# source file, all six scenes, scene node paths, signal connections, exported properties, themes, and referenced assets.
- All filesystem, archive, process, persistence, HTTP, async, signal, and scene-reload call sites.
- Search for tests, fixtures, GDScript, cancellation, rollback, dependency/conflict/profile support, checksums, schema handling, and error paths.
- Recent Git changes, especially the provider refactor, mod search/linking, Godot 4.7 update, and Ryubing update-server change.

### Commands and results

| Command/check | Result |
|---|---|
| `git status --short --branch --untracked-files=all` | Clean: `## main...origin/main`. |
| `git log --oneline --decorate -15` | Reviewed current revision and recent provider/mod/Godot changes. |
| `rg --files` and source/scene inventory | 23 C# files, six scenes, no GDScript or tests. |
| `git diff --check` | Passed with no output. |
| Resource-reference existence scan for `res://...` paths | No missing file paths reported. This is not a Godot import validation. |
| `godot --headless --path . --editor --quit` | Could not run: `godot: command not found`. |
| `dotnet --info`, `dotnet build --no-restore` | Could not run: `dotnet: command not found`. |

No application run, archive extraction, download, process launch, save operation, or real filesystem deletion was performed. No network request was intentionally made during the review. The report therefore does not claim successful compilation, scene import, or runtime behavior where those could not be observed.

## 5. Subsystem-by-Subsystem Evaluation

### 5.1 Application startup, navigation, and shutdown

`Globals` is a Godot autoload and is initialized before `Home` uses it. `Home` then sets the theme, provider selector, display mode, and defers page initialization. This is easy to follow. The important problem is that a scene reload is used as the provider-switch mechanism (`Home.cs:113-117`), while asynchronous work started by the old scene has no cancellation or ownership token. The root window's `CloseRequested` signal is also subscribed to from each `ModManager` instance (`ModManager.cs:80-85`), rather than through a single app-lifetime coordinator.

Areas that appear sound: the autoload is declared in `project.godot`, provider selection is normalized through `ProviderRegistry`, and the UI has a central error console. Findings F-17, F-18, and F-19 cover lifecycle and failure behavior.

### 5.2 Settings, persistence, and migration

`ResourceSaveManager` stores settings as a Godot Resource and uses an integer version. `Globals` maps legacy single-provider fields into the selected provider and applies defaults for each registered provider. This is a reasonable migration direction, and unknown provider IDs fall back to Eden instead of crashing.

The implementation does not provide atomic replacement, a backup, a durable schema migration path, or validation of null/invalid resource contents. The legacy-copy helper is only called from `LoadSave`, but `GetSettings` calls `LoadSave` only when `SaveExists()` is already true, so the “copy legacy save when current save is absent” branch is unreachable. F-09 and F-10 cover the consequences.

### 5.3 Provider and release discovery

`Mode` contains useful provider-independent behavior: default paths, executable candidates, archive extension selection, and release lookup. `ModeEden` and `ModeRyubing` isolate provider-specific URL and asset selection logic, which is a good foundation for adding a provider.

The implementation has no common timeout/cancellation policy, no download authenticity policy, and only partial version validation. Eden parses live HTML and only recognizes numeric versions plus `-rcN`; Ryubing accepts arbitrary custom-version strings into a URL and filename. Some provider errors are caught broadly and silently converted into an empty release list, while other JSON/network errors escape. F-04, F-16, F-20, and F-23 cover these boundaries.

### 5.4 Emulator installation and launch

The normal flow is `InstallSelectedVersion` → `InstallVersion` → optional `DeleteOldVersion` → `HTTPRequest` download → `VersionDownloadCompleted` → optional extraction/permission setting → shortcut creation/launch. The user can choose the install directory and executable name.

This flow has no transaction. The old directory can be wiped before a new download begins; a successful HTTP response is treated as enough to persist `InstalledVersion` before extraction establishes an executable; extraction is synchronous on the main thread; and extraction errors are not caught inside the `async void` completion handler. Windows shortcut creation is disabled in `Installer.Initiate` even though an implementation exists. F-05, F-06, F-14, and F-15 are direct findings.

### 5.5 Mod discovery and import

The mod manager scans directories under the configured provider mod root and treats directories whose names do not start with `Managed` as local unmanaged mods. Online records come from GameBanana's Subfeed and a second Files request. Search has a five-page cap and attempts to deduplicate by `ModId`.

There is no local archive/directory import workflow beyond scanning directories, no manifest parser, no checksum, no ownership manifest, and no dependency/conflict metadata. The title HTML parser is positional and can index beyond the length it checked. Refresh does not reset the page counter. F-11, F-12, F-13, F-16, and F-24 cover this subsystem.

### 5.6 Mod installation, removal, and update

`StandardModManagement.InstallMod` downloads an archive to a destination-derived filename, extracts every non-directory entry to a staging directory, deletes the existing managed directory contents, moves staged files into place, deletes the archive, and records the `Mod` object. `DeleteMod` removes the object from in-memory state and then recursively deletes its path. `UpdateMod` composes delete and install.

The staging step is a useful start, but the commit is not atomic, deletion failures are ignored, and metadata is changed before filesystem success. There is no conflict graph or file ownership ledger, so the manager cannot restore an overwritten game/mod file or distinguish a manager-owned file from a user edit. F-01, F-02, F-03, F-07, and F-16 are the important issues.

### 5.7 Profiles, dependencies, conflicts, and enable/disable

These capabilities are not implemented. `Mod` contains only an ID, name, URL, compatible-version list, source integer, and installed path (`Scripts/Sources/Mod.cs:4-12`). There is no profile type, dependency constraint, conflict record, load-order field, enabled flag, deployed-file ownership map, or restoration backup. The current model is “one downloaded archive becomes one directory,” not a reversible deployment system. This is a confirmed product/architecture limitation, documented as F-24 rather than inferred as a hidden bug.

### 5.8 Tools, save backup, and external processes

The tools page provides recursive directory clearing and recursive copy-based save backup/restore. `Tools` provides process launch, confirmation, native folder selection, and an error console. The centralized helper is convenient, but it accepts arbitrary user-selected source/destination relationships, does not define a backup transaction, and does not verify process exit status or escape Linux desktop-entry arguments. F-01, F-15, and F-22 cover this area.

### 5.9 C# and GDScript interoperability

No GDScript exists in the repository, so there are no cross-language method, signal, Variant, enum, collection, or serialization boundaries to validate. The actual interop surface is C#↔Godot: exported fields, scene string connections, `Godot.Collections.Array<ProviderSettingsResource>`, `ResourceSaver`, `HTTPRequest` signals, C# events, and `CallDeferred`. Those boundaries are affected by the lifecycle and async findings, especially F-17 and F-18. A runtime C# scene-load check could not be performed.

## 6. End-to-End Workflow Analysis

| Workflow | Observed path | Result and risk |
|---|---|---|
| First launch | `Globals._Ready` → settings load/defaults → `Home` deferred group initialization | Defaults are applied, but hidden installer/mod pages also start network work. Legacy save copy is bypassed when no current save exists. |
| Normal restart | Resource settings plus global `InstalledMods.json` are loaded | Paths and mod entries are trusted. A missing/corrupt JSON file can be overwritten with a partial reconstruction. |
| Close during work | Root `CloseRequested` saves JSON; outstanding async operations have no cancellation | A close can race with a write/download/extraction. Scene-owned tasks or confirmation awaiters can remain unresolved. |
| Provider switch | `Globals.SetAppMode` then `ReloadCurrentScene` | Old page requests/tasks can outlive the scene; global mod metadata can describe the previous provider. |
| Add game manually | 16-character uppercase hexadecimal validation, create `<ModsLocation>/<title-id>` | Input validation is good for the expected title-ID format. Title lookup may still fail or crash on malformed HTML. |
| Discover local mods | Immediate subdirectories are scanned; names beginning with `Managed` are skipped | Managed folders require JSON metadata; unmanaged folders are treated as deletable mods without ownership confirmation. |
| Browse/search online mods | GameBanana title lookup, Subfeed pages, per-mod Files request | Search is sequential and uncancellable. Stale results can replace newer query/game state. |
| Install online mod | HTTPRequest → await signal → open destination archive → SharpCompress extraction → delete/merge/record | Completion status, archive safety, integrity, and atomicity are missing. A failed install can leave partial files; a stale archive may be parsed. |
| Remove mod | Remove metadata/source list → recursive delete of `InstalledPath` | The path is not validated against current provider/game root; user files in unmanaged directories are deleted. |
| Update mod | Delete old mod → install same `Mod` → return `true` regardless of install result | Failed updates can lose the old install and still appear successful. |
| Emulator update | Clear selected install dir → download → set `InstalledVersion` → extract/launch | Old version is lost on pre-download failure; persisted version can be newer than deployed executable. |
| Profile switch | Not implemented | No profile state or partial-switch recovery exists. |
| Enable/disable/load order | Not implemented; folders are always treated as present | There is no reversible deployment or conflict winner calculation. |
| Save backup/restore | Recursive copy with overwrite | No same/inside path validation, snapshot, stale-file removal, or atomic rollback. |
| Game update/manual edits | No reconciliation or ownership/hash scan | External changes are not detected; removal cannot distinguish manager-owned files from user changes. |

## 7. Cross-System and Integration Analysis

### Competing sources of truth

| State | Source | Competing representation | Failure mode |
|---|---|---|---|
| Active provider | `Globals.AppMode`, `Settings.AppMode` | Scene controls and cached `_os`/release lists | Old scene async results can act after provider changes. |
| Provider paths | `ProviderSettingsResource` | UI button text and cached `_executableName` | Reset/settings changes do not refresh all page controls. |
| Installed mods | `InstalledMods.json` | Direct directory scan, `_installedMods`, `_browseMods`, `_searchMods` | Provider mixing, orphaned managed folders, duplicate entries, stale lists. |
| Download state | `HTTPRequest` file path and UI controls | `_activeDownloadRelease`, `_downloadFileName`, provider settings | Concurrent/re-entered downloads can overwrite state and files. |
| Operation result | Error console text | Boolean returns ignored by callers | UI can show success or remain blocked without a durable operation state. |

### UI versus backend

Controls are disabled during some single-mod operations but not during `UpdateAll`, refresh, source changes, or all confirmation waits. List items are identified by display-name metadata rather than a stable row object or ID. `SelectGame` derives a game ID by matching the displayed title string, so duplicate title names are ambiguous. The UI has no explicit loading/error/partial-success state model beyond a panel and error text.

### Async and threading

No worker threads are used, which avoids direct cross-thread scene mutations, but archive extraction, directory deletion, copy, and file moves all execute on the Godot main thread after the HTTP await. Large archives and save trees can freeze the UI. There are no cancellation tokens or per-operation locks. The main-thread-only concern is therefore less “wrong thread” and more “unbounded synchronous work plus re-entry and scene lifetime.”

### C# / Godot boundaries

Scene connections are declared by method name, request completions are connected by signals, and `CallDeferred` is used to cross a frame boundary. Some callbacks check `IsInstanceValid`, but there is no uniform `_ExitTree` cancellation/disconnect strategy. `StandardModManagement` is a `Node` created with `new` and never added to the scene tree, even though it uses `Node.ToSignal`; this is unnecessary coupling and should be removed or made an explicit scene-owned service.

## 8. Filesystem, Data-Loss, and Security Risks

The application has three classes of destructive operation:

1. **Whole-root deletion:** `FsHelpers.DeleteDirectoryContents` is used for emulator install cleanup and the Tools clear button. It deletes every file and directory below the supplied path.
2. **Per-mod recursive deletion:** `StandardModManagement.DeleteMod` deletes the entire path recorded in `Mod.InstalledPath`, including local unmanaged mod folders.
3. **Archive-controlled writes:** SharpCompress writes archive entry paths into a staging or installation directory with no application-level canonical-path or link validation.

The only generic delete guard rejects a path equal to a drive root or `Environment.SpecialFolder.UserProfile` (`FsHelpers.cs:15-20`). It does not establish that the path is an EmuStack-owned directory, does not reject a user Documents/Desktop/AppData path, and does not protect against path spelling/case differences or reparse-point traversal. The mod deletion path has no such guard at all.

There is no ownership record for each deployed file, no backup of original files, no hash of manager-written files, no conflict winner record, no journal, and no recovery marker. Consequently the system cannot safely answer “is this file still ours?” before replacing or deleting it.

The external-data trust boundary is also broad: release URLs and GameBanana download URLs are accepted from remote responses, archives are not checked for expected content, and no checksum/signature is compared. HTTPS protects transport but does not replace artifact authenticity or a user-visible trust decision.

## 9. Godot 4-Specific Findings

- **Autoload and initialization:** `Globals` is correctly declared as an autoload, but all page nodes are in the initial scene and are initialized through a deferred group call. This means hidden pages begin network work and each provider scene reload starts another set of requests.
- **Scene reload and freed nodes:** `Home.ModeChanged` calls `ReloadCurrentScene` while page HTTPRequests, timers, confirmation dialogs, and async continuations may be active. There is no `_ExitTree`/`_Notification` shutdown path that cancels or invalidates these operations.
- **Signals/events:** scene signal connections are clear, but `ModManager` subscribes to the root window close event from each scene instance. The connection is not explicitly removed. Request callbacks and UI writes do not share a generation/lifetime check.
- **Deferred calls:** `Home` uses `CallDeferred` for group initialization and `Tools.AddError` defers UI mutation. The deferred error lambda is not validity-checked before it touches `_errorConsole`; only the later five-second continuation checks `this` and `_errorNotifier`.
- **`async void`:** UI signals invoke many `async void` methods. Exceptions cannot be returned to the caller, and several handlers lack `try/finally` blocks to restore controls or hide loading panels.
- **Main-thread work:** SharpCompress extraction, recursive delete/copy, and executable discovery are synchronous. A large archive or save directory can block frame processing even though the surrounding method is `async`.
- **Godot collections/resources:** settings use an exported Godot `Array` of Resources, while mod state uses `System.Text.Json` over ordinary C# objects. There is no compatibility test for missing/null Resource fields or cross-version JSON data.
- **C#↔GDScript:** no GDScript scripts are committed, so the requested mixed-language checks are not applicable. The remaining C#↔Godot signal/property surface was not runtime-validated because Godot is unavailable.

## 10. Confirmed Bugs

The following findings are confirmed from committed control flow. “Confirmed” means the unsafe or incorrect behavior is present in the code; it does not mean every trigger was executed against a real installation.

### F-01 — Destructive directory clearing has no application-owned scope

**Category:** Filesystem safety / data loss<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/FsHelpers.cs:8-35`, `Scripts/Installer.cs:226-239`, `Scripts/ToolsPage.cs:22-38`, `Scenes/InstallerPage.tscn:111-117`, `Scenes/ToolsPage.tscn:27-48`.

**Evidence:** `DeleteDirectoryContents` recursively deletes every file and subdirectory below its argument. Its only guards reject a path exactly equal to a filesystem root or the current user-profile path. The install directory is selected through `Tools.PickFolder`, and the Tools page invokes the same deletion for “Clear Installation Folder.” `DeleteOldVersion` invokes it before downloading a new emulator.

**Execution path / trigger:** Select any existing non-root, non-home directory containing unrelated files as the emulator install location, then enable auto-unpack or use the Tools clear action and confirm it.

**Expected behavior:** Only an EmuStack-owned install root and files known to be managed by the selected provider are removed.

**Actual behavior:** The complete selected tree is removed. Documents, custom emulator files, manually placed tools, shortcuts, or other user data inside that directory are not distinguished.

**Impact:** A valid but overly broad user-selected path can cause broad local data loss. The guard also does not establish ownership, canonicalize case-equivalent paths, or reject reparse-point/link boundaries.

**Recommended fix:** Define an app-owned root/marker and refuse destructive actions unless the canonical target is inside it. Prefer a manifest of files written by the operation over recursive “clear everything.” Explicitly reject symlinks/junctions/reparse points and require a second, operation-specific confirmation for clearing an existing non-empty directory.

**Why / tradeoffs:** This preserves the current “choose a folder” UX only for directories that can be proven safe. Existing arbitrary installations would need an explicit adoption/migration step, but that is preferable to silently treating all contents as owned.

**Suggested tests:** Disposable fixture with sibling/unrelated files; path aliases and case variations; root/home/Documents/AppData paths; symlink and junction children; read-only and locked files; failure halfway through deletion.

### F-02 — Mod removal trusts persisted paths and deletes entire mod trees

**Category:** Mod ownership / filesystem safety<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:293-340`, `Scripts/Sources/Mod.cs:4-12`, `Scripts/StandardModManagement.cs:78-105`, `Scripts/ModPaths.cs:26-37`.

**Evidence:** `GetInstalledMods` deserializes `InstalledPath` from `user://InstalledMods.json` without checking that it exists under the current provider's `ModsLocation/<gameId>` root. `DeleteMod` removes the object from in-memory state and calls `FsHelpers.DeleteDirectoryContents(mod.InstalledPath)` followed by `Directory.Delete(mod.InstalledPath, true)`. Directly discovered unmanaged directories are given the same `InstalledPath` treatment. `ModPaths.IsManaged` is only a name-prefix test, not an ownership proof.

**Execution path / trigger:** A stale cross-provider entry, externally edited JSON, moved mod folder, or a local unmanaged mod is selected for deletion.

**Expected behavior:** Only a manager-owned managed folder, or explicitly user-approved unmanaged content, is removed after validating the path against the current provider/game root.

**Actual behavior:** Any path present in the loaded object can be recursively deleted. An unmanaged mod directory is treated as wholly owned even though the manager never created or inventoried its files.

**Impact:** Cross-provider state or malformed local metadata can direct deletion at the wrong directory. Users can lose manual files inside a locally discovered mod folder, and a future metadata corruption bug becomes a destructive primitive.

**Recommended fix:** Store provider/game identity separately from paths, canonicalize and validate containment before every delete, and maintain a per-mod manifest of files created by EmuStack. Treat unmanaged directories as read-only by default; offer a clearly labeled “delete unmanaged folder” action with a path preview.

**Why / tradeoffs:** A manifest is necessary for safe restoration and manual-change detection. It adds metadata and migration work, but avoids trusting a mutable JSON path as authority.

**Suggested tests:** JSON paths outside the current root, paths equal to the root, `..` aliases, cross-provider paths, missing paths, symlinked mod roots, unmanaged folders containing user files, and case-collision paths on Windows.

### F-03 — Mod installation ignores the download result before parsing the file

**Category:** Download correctness / reliability<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/StandardModManagement.cs:16-51`.

**Evidence:** The method checks only the synchronous return from `DownloadRequester.Request`. It then awaits the generic `request_completed` signal without reading its `result`, HTTP status, headers, or body. It immediately opens `downloadPath` and passes it to `ArchiveFactory.OpenArchive`.

**Execution path / trigger:** HTTP failure, non-archive response, redirect/error page, cancellation, disk-full write, or a prior file left at the same sanitized mod name.

**Expected behavior:** A completed request is accepted only after transport status, file existence, size, and archive validity have been checked; any partial/stale file is removed.

**Actual behavior:** A failed request becomes an archive-open exception if no file exists, or can parse whatever file happens to remain at the destination if a stale file survives. The method catches the exception but does not restore any pre-existing state or surface a typed failure.

**Impact:** Users can receive misleading install failures, install stale content, or see a failed network operation change the filesystem. Update workflows can remove the old mod before this failure.

**Recommended fix:** Handle the request completion tuple, delete any old destination before starting, download to a unique `.part` path, validate the result and archive, then atomically rename it into a staging workflow.

**Why / tradeoffs:** Explicit result handling makes network failure deterministic and prevents stale-file reuse. It may require keeping an archive temporarily, but cleanup can be made idempotent.

**Suggested tests:** 404/500, redirect, empty body, HTML body, interrupted download, disk-full simulation, stale destination archive, and two concurrent installs of the same mod.

### F-05 — Emulator installation can erase the working version before replacement succeeds

**Category:** Installation atomicity / data loss<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/Installer.cs:111-160`, `Scripts/Installer.cs:226-239`.

**Evidence:** `InstallVersion` calls `DeleteOldVersion()` before setting up and starting the new `HTTPRequest`. `DeleteOldVersion` recursively clears `ProviderSettings.SaveDirectory` whenever auto-unpack, auto-update, or single-file mode is active. Request-start failure, network failure, invalid archive, extraction failure, or process termination occurs after that deletion.

**Execution path / trigger:** Install/update with an existing emulator and auto-unpack/auto-update enabled, followed by any failure after `DeleteOldVersion` returns.

**Expected behavior:** The old working install remains available until the new release is fully downloaded, verified, deployed, and recorded.

**Actual behavior:** The previous install is removed first; there is no backup, transaction marker, or rollback path.

**Impact:** A transient network outage or malformed release can leave the user with no runnable emulator. A failed deletion is logged but the download continues, so the operation can also proceed against a partially cleared directory.

**Recommended fix:** Download and validate into a sibling staging directory, extract there, verify the expected executable, then perform an atomic directory swap with a retained previous-version backup. Only delete the backup after a successful health check.

**Why / tradeoffs:** A short-lived backup consumes disk space and makes cross-volume/custom-path handling more complex, but it is the appropriate tradeoff for replacing an executable installation.

**Suggested tests:** Failure at request start, HTTP failure, archive corruption, missing executable, locked file, permission error, disk-full, power-loss simulation between each commit step, and retry after a failed update.

### F-06 — `InstalledVersion` is persisted before deployment is known to succeed

**Category:** State synchronization / persistence<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/Installer.cs:163-202`, `Scripts/Installer.cs:354-422`.

**Evidence:** `VersionDownloadCompleted` sets `ProviderSettings.InstalledVersion` and calls `SyncCurrentProviderSettings()` immediately after an acceptable HTTP response, before `UnpackAndSetPermissions()` and `CreateShortcut()`. `UnpackAndSetPermissions` can return after reporting that an executable is missing, can return without extracting when auto-unpack is off, or can throw. It returns no success value.

**Execution path / trigger:** Archive download succeeds but extraction, flattening, executable discovery, rename, permission setting, shortcut creation, or persistence later fails. Also occurs when a user downloads an archive without auto-unpack.

**Expected behavior:** Persist the new version only after a verified executable or explicitly supported single-file artifact is committed and the old state has a recoverable replacement.

**Actual behavior:** Settings can claim the new version while `ExecutablePath` still points to the old version, points to a nonexistent file, or the install directory contains only a partial/new archive.

**Impact:** Launcher mode compares the new version and may skip a needed update, then attempts to launch a stale or missing path. Restarted UI presents false installed state.

**Recommended fix:** Make deployment return a typed result; persist version/path together only after the commit succeeds. For manual-download mode, use a distinct “downloaded” state rather than “installed,” or require the user to confirm manual adoption.

**Why / tradeoffs:** This creates a small explicit state machine (`Downloaded`, `Staged`, `Installed`, `Failed`) and prevents misleading recovery decisions.

**Suggested tests:** Successful single-file install, archive-only download, extraction exception, missing executable, rename conflict, permission failure, launcher restart after each state, and invalid/corrupt persisted provider settings.

### F-07 — Mod updates mutate metadata before filesystem success and ignore install failure

**Category:** Mod lifecycle atomicity<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:500-526`, `Scripts/ModManager.cs:692-704`, `Scripts/StandardModManagement.cs:78-129`.

**Evidence:** `UpdateMod` calls `DeleteMod`, then calls `InstallMod` but does not receive or inspect a result because the `ModManager.InstallMod` wrapper returns `Task` and discards `StandardModManagement.InstallMod`'s boolean. It then calls `SelectGame` and returns `true`. Separately, `StandardModManagement.DeleteMod` removes the mod from `installedMods` and re-adds it to the source list before deleting its directory.

**Execution path / trigger:** Any update where download, archive parsing, extraction, directory move, or cleanup fails after the old directory has been removed.

**Expected behavior:** Keep the old mod until the replacement is staged and validated; commit filesystem and metadata together; report failure if any phase fails.

**Actual behavior:** The old install and metadata can be gone, the source list can be inconsistent, and the caller can treat the update as successful despite install failure.

**Impact:** Users lose a working mod and receive an inaccurate UI/result. Retrying can behave differently because the original `InstalledPath` and list membership have already changed.

**Recommended fix:** Implement one transactional `Update` operation that stages the replacement, validates it, swaps directories, then updates the index. Propagate the boolean/typed result and roll back metadata on every failure.

**Why / tradeoffs:** This removes the convenient reuse of independent delete/install handlers, but it is the smallest design that can preserve the old version and accurately report outcome.

**Suggested tests:** Failure before deletion, after staging, during swap, during cleanup, and during JSON persistence; successful update with removed archive files; retry after each failure; update-all aggregation.

### F-08 — The global installed-mod index crosses provider boundaries

**Category:** Persistence scope / state synchronization<br>
**Severity:** High<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scenes/ModManagerPage.tscn:149-175`, `Scripts/ModManager.cs:53-63`, `Scripts/ModManager.cs:293-340`, `Scripts/Globals.cs:39-63`.

**Evidence:** The scene hard-codes one `user://InstalledMods.json`. `ProviderSettings.ModsLocation` is per provider, but `GetInstalledMods` loads the same `Dictionary<string, List<Mod>>` for every provider and keys only by game ID. Managed directories in the current provider root are skipped during scanning, so the JSON entry is the sole source for them.

**Execution path / trigger:** Install a mod for a title under Eden, switch to Ryubing with the same title ID, or change the provider's mod root, then load/select/delete/update that title.

**Expected behavior:** Each provider/game has an isolated index, and no action can target another provider's files.

**Actual behavior:** Entries from the other provider can be loaded with their old absolute `InstalledPath`; a delete/update can operate on that path while the UI labels it as belonging to the current provider.

**Impact:** Provider switching can produce invisible mods, wrong counts, false installed state, and destructive actions against another provider's mod tree.

**Recommended fix:** Make the persisted key include provider ID and game ID, or store each provider's index separately. Validate every loaded path against the current provider/game canonical root before exposing destructive actions.

**Why / tradeoffs:** Separate indexes mirror the existing provider-specific settings model and simplify migration. A one-time migration must resolve ambiguous old entries conservatively rather than guessing.

**Suggested tests:** Same title under two providers, provider-root changes, switching providers during a load, old JSON entries with absolute paths, and deletion/update attempts after switching.

### F-09 — Resetting or partially rebuilding the index can orphan managed folders

**Category:** Persistence / filesystem reconciliation<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/SettingsPage.cs:30-37`, `Scripts/ModManager.cs:103-107`, `Scripts/ModManager.cs:293-340`.

**Evidence:** `ResetInstalled` replaces `_installedMods` with an empty dictionary and saves JSON; it does not remove or inventory managed directories. On the next scan, `GetInstalledMods` explicitly skips every directory for which `ModPaths.IsManaged` is true. A JSON parse exception is caught, then `SaveInstalledMods` writes the partially reconstructed state over the old file.

**Execution path / trigger:** Use “Reset installed mods,” corrupt/truncate the JSON, or lose metadata during a partial write, then restart or refresh.

**Expected behavior:** Reset should either remove only owned files with explicit confirmation or rebuild a complete inventory and offer recovery.

**Actual behavior:** Managed directories remain on disk but disappear from the application and cannot be removed through the normal UI. Corrupt metadata can be replaced by an incomplete index.

**Impact:** Orphaned mod files consume space, remain active in the emulator, and are no longer manageable; future installs can collide with their names.

**Recommended fix:** Store a manifest in each managed folder or make the filesystem the recoverable source of truth. Quarantine corrupt JSON instead of overwriting it, and define reset semantics explicitly.

**Why / tradeoffs:** Reconciliation may show entries with missing online metadata, but that is safer than silently hiding active files.

**Suggested tests:** Reset with managed/unmanaged folders, truncated/invalid JSON, missing fields, missing directories, duplicate folders, and restart/refresh recovery.

### F-10 — Persistence writes are non-atomic and the legacy-copy migration is unreachable

**Category:** Persistence durability / migration<br>
**Severity:** High for durability; Medium for legacy migration<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ResourceSaveManager.cs:14-18`, `Scripts/ResourceSaveManager.cs:21-69`, `Scripts/ResourceSaveManager.cs:79-97`, `Scripts/ModManager.cs:679-689`.

**Evidence:** `ResourceSaver.Save(this, GetSavePath())` and `File.WriteAllText(_installedModsPath, serializedMods)` write directly to the final files and ignore the ResourceSaver error. There is no temporary file, flush/replace protocol, backup, or journal. `GetSettings` calls `LoadSave()` only inside `if (SaveExists())`; when the current save is absent it creates a default save directly, so `LoadSave`'s `CopyLegacySave()` branch cannot be reached. Version mismatch resets settings instead of applying a schema migration.

**Execution path / trigger:** Power loss, process termination, filesystem error, concurrent close/save, corrupted resource, first launch after a Yuzu Toolbox install, or an app update with an older save version.

**Expected behavior:** Settings and the mod index survive interruption and migrate old schemas/locations without silent loss.

**Actual behavior:** A partial final-file write can make the next launch reset settings or lose mod metadata. The intended legacy save is ignored when no current save exists.

**Impact:** Users can lose paths, release state, source links, and installed-mod metadata without a recoverable backup. The README claim that old installs migrate “where possible” is not fulfilled by this branch.

**Recommended fix:** Write versioned data to a temp file, flush and atomically replace the destination while retaining a backup. Load legacy candidates before deciding that no save exists; add explicit migration functions for each schema.

**Why / tradeoffs:** Atomic replacement and backups add small disk overhead and migration code, but are standard for user-owned configuration and required for crash recovery.

**Suggested tests:** Kill during settings/index write, read-only/full disk, wrong Resource type, null provider array, legacy `.res` present without current save, each schema version, and backup recovery.

### F-11 — Search results can overwrite newer query or game state

**Category:** UI state / asynchronous ordering<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:741-796`.

**Evidence:** `_searchToken` is checked only after the debounce timer. `PerformSearch` captures `gameId`, awaits `BananaManager.SearchMods`, then writes `_searchMods`, hides the loading panel, clears the list, and calls `AddMods(gameId)` without checking whether the token, current game, provider, or scene is still the same.

**Execution path / trigger:** Start a slow search, type a newer query, switch game/provider, refresh, or reload the scene before the first request completes.

**Expected behavior:** Only the latest query for the current scene/game/provider may update the UI.

**Actual behavior:** An older response can replace a newer result or render results for a no-longer-selected game. Exceptions other than `ArgumentException` can also escape the `async void` caller.

**Impact:** Users can install/delete the wrong displayed mod or believe search results belong to the current game.

**Recommended fix:** Use an operation generation containing provider, game, query, and scene lifetime; check it after every await before mutating state/UI. Cancel or ignore superseded HTTP requests.

**Why / tradeoffs:** Results may be discarded when the user navigates, but correctness is more important than displaying stale data.

**Suggested tests:** Delayed responses returned out of order, query replacement, game/provider switch during search, scene reload, and request failure.

### F-12 — Refresh does not reset the browse page counter

**Category:** Pagination correctness<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:343-368`, `Scripts/ModManager.cs:560-574`.

**Evidence:** `LoadNextPage` increments `_modsPage`. `Refresh` clears `_browseMods` and titles but never assigns `_modsPage = 1`; `GetGamesAndMods` then requests the current page for each game.

**Execution path / trigger:** Load page 2 or later, then press Refresh, change the mod location, or change source.

**Expected behavior:** A fresh browse starts at page 1 and rebuilds the complete initial view.

**Actual behavior:** The refreshed view can start at the previously selected page, omitting earlier results and making the load-more count misleading.

**Impact:** Users cannot reliably rediscover or install first-page mods after a refresh; browse state differs based on prior interaction.

**Recommended fix:** Reset the page and per-game pagination/deduplication state at the start of a refresh, and make the page part of the operation state.

**Why / tradeoffs:** This discards the current browse position, which is expected for a refresh; preserving it should be an explicit feature.

**Suggested tests:** Refresh from pages 1, 2, and 5; switch games; change mod root; source refresh; duplicate-page responses.

### F-13 — Title parsing checks for two lines but reads a third

**Category:** Input parsing / startup reliability<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:402-445`.

**Evidence:** After `gameCleaned.Split("\n")`, the code checks `if (gameSplit.Length < 2)` and then reads `gameSplit[1]` and `gameSplit[2]`. A response with exactly two lines passes the guard and throws `IndexOutOfRangeException` in the signal callback.

**Execution path / trigger:** Switchbrew returns a shortened table row, changed markup, blank lines are normalized differently, or a partial HTML response reaches `GetTitles`.

**Expected behavior:** Malformed rows are skipped or reported without terminating the title-load workflow.

**Actual behavior:** The callback can throw from an `async`-initiated startup flow; the loading state may remain inconsistent and the user may lose title discovery.

**Recommended fix:** Check `Length < 3`, parse HTML structurally with a tolerant parser, validate the ID/name fields, and isolate malformed rows instead of aborting the whole response.

**Why / tradeoffs:** A structural parser is more code than string splitting but avoids dependence on whitespace and table layout.

**Suggested tests:** Empty rows, two-line rows, changed tags, HTML entities, network error pages, and valid rows mixed with malformed rows.

### F-14 — Windows shortcut creation is disabled despite an implementation and documentation

**Category:** Platform behavior / UI correctness<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/Installer.cs:50-54`, `Scripts/ShortcutFactory.cs:8-24`, `Scripts/ShortcutFactory.cs:80-105`, `README.md:12-20`.

**Evidence:** `Installer.Initiate` assigns `_createShortcutButton.Disabled = _os == OsKind.Windows`. `ShortcutFactory` contains a Windows `.lnk` implementation, and the README says Windows shortcuts are supported.

**Execution path / trigger:** Run on Windows and open the installer page.

**Expected behavior:** The Windows shortcut checkbox is available when the platform supports it, with the appropriate privilege warning.

**Actual behavior:** The control is disabled on the very platform for which `CreateWindows` exists. The Windows shortcut path is unreachable through the UI.

**Impact:** A documented feature is unavailable and Windows auto-update shortcut workflows cannot be configured.

**Recommended fix:** Disable only unsupported platforms, likely macOS, and separately communicate elevation/fallback behavior. Add a UI-to-factory integration test per OS.

**Why / tradeoffs:** The existing implementation may still need privilege/error fixes (F-15), but restoring access is a small, low-risk correction.

**Suggested tests:** Platform-specific initializer state, Windows Start Menu success/failure/fallback, and auto-update shortcut argument persistence.

### F-15 — Linux shortcut creation can silently fail for paths with spaces or failed privilege elevation

**Category:** External process / platform integration<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ShortcutFactory.cs:27-77`.

**Evidence:** The generated desktop entry writes `Exec={executable} {launcherFlag}` and `Icon={iconPath}` without desktop-entry escaping. The `pkexec` call uses one interpolated `Arguments` string (`mv {tempShortcutPath} {shortcutPath}`), so a save path containing spaces is split into multiple arguments. `WaitForExit()` is called but its exit code is ignored; fallback is only entered for a thrown exception.

**Execution path / trigger:** Use an install path containing spaces, cancel/deny `pkexec`, or run where `/usr/share/applications` exists but cannot be modified.

**Expected behavior:** The shortcut is correctly escaped and a non-zero privilege-helper result creates a usable user-local fallback or a clear failure.

**Actual behavior:** The desktop entry may not launch, and an unsuccessful `pkexec` can be treated as completion while leaving the temporary file behind.

**Impact:** The user receives no reliable shortcut and may believe auto-update/launch integration is configured.

**Recommended fix:** Use argument-list APIs, desktop-entry escaping/quoting rules, check exit status, clean temp files, and prefer `$XDG_DATA_HOME/applications` before requesting root access.

**Why / tradeoffs:** User-local installation reduces privilege prompts and is safer, but may change where the shortcut appears on some desktops.

**Suggested tests:** Paths with spaces/Unicode, `pkexec` denied/non-zero, missing system directory, desktop-entry parser validation, and launch with/without `--launcher`.

## 11. Highly Likely Bugs and Edge Cases

### F-04 — Downloads are written directly to final paths without integrity or atomic replacement

**Category:** Download security / reliability<br>
**Severity:** High<br>
**Confidence:** High for the missing controls; Medium for each resulting failure mode<br>
**Status:** Risk<br>
**Affected:** `Scripts/Installer.cs:148-182`, `Scripts/StandardModManagement.cs:20-64`, `Scripts/Modes/ProviderRelease.cs:1-8`, `Scripts/Modes/Ryujinx.cs:104-113`.

**Evidence:** `HTTPRequest.DownloadFile` is set to a final install/archive path. There is no `.part` path, checksum/signature field, content-type/size policy, archive allowlist, or atomic rename. Release responses supply URLs and filenames but no integrity metadata. The installer checks HTTP result but leaves any partial final file in place; the mod path does not check result at all (F-03).

**Execution path / trigger:** Interrupted download, disk-full condition, server returning HTML/error content, changed release asset, compromised/incorrect endpoint, or retry after a partial file.

**Expected behavior:** A download is isolated until it passes transport, size, integrity, and format checks; failed artifacts are removed.

**Actual / likely behavior:** Partial or unexpected content can remain at a path later treated as an archive/executable, and a remote server can replace an artifact without the app detecting it.

**Impact:** Corrupt installs, stale reuse, misleading version state, and a larger supply-chain trust boundary.

**Recommended fix:** Add per-release checksum/signature metadata, download to a unique temporary file, enforce maximum size/content type, validate archive structure, and atomically commit only after verification.

**Why / tradeoffs:** Providers must expose trustworthy digests/signatures or the UI must label verification unavailable. This is a deliberate product/security dependency rather than an implementation detail.

**Suggested tests:** Partial files, wrong checksum, wrong content type, oversized archive, server replacement, resume/cancel, and retry cleanup.

### F-16 — Archive extraction does not enforce a safe-entry policy

**Category:** Archive security / filesystem escape / resource exhaustion<br>
**Severity:** High; potentially Critical if the library permits escape<br>
**Confidence:** Medium<br>
**Status:** Risk<br>
**Affected:** `Scripts/StandardModManagement.cs:35-50`, `Scripts/Installer.cs:386-399`, SharpCompress 0.49.1 usage in `EmuStack.csproj:8`.

**Evidence:** Both extraction paths iterate archive entries and call `WriteToDirectory`/`WriteEntryToDirectory` with `ExtractFullPath = true` and `Overwrite = true`. The repository does not canonicalize each entry against the staging root, reject absolute or `..` paths, reject symlinks/junctions/reparse points, cap file count/uncompressed bytes, or define duplicate/conflicting-entry behavior.

**Execution path / trigger:** Install a malicious GameBanana archive or a compromised provider release containing traversal, link, duplicate, very large, or deeply nested entries.

**Expected behavior:** Every extracted path remains inside a private staging root, links are rejected or handled as inert data, and resource limits are enforced before commit.

**Actual / likely behavior:** The application delegates all such decisions to the package. If the package version does not fully reject a class of entry, the app can write outside the intended root or exhaust disk/memory. This was not runtime-tested because .NET/Godot and the restored package are unavailable.

**Impact:** Potential arbitrary local file modification, overwrite of unrelated files, denial of service, or partial install.

**Recommended fix:** Add an explicit archive preflight layer independent of library defaults; use canonical containment checks, reject links and collisions, enforce limits, and extract only into a disposable same-volume staging directory.

**Why / tradeoffs:** Package defaults can change across upgrades; an app-level policy makes the safety boundary reviewable. Limits may reject legitimate large mods, so expose clear diagnostics and configurable safe ceilings.

**Suggested tests:** Zip-slip (`../`), absolute paths, Windows drive/UNC paths, backslash separators, symlink/junction entries, duplicate names with different case, large declared sizes, many files, nested archives, corrupt archives, and interrupted extraction.

### F-17 — Scene reloads and dialogs have no cancellation/lifetime protocol

**Category:** Godot lifecycle / async state<br>
**Severity:** High<br>
**Confidence:** Medium<br>
**Status:** Highly Likely<br>
**Affected:** `Scripts/Home.cs:113-117`, `Scripts/ModManager.cs:73-100`, `Scripts/Installer.cs:72-202`, `Scripts/Tools.cs:79-112`, scene `HTTPRequest`/Timer nodes.

**Evidence:** Provider changes call `ReloadCurrentScene` while `Installer`, `ModManager`, and `Tools` have pending `await` continuations. `ModManager` awaits an `HTTPRequest` signal and attaches a root close handler; `StandardModManagement` awaits a scene `HTTPRequest`; `Tools` stores confirmation/folder-picker `TaskCompletionSource` objects but does not complete them in `_ExitTree`. No cancellation token, operation generation, or lifecycle disposal path exists.

**Execution path / trigger:** Switch provider, close/reload the scene, or navigate while a request, dialog, download, or two-second success timer is active.

**Expected behavior:** Work is canceled or detached, awaiters complete with cancellation, and no old page writes current UI/state.

**Actual / likely behavior:** A request signal can never arrive after its node is freed; a confirmation task can wait forever; an old HTTP continuation can call stale controls or `Tools.Instance`; a root close handler can retain an old controller or save stale state.

**Impact:** Stuck actions, stale provider data, unobserved exceptions, duplicate close saves, and partial operations during scene changes.

**Recommended fix:** Introduce an app-operation coordinator with cancellation tokens and generation IDs. Cancel in `_ExitTree`, disconnect root events explicitly, complete dialogs as canceled, and guard every post-await UI/state mutation.

**Why / tradeoffs:** Cancellation needs provider/download APIs to accept tokens and may require careful cleanup, but it is the only predictable behavior for scene-owned async work.

**Suggested tests:** Provider switch during each network/dialog/download phase, scene reload during extraction, close during install, canceled confirmation/folder picker, and late signal delivery.

### F-18 — Fire-and-forget handlers can leave loading state or exceptions unresolved

**Category:** Error handling / UI reliability<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Highly Likely<br>
**Affected:** `Scripts/ModManager.cs:110-164`, `Scripts/ModManager.cs:228-250`, `Scripts/ModManager.cs:343-368`, `Scripts/ModManager.cs:741-796`, `Scripts/Installer.cs:163-202`, `Scripts/Tools.cs:135-153`.

**Evidence:** Many signal handlers are `async void`. `GetAvailableMods` catches only `ArgumentException`; `LoadNextPage` has no `try/finally`; `PerformSearch` catches only `ArgumentException`; `VersionDownloadCompleted` has no outer exception boundary around extraction/shortcut/launch. Controls and loading panels are reset only on selected paths. `Tools.AddError` defers an unguarded `_errorConsole` write before its later validity check.

**Execution path / trigger:** Network exception, JSON parse error, archive/permission error, freed node, or process/shortcut failure.

**Expected behavior:** Every operation reports one terminal state, restores controls, and preserves the exception with contextual logging.

**Actual / likely behavior:** The exception is delivered to the engine's unhandled-error path, the spinner remains visible, controls remain disabled, or a successful-looking state is left behind.

**Impact:** Users cannot recover without restarting, and the error console may not contain the root cause.

**Recommended fix:** Return `Task<OperationResult>` from handlers where possible, catch at the signal boundary, use `try/finally` for UI state, and centralize structured error reporting with object validity checks before deferred callbacks.

**Why / tradeoffs:** This removes convenient `async void` signal handlers but makes failures testable and observable.

**Suggested tests:** Inject exceptions at each await and filesystem phase; assert loading/control state and persisted metadata after each failure.

### F-19 — Destructive operations are not serialized across buttons or repeated clicks

**Category:** Concurrency / re-entry<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Highly Likely<br>
**Affected:** `Scripts/Installer.cs:72-107`, `Scripts/ModManager.cs:472-497`, `Scripts/ModManager.cs:824-895`, `Scripts/ModManager.cs:1016-1023`, relevant scene buttons.

**Evidence:** The installer awaits confirmation before disabling the download button, so repeated clicks can create multiple confirmations/operations. `UpdateAll` never calls `DisableInteraction`; `ClearModsPressed` confirms and invokes an `async void` `ClearMods` without awaiting or locking. There is no per-provider/game/mod operation lock.

**Execution path / trigger:** Double-click Download, press Update All repeatedly, update while selecting/deleting a mod, or clear while another operation is active.

**Expected behavior:** One operation owns a provider/game/mod target until it completes or cancels.

**Actual / likely behavior:** Requests share one `HTTPRequest`, same archive/directory paths, and mutable `_activeDownloadRelease`/lists. Interleaved operations can overwrite files, duplicate list entries, or persist a later stale snapshot.

**Impact:** Corrupt installs, duplicate metadata, wrong UI selection, and difficult-to-reproduce data loss.

**Recommended fix:** Disable/lock before awaiting confirmation, add a per-root operation gate, reject same-target re-entry, and make all destructive commands awaitable.

**Why / tradeoffs:** Serializing operations reduces peak throughput but file updates are inherently serialized per root; unrelated providers/games can remain parallel if their roots are isolated.

**Suggested tests:** Double-click and rapid-click tests, update-all plus manual action, two games concurrently, provider switch during update, and cancellation/failed retry.

### F-20 — Custom version and executable-name input reaches filesystem paths without validation

**Category:** Input validation / filesystem safety<br>
**Severity:** High if path escape is exploitable; otherwise Medium<br>
**Confidence:** Medium<br>
**Status:** Risk<br>
**Affected:** `Scripts/Installer.cs:81-100`, `Scripts/Installer.cs:425-441`, `Scripts/Modes/Ryujinx.cs:70-74`, `Scripts/Modes/Ryujinx.cs:300-324`, `Scripts/Modes/Mode.cs:64-72`.

**Evidence:** Custom Ryubing versions are only trimmed and have a leading `v` removed. `GetConventionAssetName` interpolates the value into `ProviderRelease.FileName`; `Installer` then uses it in `Path.Join(ProviderSettings.SaveDirectory, _downloadFileName)`. The custom executable name is interpolated into another path in `RenameExecutableIfRequested` without invalid-character, separator, or containment validation.

**Execution path / trigger:** Enter a version/name containing separators, `..`, drive/UNC syntax, invalid characters, or a very long value.

**Expected behavior:** Inputs are constrained to a documented version grammar/name grammar and cannot alter the target root.

**Actual / likely behavior:** The generated download/rename path can contain traversal or invalid path components. Whether Godot creates every intermediate path for a crafted `DownloadFile` value needs runtime verification, but the repository does not prevent the construction.

**Impact:** Failed installs, writes outside the intended install directory where path resolution permits it, or overwriting an unexpected file selected by the resulting path.

**Recommended fix:** Validate custom versions against provider-specific syntax, generate a safe filename from a parsed version, use `Path.GetFullPath` plus root containment, and reject all separators in executable names.

**Why / tradeoffs:** This may reject provider tags that are not semantic versions; each provider can expose an explicit safe tag grammar rather than accepting arbitrary path syntax.

**Suggested tests:** `..`, absolute/UNC paths, slash/backslash, Unicode normalization, reserved Windows names, trailing dots/spaces, very long values, and valid pre-release/build tags.

### F-21 — macOS `.app` bundles are found as directories but launched as executable paths

**Category:** Platform integration<br>
**Severity:** Medium<br>
**Confidence:** Medium<br>
**Status:** Highly Likely<br>
**Affected:** `Scripts/Modes/Mode.cs:104-143`, `Scripts/Tools.cs:56-69`, `Scripts/Installer.cs:354-378`.

**Evidence:** On macOS, `GetExecutableCandidates` returns names ending in `.app`, and `FindExecutable` returns the bundle directory. `Tools.LaunchEmulator` calls `Process.Start(new ProcessStartInfo(executablePath))` directly. Eden DMGs are deliberately left mounted manually and `ExecutablePath` is set to the `.dmg` file.

**Execution path / trigger:** Install a macOS tar/app bundle or run launcher mode after a bundle path has been discovered.

**Expected behavior:** Launch Services opens the app bundle, or the inner executable is resolved and launched with the correct working directory.

**Actual / likely behavior:** Direct process creation may reject a bundle directory or try to execute the wrong object; a DMG path is not a runnable emulator executable. This requires a macOS runtime check.

**Impact:** macOS users can download an apparent release but fail to launch it, especially in launcher mode.

**Recommended fix:** Use `/usr/bin/open --` for `.app` bundles or resolve `Contents/MacOS/<binary>` after validating the bundle; model DMG mounting/copying as an explicit unsupported/manual state rather than an executable path.

**Why / tradeoffs:** Launch Services handles signing/quarantine and bundle semantics better; direct inner-binary launch can bypass those behaviors and should be used only when intentional.

**Suggested tests:** Intel/ARM macOS, `.app` with spaces, bundle with missing inner binary, DMG manual flow, launcher mode, and process exit/error reporting.

### F-22 — Save backup/restore accepts unsafe source/destination relationships and is not transactional

**Category:** User-file safety / backup correctness<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Risk<br>
**Affected:** `Scripts/ToolsPage.cs:41-72`, `Scripts/FsHelpers.cs:72-90`.

**Evidence:** `DuplicateDirectoryContents` creates the destination, recursively enumerates the source, then copies files with overwrite. It does not reject source==destination, destination inside source, source/destination on different or linked roots, or reparse points. Restore overwrites matching files but never removes files that are absent from the backup. There is no temporary snapshot or rollback.

**Execution path / trigger:** Choose the same folder, a child folder, a linked folder, a read-only destination, or restore after files have been removed from the backup.

**Expected behavior:** Backup is a point-in-time snapshot; restore is deliberate, complete, and recoverable.

**Actual / likely behavior:** Same/contained paths can self-copy or recurse; partial copies leave mixed data; restore leaves stale files and can partially overwrite saves after an error.

**Impact:** Save data can be duplicated unexpectedly, corrupted by a partial restore, or remain inconsistent with the selected backup.

**Recommended fix:** Canonicalize and reject overlapping roots, snapshot to a temp directory, copy with explicit file lists and link handling, fsync/atomic-swap the backup, and require a backup of the current save before destructive restore. Decide explicitly whether restore removes extraneous files.

**Why / tradeoffs:** A complete restore may remove user-created files, so the UI must show a manifest/diff and offer a safe merge mode.

**Suggested tests:** Same/parent/child roots, links, empty files, extensionless files, locked/read-only files, interrupted copy, stale destination files, and restore rollback.

### F-23 — Network operations lack a uniform timeout, cancellation, retry, and disposal policy

**Category:** Network reliability / resource management<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Risk<br>
**Affected:** `Scripts/Globals.cs:16-17`, `Scripts/Sources/BananaManager.cs:121-263`, `Scripts/Modes/Eden.cs:39-87`, `Scripts/Modes/Ryujinx.cs:86-208`, HTTPRequest nodes in `Scenes/InstallerPage.tscn` and `Scenes/ModManagerPage.tscn`.

**Evidence:** The shared `HttpClient` has no configured timeout. GameBanana, Switchbrew, Eden, and the update server use direct `GetAsync` calls; only some Forgejo calls create a 15-second cancellation source. HTTPRequest nodes have no repository-level timeout/cancel policy. Responses are not disposed in `BananaManager`, and archive readers are not explicitly disposed in the extraction methods.

**Execution path / trigger:** Offline server, stalled connection, rate limiting, large response, scene reload, or repeated refresh/search.

**Expected behavior:** Each operation has a bounded lifetime, cancellation, retry/backoff where appropriate, response disposal, and a terminal UI state.

**Actual / likely behavior:** The UI can remain loading indefinitely; repeated requests accumulate; scene reload cannot reliably stop work; resource cleanup depends on GC/library behavior.

**Recommended fix:** Define per-operation timeouts and cancellation tokens, use request/response disposal, bounded retries for idempotent reads, and explicit cancellation on scene exit.

**Why / tradeoffs:** Retrying downloads can increase load and must not duplicate writes; separate read/download policies keep behavior predictable.

**Suggested tests:** DNS failure, connect timeout, stalled body, 429/5xx, cancellation during every await, repeated refresh, and response/stream disposal checks.

## 12. Architecture and Maintainability Concerns

### F-24 — The mod model has no reversible deployment, dependency, conflict, profile, or load-order representation

**Category:** Functional scope / extensibility<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Maintainability Concern<br>
**Affected:** `Scripts/Sources/Mod.cs:4-12`, `Scripts/StandardModManagement.cs:16-129`, `Scripts/ModManager.cs`, `Scenes/ModManagerPage.tscn`.

**Evidence:** `Mod` has no enabled state, profile, dependency constraints, conflict declarations, load-order key, file manifest, original-file backup, or deployment transaction ID. A mod is installed as a directory and removed as a directory. Repository search found no dependency/conflict/profile/rollback/enable/disable implementation.

**Execution path / trigger:** Install two mods that contain the same relative file, disable one, change load order, switch profiles, or update a dependency.

**Expected behavior:** The manager should be able to calculate a deterministic winner, explain conflicts/dependencies, enable/disable without losing files, and restore the previous file version.

**Actual behavior:** Both directories can exist with behavior delegated to emulator filesystem ordering; the UI has no model for why a file wins or how to restore it. Enable/disable, profiles, dependency resolution, and conflict resolution are absent.

**Impact:** The current feature set cannot safely support common mod-manager semantics, and adding them later will require changing the DTO, persistence, UI, and deployment code together.

**Recommended fix:** Define a small domain model first: stable mod identity/version, profile membership, dependency constraints, conflict records, deterministic load order, and per-deployment file ownership. Keep the resolver pure and make filesystem deployment consume its plan.

**Why / tradeoffs:** This is a real boundary needed for correctness, not abstraction for its own sake. It can start with a single active profile and explicit conflict errors before supporting richer presets.

**Suggested tests:** Two mods sharing a file, dependency missing/circular/incompatible, deterministic ordering, disable/enable restoration, profile switch with missing mod, and manual file modification detection.

### F-25 — Mod identity and managed-folder detection are ambiguous

**Category:** Data modeling / UI correctness<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** `Scripts/ModManager.cs:264-289`, `Scripts/ModManager.cs:371-395`, `Scripts/ModManager.cs:824-855`, `Scripts/ModPaths.cs:15-37`, `Scripts/StandardModManagement.cs:116-129`.

**Evidence:** `ModKey` uses `ModId` when present but lowercased/truncated name otherwise. Item metadata stores only `mod.ModName`, and click/update/delete handlers find rows by that name. `ModPaths.IsManaged` returns `folderName.StartsWith("Managed")` with no separator or ownership marker. `Sanitize` can map distinct names to the same folder name.

**Execution path / trigger:** Two mods share a display name, a local unmanaged folder has a name beginning with `Managed`, two remote names sanitize to the same value, or a local mod later links to a remote ID.

**Expected behavior:** A stable immutable identity maps one UI row to one object and one unique on-disk directory; unmanaged folders remain visible.

**Actual behavior:** The wrong same-named mod can be acted on, a user folder such as `ManagedSettings` can be hidden, and distinct names can collide or overwrite the same managed path.

**Impact:** Wrong install/delete/update actions and invisible or overwritten mod content.

**Recommended fix:** Use stable row/object IDs, store row metadata as an ID rather than display text, normalize identifiers explicitly, and use a manifest/schema marker (for example a metadata file) instead of a broad prefix heuristic. Detect sanitized-name collisions before download.

**Why / tradeoffs:** Display names remain user-facing while identity becomes deterministic. Existing folders need a migration scan and collision report.

**Suggested tests:** Duplicate names with different IDs, case/Unicode variants, invalid-character collisions, `Managed*` unmanaged folders, and linked local mods.

### F-26 — UI controllers and global services own too many unrelated responsibilities

**Category:** Architecture / testability<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Maintainability Concern<br>
**Affected:** `Scripts/ModManager.cs` (1,027 lines), `Scripts/Installer.cs` (512 lines), `Scripts/Globals.cs`, `Scripts/Tools.cs`, `Scripts/StandardModManagement.cs`.

**Evidence:** `ModManager` owns HTTP title parsing, source browsing, search debounce, game discovery, JSON serialization, UI list rendering, confirmations, and calls into filesystem-changing mod operations. `Installer` owns release selection, HTTP download, deletion, extraction, executable discovery/rename, permissions, shortcut selection, and launcher mode. Both reach `Globals.Instance` and `Tools.Instance` directly.

**Execution path / trigger:** Add a new provider, change persistence format, add a transaction/rollback rule, or test an install failure without a running scene tree.

**Expected behavior:** Domain planning, persistence, filesystem effects, provider/network access, and UI orchestration have explicit interfaces and independently testable boundaries.

**Actual behavior:** An unrelated feature often requires changing a large scene controller; failure handling and state transitions are spread across callbacks and helper methods.

**Impact:** High regression probability, difficult unit testing, and unclear ownership of state/cleanup.

**Recommended fix:** Extract pure models/planners, a persistence repository, a safe filesystem/deployment service, and provider/source clients. Keep Godot controllers as thin adapters that render operation results.

**Why / tradeoffs:** The provider `Mode` abstraction can remain; only the destructive/stateful boundaries need separation. Incremental extraction is safer than a broad rewrite.

**Suggested tests:** Pure provider/version/path tests, repository round trips/migrations, deployment fixture tests, and scene-level smoke tests with fake clients.

## 13. Performance and Reliability Concerns

### F-27 — Large file and network operations run synchronously or scale poorly

**Category:** Performance / reliability<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Maintainability Concern<br>
**Affected:** `Scripts/StandardModManagement.cs:35-64`, `Scripts/Installer.cs:386-405`, `Scripts/ModManager.cs:138-147`, `Scripts/ModManager.cs:293-340`, `Scripts/Sources/BananaManager.cs:121-202`, `Scripts/ToolsPage.cs:41-72`.

**Evidence:** Archive extraction, recursive copy/delete, flattening, and executable search occur on the Godot thread. Each GameBanana browse page can perform one Subfeed request plus a Files request for each record, sequentially. `GetInstalledMods` reads the entire JSON file and then writes it once per discovered game. Browse pages use `AddRange` without a general deduplication pass.

**Execution path / trigger:** Large mod archives/save trees, many configured games, high-result GameBanana pages, or repeated refresh/search/update-all.

**Expected behavior:** Long work is cancellable and off the UI thread where safe; network requests are bounded and deduplicated; persistence is loaded/saved once per operation.

**Actual / likely behavior:** Frame processing freezes during file work, discovery latency grows with the number of mods/games, and duplicate data can grow in memory/list rendering.

**Impact:** Apparent hangs, high memory/disk pressure, and increased exposure to re-entry while users click because the UI does not respond.

**Recommended fix:** Move pure archive/file planning and bounded I/O to cancellable workers, marshal only results to the main thread, batch title/index load/save, cache source metadata with expiry, and deduplicate by stable ID.

**Why / tradeoffs:** File commits still need a serialized safe path; only scanning, hashing, and preflight need parallelism. Cache invalidation must be explicit.

**Suggested tests:** Large archives, thousands of files, 100+ games/mods, repeated pages, memory/handle counts, frame-time during extraction, and cancellation latency.

### F-30 — Unsupported platforms silently use macOS behavior and packaging policy is incomplete

**Category:** Platform compatibility / release engineering<br>
**Severity:** Low<br>
**Confidence:** High for OS fallback and export settings; Medium for end-user packaging impact<br>
**Status:** Risk<br>
**Affected:** `Scripts/Os.cs:7-17`, `export_presets.cfg`, `README.md:1-20`.

**Evidence:** `Os.Parse` maps every OS name other than exactly `Windows` or `Linux` to `MacOS`. Export presets target Windows/Linux x86_64 and a universal macOS bundle. The macOS preset has empty signing identity/team fields and `notarization/notarization=0` (`export_presets.cfg:192-222`), and the README says Godot 4.6 while `project.godot`/csproj target 4.7.

**Execution path / trigger:** Run on an unexpected Godot platform name or distribute the macOS artifact without signing/notarization; maintainers follow stale README version guidance.

**Expected behavior:** Unsupported platforms fail explicitly; supported-platform packaging requirements and engine version are documented accurately.

**Actual / likely behavior:** An unsupported target is treated as macOS and may request macOS release assets/paths. macOS distribution may encounter Gatekeeper/quarantine friction, and documentation can lead to mismatched tooling.

**Impact:** Confusing failures on unsupported systems and avoidable distribution/support problems.

**Recommended fix:** Use an explicit supported-platform switch with an error for unknown values, document the exact engine/runtime version, and define signing/notarization or an explicit supported sideload flow for macOS.

**Why / tradeoffs:** A hard failure is clearer than selecting the wrong provider asset. Signing may require release credentials and should be handled in packaging automation.

**Suggested tests:** OS parser table tests, export smoke tests for each preset, macOS signed/unsigned launch checks, and README/version consistency check.

## 14. Testing and Observability Gaps

### F-28 — There are no automated tests, fixtures, CI checks, or safety regression harness

**Category:** Testing / release confidence<br>
**Severity:** High for a destructive file manager; Medium as a code-quality classification<br>
**Confidence:** High<br>
**Status:** Confirmed<br>
**Affected:** Repository-wide; `EmuStack.csproj`, `.gitignore`, `.github/`.

**Evidence:** No test/spec/fixture files or test project are present. `.github` contains only issue templates; no workflow was found. The project has no analyzer/formatter configuration or test command documented in `CONTRIBUTING.md` (one line). The required Godot/.NET runtime is also absent in this review environment.

**Execution path / trigger:** Any change to archive extraction, delete/restore, persistence, provider selection, scene reload, or C# signal wiring.

**Expected behavior:** Destructive logic has isolated fixture tests and each release runs build/import/smoke checks on supported platforms.

**Actual behavior:** The highest-risk paths can regress without a reproducible automated signal; only static inspection and manual testing can reveal breakage.

**Impact:** The current code cannot credibly demonstrate safety under partial failure, platform differences, or Godot lifecycle events.

**Recommended fix:** Add a test project for pure models/path validation/version logic; disposable filesystem/archive fixtures; fake HTTP clients; persistence migration tests; Godot headless scene smoke tests; and CI build/import checks for Windows/Linux/macOS targets where available.

**Why / tradeoffs:** Fixture tests are cheaper and safer than testing real user folders. A small first suite covering deletion boundaries and transactional failure gives the highest value.

**Suggested tests:** All tests listed in F-01–F-23, plus C# scene signal/exported-property smoke tests, provider asset selection, JSON/resource compatibility, and launcher argument tests.

### F-29 — Errors are not structured around operation, provider, game, mod, and path context

**Category:** Observability / error recovery<br>
**Severity:** Medium<br>
**Confidence:** High<br>
**Status:** Maintainability Concern<br>
**Affected:** `Scripts/Tools.cs:135-176`, `Scripts/StandardModManagement.cs:68-73`, `Scripts/StandardModManagement.cs:106-110`, `Scripts/Installer.cs:103-107`, `Scripts/ModManager.cs:333-337`, `Scripts/Modes/Ryujinx.cs:188-207`.

**Evidence:** Errors are appended as free-form strings, often with full exception text, while several provider/network catches return empty/default values. There is no operation ID, typed error category, retryability flag, target path, provider/game/mod identity, or persisted operation journal. `Tools.FormatError` truncates to 1,200 characters after optional HTML stripping.

**Execution path / trigger:** Partial download, archive failure, provider fallback, missing path, save failure, or an async exception after the UI has moved on.

**Expected behavior:** The user sees a concise actionable message, the console/log retains structured diagnostic context, and the operation state says whether retry is safe.

**Actual / likely behavior:** Different failures look alike (“failed to install”), some are silently converted to empty results, and truncation can remove the useful path/inner exception. Full path/exception text can also expose more local detail than necessary in a user-visible console.

**Impact:** Poor supportability, unsafe retries, and difficulty distinguishing “nothing found” from “network failed” or “state is inconsistent.”

**Recommended fix:** Define an operation result/error type with safe user message plus diagnostic fields, log target/provider/game/mod and phase, preserve inner exceptions in a file log, and classify retryable/permanent/partial outcomes.

**Why / tradeoffs:** Structured logging requires a small shared model, but it reduces duplicated catch logic and improves recovery decisions.

**Suggested tests:** Assert error classification/context for network, permission, lock, malformed archive, invalid persistence, cancellation, and partial commit failures.

## 15. Prioritized Findings Table

Priority meanings: P0 = safety/release blocker; P1 = correctness/recovery; P2 = architecture/reliability; P3 = polish/documentation.

| ID | Finding | Severity | Confidence | Category | Estimated scope | Recommended priority |
|---|---|---:|---:|---|---|---:|
| F-01 | Destructive directory clearing has no application-owned scope | High | High | Filesystem safety | Shared helper plus installer/tools callers | P0 |
| F-02 | Mod removal trusts persisted paths and deletes whole trees | High | High | Ownership/data loss | Mod persistence and delete service | P0 |
| F-03 | Mod installation ignores download result | High | High | Download correctness | Localized mod installer change | P0 |
| F-04 | Downloads lack staging, integrity, and atomic replacement | High | High/Medium | Download security | Provider metadata plus shared download service | P0 |
| F-05 | Emulator update erases old version before replacement succeeds | High | High | Installation atomicity | Installer transaction | P0 |
| F-06 | Installed version is recorded before deployment succeeds | High | High | State synchronization | Installer result/state model | P0 |
| F-07 | Mod update mutates state before commit and ignores install failure | High | High | Mod lifecycle | Transactional update path | P0 |
| F-08 | Global mod index crosses provider boundaries | High | High | Persistence scope | Schema and provider-switch migration | P0 |
| F-09 | Reset/corrupt index can orphan active managed folders | Medium | High | Recovery | Index reconciliation and reset semantics | P1 |
| F-10 | Persistence is non-atomic and legacy migration is bypassed | High/Medium | High | Durability/migration | Resource and JSON repositories | P0/P1 |
| F-11 | Stale search results overwrite newer state | Medium | High | Async UI state | Search operation generation | P1 |
| F-12 | Refresh does not reset pagination | Medium | High | Browse correctness | Small controller fix plus tests | P2 |
| F-13 | Title parser can throw on a two-line response | Medium | High | Input parsing | Small parser fix | P1 |
| F-14 | Windows shortcut control disables supported feature | Medium | High | Platform UI | Small initializer/docs fix | P2 |
| F-15 | Linux shortcuts are unescaped and ignore privilege failure | Medium | High | Process integration | Shortcut writer/service | P2 |
| F-16 | Archive entry/link/resource policy is absent | High/Potential Critical | Medium | Archive security | Shared preflight extractor | P0 |
| F-17 | Scene reload/dialog/download lifecycle has no cancellation | High | Medium | Godot lifecycle | App operation coordinator | P0/P1 |
| F-18 | Fire-and-forget handlers can strand UI and errors | Medium | High | Error handling | All async signal handlers | P1 |
| F-19 | Update/repeated actions are not serialized | Medium | High | Concurrency | Per-root operation gate and UI lock | P1 |
| F-20 | Custom version/name strings reach filesystem paths | High/Medium | Medium | Input/filesystem safety | Provider validation and path helper | P1 |
| F-21 | macOS `.app` launch path is likely incorrect | Medium | Medium | Platform runtime | macOS launch adapter | P2 |
| F-22 | Save backup/restore is unsafe and non-transactional | Medium | High | User-file safety | Copy/restore service and UX | P1 |
| F-23 | Network timeout/cancel/retry/disposal is inconsistent | Medium | High | Network reliability | Shared client policy | P1 |
| F-24 | No dependency/conflict/profile/enable/load-order model | Medium | High | Extensibility | Domain model and deployment plan | P2 |
| F-25 | Mod identity and managed-folder detection are ambiguous | Medium | High | Data/UI correctness | Identity and manifest migration | P1 |
| F-26 | Controllers/global services are over-centralized | Medium | High | Architecture/testability | Incremental service extraction | P2 |
| F-27 | File/network work scales poorly and blocks the main thread | Medium | High | Performance | Worker/preflight and batching | P2 |
| F-28 | No automated tests, fixtures, CI, or safety harness | High as a release gate | High | Testing | New test/CI surface | P0 |
| F-29 | Error reporting lacks structured operation context | Medium | High | Observability | Shared result/log model | P2 |
| F-30 | Unknown OS falls through to macOS; docs/package policy drift | Low | High/Medium | Platform/release | Small platform/docs/release changes | P3 |

## 16. Recommended Remediation Plan

### Immediate safety fixes (P0)

1. Stop recursive deletion of arbitrary user-selected paths. Require a canonical, app-owned root and reject links/reparse points. Make unmanaged mod folders read-only to the manager.
2. Add a single safe path/ownership validator used by every delete, move, extract, and rename operation. Do not trust `InstalledPath` from JSON.
3. Change all downloads to unique temporary files. Check request result/status, remove stale artifacts, enforce size/type limits, and verify a provider-supplied checksum/signature where available.
4. Preflight archives before extraction and commit only inside a private staging root. Reject absolute/traversal/link/colliding entries and enforce file-count/uncompressed-size limits.
5. Replace delete-then-install with stage → validate → atomic swap → persist. Retain the previous version until the replacement is confirmed runnable.
6. Make operation results authoritative: no `InstalledVersion`, `InstalledPath`, or installed-list mutation before the filesystem commit succeeds. Propagate failures instead of discarding booleans.
7. Serialize operations per provider/game/mod root and disable controls before confirmation awaits. Add a cancellation/close policy before exposing scene reload while work is active.
8. Add a minimal safety test harness before further feature work. At minimum, cover F-01 through F-08, F-16, F-17, and F-19 using disposable directories and fake HTTP responses.

### Correctness and recovery fixes (P1)

- Split installed-mod persistence by provider and game, and migrate the old global index conservatively.
- Use atomic JSON/Resource replacement with backup/quarantine and explicit schema migrations. Preserve corrupt files for diagnosis instead of overwriting them.
- Add generation checks to every post-await UI update; cancel search/download/title requests when superseded.
- Ensure every async handler has a terminal success/failure/canceled state and `try/finally` UI cleanup.
- Reset pagination on refresh and replace positional title parsing with tolerant structural parsing.
- Define safe save backup/restore semantics, including overlap checks, snapshots, stale-file handling, and rollback.
- Add timeouts and retry policies to read operations, while keeping destructive/download commits non-retryable unless idempotent.

### Architectural improvements (P2)

- Extract a pure provider/release client, source client, persistence repository, safe filesystem service, archive preflight/deployment service, and operation coordinator.
- Keep `Home`, `Installer`, `ModManager`, and `ToolsPage` as thin Godot adapters.
- Replace display-name row lookup with stable IDs and use a manifest instead of `Managed` prefix heuristics.
- Introduce a minimal deployment plan model that can later support dependencies/conflicts/load order without requiring a rewrite of the UI.
- Remove `Node` inheritance from `StandardModManagement` unless it becomes an actual scene-owned service; pass dependencies explicitly.

### Testing and release improvements

- Add unit tests for `ModPaths`, canonical containment, provider asset/version selection, OS parsing, identity/collision handling, and migration.
- Add disposable filesystem tests for deletion, copy/restore, staging, atomic swap, locked/read-only/disk-full conditions, and link boundaries.
- Add archive fixture tests for traversal, absolute paths, symlinks, duplicate/case-collision entries, bombs, corrupt archives, and cancellation.
- Add fake HTTP tests for status failures, redirects, partial downloads, stale files, timeouts, cancellation, retry, and out-of-order responses.
- Add Godot headless smoke tests for scene import, exported node paths, signal connections, provider reload, dialog cancellation, close during operation, and launcher mode.
- Add CI to restore/build the C# project, run tests, validate scene/resource references, and at least smoke-check each export preset on its supported runner.
- Align README/devlog/export metadata with the actual Godot 4.7/.NET 8 target and document the unsupported/manual macOS DMG path.

## 17. Areas Reviewed With No Significant Issues

These areas were inspected and did not show a significant defect in static review:

- The repository has a clean working tree at review start, and the recent branch/history state is internally consistent (`main` tracks `origin/main` at `5f71d85`).
- `project.godot` has a clear main scene and autoload declaration. Referenced `res://` file paths found in the scenes/configuration all resolved in a static existence check.
- `ProviderRegistry` normalizes the documented Eden/Ryubing/Ryujinx/Yuzu aliases and falls back to the default provider rather than returning null.
- Manual title-ID input is constrained to the expected 16-character hexadecimal format before a game directory is created (`ModManager.cs:188-207`).
- GameBanana search terms are URL-escaped, and the paginated search path attempts to deduplicate non-empty remote IDs.
- Confirmation dialog cancellation covers the explicit Cancel, Escape, and window-close events (`Tools.cs:23-30`), which is a good local decision. It still needs scene-exit cancellation (F-17).
- The provider `Mode` abstraction avoids duplicating all executable/path defaults in each provider and is a reasonable extension point.
- No committed production code, real user folder, game installation, save directory, or external process was modified during this review. The only intended repository artifact is this report.

These positives do not offset the absence of transaction/ownership guarantees in destructive workflows.

## 18. Uncertainties and Recommended Runtime Verification

The following points could not be conclusively established without Godot/.NET and should be verified in a disposable test environment:

1. **SharpCompress extraction behavior:** Confirm whether SharpCompress 0.49.1 rejects traversal, absolute paths, symlink entries, duplicate paths, and oversized declarations by default. Test both `WriteToDirectory` call sites; do not rely on a package default without an application-level test.
2. **Godot `HTTPRequest.DownloadFile`:** Determine whether a failed request truncates/removes an existing destination, how redirects and error bodies are handled, and whether parent directories in a crafted path are created. This validates the consequence described in F-03/F-04/F-20.
3. **Godot C# scene import:** Run a headless/editor import and launch to validate exported private fields, scene string signal connections, deferred group calls, timers, and `Godot.Collections.Array` Resource serialization.
4. **Scene teardown:** Start each HTTP/dialog/download operation, reload the provider scene, and close the app. Confirm whether freed `HTTPRequest` signals cancel or leave `ToSignal` awaiters pending, and whether root `CloseRequested` retains old `ModManager` handlers.
5. **macOS launch semantics:** Test `.app` bundle discovery and `Process.Start` on Intel/ARM macOS. Verify whether `/usr/bin/open` is required and how quarantine/signing affects launch.
6. **Filesystem link behavior:** On Windows test junctions/reparse points and case variants; on Linux/macOS test symlinked directories during recursive delete/copy/extraction. Confirm whether .NET enumeration follows links in each targeted runtime.
7. **Crash/power-loss recovery:** Kill the process during download, extraction, directory swap, JSON write, and Resource save. Verify no false installed state and that the previous working install remains recoverable.
8. **Package/security audit:** Restore exact NuGet versions, run the project's supported dependency audit, and validate whether any package requires a security upgrade before release. No restore or vulnerability audit was possible here.

All runtime verification should use a temporary fixture root and a fake local HTTP server. It should never point at a real emulator install or user save directory.

## 19. Appendix

### A. Repository inventory

| Area | Files/size observed |
|---|---:|
| C# source | 23 files, approximately 3,700 lines including providers/UI/services |
| Godot scenes | 6 scenes, approximately 1,100 lines |
| Project/build | `project.godot`, `EmuStack.csproj`, `EmuStack.sln`, `export_presets.cfg` |
| Persistence | `ResourceSaveManager` Resource plus `user://InstalledMods.json` |
| Tests/fixtures | None found |
| GDScript | None found |
| CI/analyzers | None found |
| External services | Eden HTML, Ryubing update server/Forgejo, Switchbrew HTML, GameBanana API |

### B. Important state and filesystem paths

```text
user://InternalSave.tres or .res       SettingsResource / provider settings
user://InstalledMods.json              Global mod index (not provider-scoped)
<ModsLocation>/<title-id>/              Per-title mod root
<title-id>/Managed<mod-name>            Managed mod deployment folder
<title-id>/Managed<mod-name>-temp       Mod extraction staging folder
<title-id>/<mod-name>-Download          Mod archive destination
<SaveDirectory>/                        Emulator install/download root
```

### C. Review evidence index

- Startup/lifecycle: `project.godot:17-27`, `Scripts/Globals.cs:19-69`, `Scripts/Home.cs:33-117`.
- Persistence/migration: `Scripts/ResourceSaveManager.cs:14-98`, `Scripts/SettingsResource.cs:5-36`, `Scripts/Globals.cs:72-126`.
- Emulator install: `Scripts/Installer.cs:72-239`, `Scripts/Installer.cs:354-451`.
- Mod lifecycle: `Scripts/StandardModManagement.cs:16-129`, `Scripts/ModManager.cs:293-340`, `Scripts/ModManager.cs:472-526`, `Scripts/ModManager.cs:679-727`.
- Filesystem helpers: `Scripts/FsHelpers.cs:8-90`, `Scripts/ModPaths.cs:4-37`.
- UI/network async: `Scripts/ModManager.cs:110-250`, `Scripts/ModManager.cs:343-368`, `Scripts/ModManager.cs:741-1023`, `Scripts/Sources/BananaManager.cs:28-263`.
- Platform/process: `Scripts/Os.cs:7-17`, `Scripts/ShortcutFactory.cs:8-106`, `Scripts/Tools.cs:56-69`.
- Scene wiring: `Scenes/Home.tscn:39-245`, `Scenes/InstallerPage.tscn:10-250`, `Scenes/ModManagerPage.tscn:149-392`, `Scenes/ToolsPage.tscn:6-107`.

### D. Review limitation

The absence of `godot` and `dotnet` prevented build, import, test, and runtime reproduction. Static checks passed where available, but that is not evidence that the Godot project compiles or that exported scenes launch successfully.
