# One launcher for GT Sport and GT7 (FEAT-GTS-290)

RyGT Launcher, `RyGTLauncher.exe` (this folder), launches both **Gran Turismo Sport** (RyGTS, CUSA03220) and
**Gran Turismo 7** (ryGT7, CUSA24769). It merges the RyGTS launcher (0.2.2, which was RyGTS.exe) with the ryGT7
launcher (rygt7 `main` 6178d0b, 0.1.6, which was RyGT7.exe). This page compares the two launchers as they were
and says where each feature lives now. FEAT-GTS-291 (0.3.1) gave it its own name and exe and one updater.

## How it works

- **Target game.** The home view has a *Game* list at its top left. Picking a game fades the accent in about
  300 ms (red for GT Sport, blue for GT7) and cross-fades the pictures to the other game's. The settings pages
  are then built for that game. The fade runs on the launcher's animation timer (`Anim`) and is drawn double
  buffered: `Dark.Accent`, `AccentHover` and `AccentText` are read at paint time, and labels drawn in the
  accent colour are recoloured as it moves.
- **One folder for both games.** FEAT-GTS-291: every name a game writes in its folder follows its serial
  (CUSA03220 GT Sport, CUSA24769 GT7): `user\custom_configs\<serial>.json`, `shaders\<serial>*`, `user\savedata\<serial>`
  (GAME-004/004), `logs\<serial>`, `app0\<serial>`, the settings `<serial>.RyGT.ini`, and `convert\<serial>`, `temp0\<serial>`,
  `data\<serial>` (`SharedNames` in `GameTarget.cs`). The runtime and renderer libraries are one build for both (one
  `libs\libSceMbus.prx` too), so GTSport.exe and GT7.exe may sit in ONE folder. FEAT-RT-005: both games use the
  launcher's own folder; there is no folder field. GT7 is ready there once the folder has GT7's files (RyGT7.exe or
  RyGT7.exe.old, RyGT7.ini, CUSA24769.RyGT.ini, GT7.exe, `patches\CUSA24769.xml` or `libs\libSceTextToSpeech.prx`).
  A `gt7_root` an older launcher saved in `RyGT-Launcher.ini` is kept but not used; the Game page names it.
  `user\input_config\default.ini` (the controller bindings) and `ryty-input.ini` / `ryty-wheel.ini` stay one for both.
- **Settings.** Each game's settings are its `<serial>.RyGT.ini`, which the runtime reads too:
  - GT Sport: `CUSA03220.RyGT.ini`, `RyGT.ini` renamed, written line for line as before, plus its config JSON.
  - GT7: `CUSA24769.RyGT.ini`, `RyGT7.ini` and the `RyGT.ini` it was mirrored into (with online play's keys) merged,
    plus its config JSON. Nothing is mirrored any more.
  - The old names move at the launcher's start and before Set up and Play (libc does the same at load), only when they
    are clearly the game's; the old files are kept as `.moved-<date>`, folders move in three steps, and each step is in
    `logs\<serial>\setup.log`.

  Only the launcher's own state is new. It lives in `RyGT-Launcher.ini` beside RyGTLauncher.exe: `game=gts|gt7` (the
  game picked last), and `gts_root=` / `gt7_root=` from older launchers, kept but not used (FEAT-RT-005). The launcher keeps
  any other lines in that file. A GT Sport player who never opens GT7 gets no new file, and nothing in
  their folder changes.
- **Launches.** Each game starts its own exe with its own launcher's environment, copied verbatim:
  - `StartGameGts` in `GtsPages.cs` starts GTSport.exe.
  - `StartGameGt7` in `Gt7Pages.cs` starts GT7.exe.

  The Advanced page's list of launcher-managed variables is per game too (`SwitchWalker.Game`).
- **Command line.** `--setup` and `--snapshot` take `--game gts|gt7`. Without it they use the game picked
  last. `--allow-firewall` (GT7's elevated firewall helper) runs beside an open launcher.
- **The older launchers (FEAT-GTS-291).** A folder updated by RyGTS.exe or RyGT7.exe still holds it, as the
  packages now ship RyGTLauncher.exe. At start (not for `--snapshot`, `--prepare` or `--allow-firewall`),
  `OldLaunchers.Retire` in `GameTarget.cs` renames `RyGTS.exe` or `RyGT7.exe` in the launcher's own folder to
  `<name>.old` when its version information's ProductName is `RyGTS` or `ryGT7` (the old launchers), never the
  running exe, a game or another file. It is best effort: what happened goes to the first setup log written.
  After a roll back to a release from before RyGTLauncher.exe, `Updater.Restart` puts the old launcher back
  (`OldLaunchers.PutBack`) and starts it.
- **One updater (FEAT-GTS-291).** `Updater.cs` updates either game's folder the same way: RyGT-Team/RyGT
  releases on GitHub with the Stable, Prerelease and Nightly channels, the Changelog window and roll back,
  in one Settings > About > Updates section. It updates the selected game's folder, only from a package that
  has a file named after that game's title ID (GT Sport's `user\custom_configs\CUSA03220.json`, GT7's
  `patches\CUSA24769.xml`), and it never touches either game's settings, converted exe or data. ryGT7's
  GitLab updater (`Gt7Updater.cs`) is gone. A ryGT7 folder from GitLab (manifest `RyGT7-...`) shows its
  version but can't be updated until it holds a RyGTS package for GT7 (manifest `RyGTS-...` or a
  `BUILD_INFO.txt` commit); every RyGTS package carries GT7's files since FEAT-RT-006.

