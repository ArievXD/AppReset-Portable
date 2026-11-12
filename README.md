# AppReset

Put a Windows app back to how it was the first time you opened it, without uninstalling it.

AppReset finds the settings and data an installed program keeps in your Windows user folders and registry, shows you exactly what it found, saves a backup, and removes only what you tick. The program itself stays installed.

[Baca dalam Bahasa Indonesia](README.id.md)

> Status: early version (0.1.0). Read the safety section before using it on a PC you care about.

## Why

Windows can reset apps from the Microsoft Store (Settings, Apps, Installed apps, Advanced options, Reset), but not regular programs installed with an `.exe` or `.msi` installer. Uninstallers remove the program; what most people want after a broken update or a messed-up configuration is the program kept and its settings wiped. That usually means hunting through `AppData` and the registry by hand. AppReset does that search for you and lets you decide.

## What it does

1. Lists the programs Windows knows are installed.
2. For the one you pick, looks in:
   - `%APPDATA%` (AppData\Roaming)
   - `%LOCALAPPDATA%` (AppData\Local)
   - `AppData\LocalLow`
   - `%PROGRAMDATA%`
   - `HKEY_CURRENT_USER\Software`
   - `HKEY_LOCAL_MACHINE\SOFTWARE` and its 32-bit `WOW6432Node` view
3. Shows every match with the reason it matched and how sure it is:

   | Match | Example | Ticked for you? |
   |---|---|---|
   | Exact | `AppData\Roaming\7-Zip` for 7-Zip | Yes, in your own user data |
   | Likely | a folder named after the program file, or the whole publisher folder when that publisher has no other app installed | No |
   | Possible | a folder whose name contains the app name | No |

   Anything shared by every user on the PC (`ProgramData`, `HKEY_LOCAL_MACHINE`) is never ticked for you, because installers often keep files there that the program needs to start.
4. Saves a backup of everything you ticked to `%LOCALAPPDATA%\AppReset\Backups`, then removes it.
5. Lets you restore any backup from the Backups tab.

## What it does not do

- It does not uninstall programs or touch their program folder.
- It does not look for license keys, activation data or trial markers, and it is not a tool for getting around software licensing. Pull requests that add that will be closed.
- It does not touch accounts or data stored online. If an app syncs settings from a server, they may come back after you sign in.
- It only checks the standard locations listed above. Apps that store data elsewhere (for example in `Documents`) are not fully reset.

## Safety

AppReset deletes data, so the rules that decide what it may touch are strict and live in one place: [`SafetyRules.cs`](src/AppReset.Core/SafetyRules.cs). They are checked when scanning and again right before anything is removed or restored.

- Only folders one or two levels inside the data folders above, and only registry keys one or two levels inside `Software`.
- Never Windows' own or shared folders and keys: `Microsoft`, `Windows`, `Temp`, `Packages`, `Programs`, `Classes`, `Policies` and others.
- Never the app's program folder, even when it sits inside AppData (Discord and VS Code user installs do this).
- Never a junction or symbolic link, so nothing outside the folder can be reached through it.
- A publisher folder shared by several installed apps is never offered as a whole.
- An item that cannot be backed up completely (for example because the app has a file open) is left in place.
- Machine-wide items need you to restart AppReset as administrator. It runs as a normal user otherwise.
- If Windows asks for a different administrator account when you restart as administrator, AppReset then works on that account's user data, not yours. The paths it shows include the user name, so check them.
- AppReset warns you and blocks the reset while the app is running.

The matching and safety rules are covered by unit tests in [`tests/`](tests/AppReset.Tests).

## Download

Get `AppReset.exe` from the [Releases](../../releases) page. It is a single file that runs on Windows 10 and 11 (64-bit) with nothing else to install.

Windows SmartScreen may warn about the file because it is not code-signed yet. You can check what it does in the source and build it yourself (below).

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
git clone <this repository>
cd AppReset
dotnet test tests/AppReset.Tests
dotnet run --project src/AppReset.App
```

To make the single-file exe that the release uses:

```powershell
dotnet publish src/AppReset.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
```

Every push to `main` builds the exe with GitHub Actions. Pushing a tag such as `v0.1.0` also creates a release with the exe attached.

## How the code is laid out

| Folder | What is in it |
|---|---|
| `src/AppReset.Core` | Finding apps, matching names, the safety rules, backup, reset and restore. No UI. |
| `src/AppReset.Core/Windows` | The parts that touch the registry, disk and processes. |
| `src/AppReset.App` | The WPF window. Text is in `Strings/Strings.en.xaml` and `Strings/Strings.id.xaml`. |
| `tests/AppReset.Tests` | Tests for name matching, safety rules and the scanner, using a pretend disk and registry. |

## Contributing

Bug reports with the app name, its publisher and what AppReset found (or missed) are the most useful thing you can send.

If you change what AppReset may touch, add a test to `SafetyRulesTests.cs` or `DataScannerTests.cs` that shows the new case. To add a language, copy `Strings/Strings.en.xaml`, translate the values (keep the keys), and add it to the language list in `MainViewModel.cs`.

## License

MIT. See [LICENSE](LICENSE).
