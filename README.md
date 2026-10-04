# E33-Randomizer
A randomizer mod for Clair Obscur: Expedition 33 that gives users complete control over every enemy placement in the game, as well as a lot of different tools to customize them to their heart's desire.

Installation:
- Download the latest release from [Releases](https://github.com/SilestaGroves/E33-Randomizer/releases/latest):
  - `E33Randomizer-<version>-win-x64.zip` works as is, nothing else to install
  - `E33Randomizer-<version>-win-x64-framework.zip` is much smaller, but needs the [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- Unpack the zip file in a non-admin folder
- Run E33Randomizer.exe
- Updates: the randomizer checks for a new release on startup (and with the "Check for updates" button at the bottom of the main window), and can download and install it itself. Your settings, game folder, custom presets and generated mods are kept

Publishing a new release: push a tag like `v1.2.3`; GitHub Actions runs the tests, builds both zips and creates the release. Release notes come from `docs/release-notes/<tag>.md` if that file exists.
<br>

Running the randomizer:
- Start the E33Randomizer.exe
- Configure the mod as you see fit
- Set the game folder at the bottom of the main window once ("Find Steam install" or "Browse..."); it's remembered in game_path.txt
- Click "Generate and pack mod" button in the main window or "Generate mod files from current"
- The generated .pak, .utoc, and .ucas files are copied into **Expedition 33\Sandfall\Content\Paks\\~mods** automatically, replacing the previous randomizer files. Without a game folder (or with "Copy the mod into the game folder" unchecked), copy them there yourself from the rand_&lt;seed&gt; folder, creating ~mods directory if necessary
- A human-readable spoiler_log.txt is written next to the mod
- Start the game and enjoy the chaos

If the game crashes:
- Generate the mod again with the latest version of the randomizer, and make sure the game is up to date: the randomizer only works with the game version its Data folder was taken from (shown at the top of generation_log.txt), because the mod replaces whole game files
- Remove other mods from ~mods and LogicMods to rule them out (generation_log.txt lists everything that was installed)
- Send these files with the crash report:
  - **generation_log.txt** from the rand_&lt;seed&gt; folder: randomizer and game versions, settings, other installed mods, and every game file the mod replaces
  - **logs\randomizer.log** next to E33Randomizer.exe: errors of the randomizer itself
  - The "Application Error" entry for SandFall-Win64-Shipping.exe in the Windows Event Viewer (Windows Logs → Application)
  - A crash dump: the game keeps no logs of its own, but Windows can save a dump on every crash. Run `enable_game_crash_dumps.ps1` (next to E33Randomizer.exe) once in PowerShell as administrator, crash again, and send the .dmp file from %LOCALAPPDATA%\CrashDumps\Expedition33


<br>
For other modders - enemy rando overrides DT_jRPG_Encounters, DT_jRPG_Encounters_CleaTower, DT_Encounters_Composite, and DT_WorldMap_Encounters files, as well as DT_jRPG_Enemies if the option "Tie loot drops to encounters instead of enemies" is on. The item rando can override the files in the Data/ItemData directory, but only puts the ones it actually changed into the mod; generation_log.txt lists them.
<br>
<br>
<br>
Running from source:
- Install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or newer)
- `git clone --recursive https://github.com/SilestaGroves/E33-Randomizer.git` (or run `git submodule update --init` after a plain clone)
  - If git reports "Unable to checkout" in external/UAssetAPI, the clone is in a deep folder and some of UAssetAPI's own test files exceed the Windows path length limit. They aren't needed to build or run; to avoid the message, run `git config --global core.longpaths true` before cloning or clone into a short path like C:\E33-Randomizer
- `dotnet run --project E33Randomizer`, or open E33Randomizer.sln and run it
- Everything else comes with the repository: the game data and the external tools are copied next to the exe on build, and generated mods, settings and logs are written there too

This project uses external tools (namely repak, retoc, and uesave), included in the tools folder, built from source; see [tools/README.md](tools/README.md) for versions and how to rebuild them. By using E33 Randomizer, users must also adhere to the licenses of repak, retoc, and uesave (tools/licenses), as well as UAssetAPI, in addition to E33 Randomizers' own.
<br>
<br>
<br>

Credits & Special Thanks:

- Ihor Chornyi: For the original E33 Randomizer this fork is based on, and the "skills drop from enemies" approach (skill unlock items), ported from it
- truman: For writing repak, retoc, and uesave
- atenfyr: For writing UAssetAPI
- TheNaeem: For hosting the Expedition 33 .usmap file
- Thefifthmatt: For the inspiration behind the UX of the mod
- Sandfall Interactive and Kepler Interactive: For creating one of my favourite games of all time and making it easy to mod