## Sources

| File | What |
|---|---|
| `GameTarget.cs` | `GameKind`, `GameInfo` (names, exe, ini, colours), `LauncherState` (RyGT-Launcher.ini, folders) |
| `RyGTLoader.cs` | shared look and helpers, the shell (home view, game list, sidebar, fade), the shared pages (Game, Shaders, About, Advanced), Set up and Play dispatch |
| `GtsPages.cs` | GT Sport only: Display/Graphics/Controls pages, Set up, launch (from RyGTS's RyGTLoader.cs) |
| `Gt7Pages.cs` | GT7 only: Display/Graphics/Controls pages, Set up, launch (from ryGT7's RyGTLoader.cs) |
| `GT7Setup.cs`, `Bindings.cs`, `ControlsPage.cs`, `Wheel.cs`, `DlssDownload.cs`, `Online.cs` | from ryGT7, unchanged except `Online.cs` calling `PlayGt7` |
| `Updater.cs` | the one updater for both games' folders (GitHub RyGT-Team/RyGT); ryGT7's GitLab `Gt7Updater.cs` was removed in FEAT-GTS-291 |
| `UnifiedLauncherTests.cs`, `GT7SetupTests.cs`, `test-launcher.cmd` | the checks (see below) |

## Feature comparison

| Feature | RyGTS 0.2.2 (GT Sport) | ryGT7 0.1.6 (GT7) | Unified |
|---|---|---|---|
| Home view | pictures, logo/title "GRAN TURISMO SPORT", Play, gear, update + Changelog buttons | the same, title "...7", the signed-in online ID at the top right | **shared**, plus the Game list; title, caption, version and pictures per game |
| Sidebar pages | Game, Display, Graphics, Shaders, Controls, Advanced, About | Game, Display, Graphics, Shaders, Controls, Account, Online, About, Advanced | per game, as each had it |
| Game page paths | Game (CUSA03220), Update 1.69, System modules, Fonts (optional) | Base game (CUSA24769), Update (optional), System modules, Fonts | **shared page with one section per game** (selected game first), with its own labels; both games use the launcher's folder (FEAT-RT-005), so there is no folder field; saved to that game's ini when left |
| Set up | SELF unwrap, ryty convert, version info + embedded icon, shared shader folder, SHA-256 build record (game scripts and shader prep from the game files removed, FIX-GTS-293/294) | GameLayout validation, 1.00 conversion fix, Offline mode patch, game icon, app0 linking for old runtimes, ConfigureGT7 | **game-only** (`RunSetupGts` / `RunSetupGt7`); the helpers they share are single: SELF unwrap, fonts, modules, converter run, shader folder migration |
| Firmware modules | + libSceBluetoothHid | without it | per game |
| Display | Vulkan device, screen mode/resolution/refresh, screen shape (16:9 / fill / true wide), fast flips, FPS, splash, Discord, opening movie, RAM | screen mode/resolution/refresh + Ultrawide presets, FPS, splash, Discord, RAM, VRAM budget | **shared** screen and RAM rows (Ultrawide row GT7 only; RAM keeps each game's pipeline-cache rule); the rest game-only |
| Graphics | upscaler (FSR 1/3/4, XeSS, DLSS), scene quality / supersampling, Anti-Lag, frame generation, sharpening, libraries, wet reflections, half-rate mirror, enhancements | method/quality (`upscale_quality`), DLSS texture detail, HDR peak/paper white, PS4 Pro mode, race fps lock, texture LOD, driver cache, AMD fix | **game-only**: different config keys and different renderer switches |
| DLSS download | inline: DLSS 310.9.16 + Streamline 2.14.16 from NVIDIA GitHub, no hash check | `DlssDownload.cs`: the same versions, pinned SHA-256 + NVIDIA signature, sl.dlss.dll | **game-only** (see open questions); "Add DLSS files" shares `InstallDlssLibrary` |
| Shaders | HDD streaming; precompile, shader objects, pre-translation (on), compile pack, compile mode (default slow motion), batching | compile mode (default keep going), batching; pre-translation and compile pack (both off) were on Graphics | **shared page**: compile mode/batching/pre-translation/compile pack with each game's defaults and texts; HDD streaming and precompile GT Sport only (FIX-RT-028 removed the shader objects and compile pack options) |
| Controls | windows: Controller ports, Button bindings (ControlsForm), Steering wheel (WheelForm), HD rumble & adaptive triggers (FeelForm) | one drawn page (ControlsPage: pad, keyboard, wheel tabs) opening Controller ports | **game-only**; Controller ports (PortsForm) shared; both write `user\input_config\default.ini`, `ryty-input.ini`, `ryty-wheel.ini` in the game's folder |
| Online | none | Account and Online pages, RyGTServer + shadNet hosting, invite codes, firewall helper | **GT7 only** |
| Ports | PortsForm (2 players, names) | the same form (made public static `Apply`) | **shared** |
| Updates | GitHub RyGT-Team/RyGT: Stable/Prerelease/Nightly channels, Changelog dialog, roll back (About) | GitLab rygt-team/rygt7 releases, SHA-256 + manifest, roll back (Display page) | **shared** (FEAT-GTS-291): RyGTS's updater for both games under **About > Updates**, for the selected game's folder; it restarts RyGTLauncher.exe |
| Logging | stdout level, logs folder rotation | the same | **shared** |
| Advanced page | FEAT-GTS-437..275: switch walker (SHADGT_/RYGT_/SHARED_), descriptions, profiles, renames | an older copy (RYGT_/SHADGT_, no profiles) | **shared**: RyGTS's page for both, with each game's launcher-managed variables |
| Obsolete settings prompt | FEAT-GTS-469/275 | none | GT Sport only |
| Custom environment | env= lines in CUSA03220.RyGT.ini | env= lines in CUSA24769.RyGT.ini | **shared** (`CustomEnvironment`), saved in the game's ini |
| Environment written at start | `RYGT_LOG_DIRECTORY`, `RYGT_HDD_STREAMING`, `SHADGT_ANTI_LAG`, `SHADGT_SHADER_OBJECT` (always 0 since FIX-RT-028), `SHADGT_MANIFEST_*`, render scale (`SHADGT_PATCH`, `SHADGT_SCENE_*`), custom | `RYGT_LOG_DIRECTORY`, `RYTY_INTEROP_BASE`, `RYGT_NATIVE_INPLACE_*`, `SHADGT_FPS_LOCK`, `RYGT_DRIVER_CACHE_MAX_MB`, `RYGT_AMD_SINGLE_SAMPLE_BARYCENTRIC`, `RYGT_MANIFEST_*`, `RYGT_COMPILE_PACK`, `RYGT_DLSSG_FRAMES`, `RYGT_UPSCALE_QUALITY`, `RYGT_TEXTURE_LOD_BIAS`, `RYGT_DLSS_TEMPORAL_MIP_*`, `RYGT_NEO*`, custom | unchanged per game |
| Log rotation, portable temp | yes | yes | **shared** |
| Device loss | restart once | restart once | no restart, a line in the game's setup log (FIX-GTS-491, 0.3.2) |
| AMD Software entry, build SHA-256 record | yes | no | GT Sport only |
| Single instance | mutex | none | mutex (both) |

## Checks

`loader\test-launcher.cmd <out>` builds RyGTLauncher.exe and runs:

- `UnifiedLauncherTests.cs`:
  - RyGT-Launcher.ini defaults, round trip and unknown lines
  - one folder for both games (GTSport.exe and GT7.exe side by side, each game's files apart) or one each
  - each game's `<serial>.RyGT.ini` written back byte for byte, and the moves of RyGT.ini, RyGT7.ini, convert, temp0 and data
  - the colours and the fade's midpoint
  - each game's pages, built headless, and after a switch
  - the older launchers renamed to `<name>.old` (only those), put back after a roll back, and which
    game's folder a package can update
- `GT7SetupTests.cs`: ryGT7's Set up checks (GameLayout, Offline patch, app0, switch names, DLSS detail).
- `LoggingTests.cs`: logging and settings persistence.
- `shader-cache-tests\RenderScaleTests.cs`: the render scale.

## Open questions

- DLSS: GT Sport still downloads NVIDIA's files without pinned hashes. GT7's `DlssDownload` checks
  SHA-256 and signatures, but installs a different file set (sl.dlss.dll). Moving GT Sport to it needs a
  check that GT Sport's renderer works with that set.
- Controls: GT7's drawn Controls page and GT Sport's Controls, Bindings, Wheel and Feel windows edit the same
  files. Making one UI for both is a design decision.
- Packaging: done (FEAT-RT-006). The RyGTS package carries GT7's files too (libSceTextToSpeech.prx,
  patches\CUSA24769.xml, the server), so one package serves both games and GT7 folders update from it.
