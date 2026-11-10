# Contributing

Issues and pull requests are welcome, especially from **Windows** users: the Windows code paths (plugin, installers, PowerShell scripts) have never run on a real Windows machine. A report that says "this works" or "this fails at that step" is already a great help.

## Before you open a pull request

```sh
dotnet build MxDevKit.slnx
dotnet test tests/MxDevKit.Core.Tests
sh tests/shell/run.sh          # the scripts and installers, with fake docker, curl, notifiers and release (macOS, Linux)
```

- Logic that does not need Logitech's API goes to `src/MxDevKit.Core`, with tests. The plugin project stays a thin layer: processes, timers, drawing, actions.
- A new bubble needs English and French texts in `Strings.cs`, a state in `Bubbles.cs` and, if it shows in the README, a sample in `tools/BubblePreview/Samples.cs`.
- A new haptic event goes to `HapticEvents.cs`, `package/events/DefaultEventSource.yaml` and `package/events/extra/eventMapping.yaml`: a test checks that the three agree.
- A command-line tool comes as a `sh` script and a `.ps1` for Windows, installed by both installers, with tests in `tests/shell/run.sh`.
- Scripts: POSIX `sh` when possible, bash 3.2 compatible (the one macOS ships), PowerShell 5.1 compatible on Windows. Never edit a user's file without a backup.
- Installing and the basic features need nothing that macOS or Windows 10/11 do not ship: sh, curl, osascript, unzip, Windows PowerShell 5.1. No jq, Python, Node.js or .NET in an installer. A tool that only one feature needs (gh, docker, osv-scanner, npm) is optional: its absence turns that feature off and never stops the installation.
- Keep the README in sync with what you change.

## Running your build in Options+

Contributors need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); users never do.

```sh
./install.sh --link --only plugin
```

This builds the Debug configuration and writes a `.link` file in the Logi Plugin Service plugins folder, pointing to `src/MxDevKitPlugin/bin/Debug`. Afterwards, `dotnet build src/MxDevKitPlugin -p:LinkPlugin=true` rebuilds and asks the service to reload the plugin. A plain `dotnet build` never touches Logi Plugin Service.

`./install.sh --from-source` (`.\install.ps1 -FromSource`) builds the Release configuration, packages it with LogiPluginTool (installed with `dotnet tool` if missing) and installs that package instead of the one of the latest release.

To unlink, delete `MxDevKitPlugin.link` from `~/Library/Application Support/Logi/LogiPluginService/Plugins` (macOS) or `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins` (Windows), then restart Logi Plugin Service from the Options+ settings.

Debugging: attach your debugger to the `LogiPluginService` process (`LogiPluginService.exe` on Windows). Logs are in `Logs/plugin_logs/MxDevKit.log` next to the `Plugins` folder.

## Releases

A release is what users install: `MxDevKit.lplug4` (prebuilt, one package for macOS and Windows), `mx-dev-kit.zip` (installers, hooks, shell integration, scripts and the plugin), `install.sh`, `install.ps1` and `SHA256SUMS`. The names carry no version, so that `releases/latest/download/<name>` always points to the latest one.

1. Set the version in `src/MxDevKitPlugin/package/metadata/LoupedeckPackage.yaml` and in `<Version>` of `src/MxDevKitPlugin/MxDevKitPlugin.csproj`.
2. Give it a `## X.Y.Z (date)` section in `CHANGELOG.md`: it becomes the release notes.
3. Run `tools/build-release.sh --tag vX.Y.Z`. It runs the unit and shell tests, builds, packages, checks the package (`logiplugintool verify`, expected files, version, no copy of PluginApi) and the archive, and writes everything to `dist/release`. Try `dist/release/mx-dev-kit.zip` on your machine.
4. Commit, then push a tag `vX.Y.Z`. The release workflow runs the same script on macOS, checks the package and the installers on Windows, and publishes the GitHub release with its files.

## Images

The pictures in `docs/img` are drawn by the plugin's painter itself, from made-up projects:

```sh
dotnet run --project tools/BubblePreview
```

It loads `BitmapBuilder` and Skia from your Logi Plugin Service installation (macOS; on Windows set `LOGI_PLUGIN_SERVICE_DIR` if it is not in Program Files).
