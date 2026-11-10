# MX Dev Kit

Developer productivity kit for Logitech MX devices on macOS and Windows. Actions Ring bubbles for your repo (CI, PRs, git) and MX Master 4 vibrations when your coding agent finishes.

**Unofficial, not affiliated with Logitech.**

[![CI](https://github.com/lesjoiesducode/mx-dev-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/lesjoiesducode/mx-dev-kit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![macOS](https://img.shields.io/badge/macOS-tested-success)
![Windows](https://img.shields.io/badge/Windows-untested-lightgrey)

A plugin for the Logitech Actions Ring that shows the state of the repository you are working on: CI, issues, pull requests, vulnerabilities, changes to commit, push or pull. Each bubble is also a button, and one click starts Claude Code, Codex, Gemini CLI or Copilot CLI in the project. On an MX Master 4, the mouse vibrates when your CI finishes, your coding agent is done or a container dies.

![Two Actions Rings drawn by the plugin. Left: the Claude Code bubble and a project with a failed CI, 7 issues, 2 pull requests, 5 vulnerabilities, 3 changed files and 2 commits to push. Right: the generic agent bubble and another project with a green CI, a focus timer running and 2 review requests.](docs/img/hero.png)

<sub>Drawn by the plugin's own code. In Logi Options+ the ring is smaller and opens around the cursor.</sub>

<!-- Replace with the URL of the article once it is published. -->
> The story behind it, in French, on [Les Joies du Code](https://lesjoiesducode.fr): *article link coming soon*.

Logitech, Logi, MX Master, Logi Options+ and Actions Ring are trademarks of Logitech. Claude and Claude Code are trademarks of Anthropic, Codex of OpenAI, Gemini of Google, GitHub Copilot of GitHub. Their marks only name the tool each bubble starts.

## Features

- **Project bubbles** that follow the repository of your last terminal prompt or Finder window: name and branch, open issues, open pull requests, latest GitHub Actions run, known vulnerabilities.
- **Git from the ring.** Commit all changes, push, pull. Push never forces and pull only fast-forwards. An AI CLI can write the commit message.
- **Coding agents in one click.** Claude Code, Codex, Gemini CLI and Copilot CLI each have a bubble with their mark, which opens a terminal in the repository and starts the agent. **Agent in project** does the same with any command you set.
- **GitHub inbox** across all your repositories: review requests, mentions, assignments, approvals, merges, changes requested and failed checks on your pull requests. Each one vibrates and shows a notification you can click.
- **Vulnerabilities for any stack** with osv-scanner, or `npm audit` when osv-scanner is not installed. It never fixes anything.
- **Focus timer**, 25 minutes of work then a 5-minute break.
- **Coding agent hooks.** Claude Code, Codex CLI, Gemini CLI and GitHub Copilot CLI vibrate when they finish, wait for you or fail.
- **Shell integration** for zsh, bash and PowerShell. Any command longer than 30 seconds vibrates when it ends.
- **`uptime-watch`** for websites, health endpoints and local ports, **`docker-watch`** for the containers of your compose stack.
- **`haptic`** and **`mx-notify`** to vibrate or show a clickable notification from your own scripts.

## Requirements

- [Logi Options+](https://www.logitech.com/en-us/software/logi-options-plus) on macOS or Windows 10/11. It brings Logi Plugin Service 6.4 or later, which runs the plugin.
- A Logitech device that opens the Actions Ring. Vibrations need an **MX Master 4**, the only device with a haptic motor.
- The free [Haptic Web Plugin](https://github.com/Fallstop/HapticWebPlugin) by Jasper M-W (MIT license), for the vibrations of the hooks, the shell and the watchers. The installer offers to install it for you, or you can get it from the Marketplace inside Options+. The plugin's own vibrations work without it.

Some features use a tool you may already have. Without it, that feature stays off and the rest works.

| Tool | Turns on |
|---|---|
| Git | The project bubbles |
| [GitHub CLI](https://cli.github.com), after `gh auth login` | GitHub bubbles, GitHub inbox, `ci-watch`. github.com only. |
| [osv-scanner](https://google.github.io/osv-scanner/) | Vulnerabilities for every lockfile. Without it, npm projects only. |
| Docker | `docker-watch` |
| [terminal-notifier](https://github.com/julienXX/terminal-notifier), macOS | Notifications that open a page when clicked |

**Windows is untested.** The plugin and the scripts are written for Windows and the release workflow checks them there, but nobody has run them on a real Windows machine with an MX device yet. Reports and pull requests are welcome.

## Install

Nothing to build. Logi Options+ is all you need.

```sh
# macOS
curl -fsSL https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.sh | sh
```

```powershell
# Windows
irm https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.ps1 | iex
```

The installer asks about each part, and Enter means yes. The parts are the Actions Ring plugin, the coding agent haptics and the terminal integration. `--only` picks parts without asking, `--yes` installs everything. For the agent vibrations alone:

```sh
curl -fsSL https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.sh | sh -s -- --only hooks
```

On Windows, the same is `& ([scriptblock]::Create((irm https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.ps1))) -Only hooks`.

Downloads are checked against `SHA256SUMS`. Files go to `~/.config/mx-dev-kit`, or `%APPDATA%\mx-dev-kit` on Windows, and every file the installer edits is backed up first. If you would rather not pipe a script into your shell, download `mx-dev-kit.zip` from the [latest release](https://github.com/lesjoiesducode/mx-dev-kit/releases/latest) and run `./install.sh` from it. To add the plugin by hand instead, download [MxDevKit.lplug4](https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/MxDevKit.lplug4), open the plugins manager of Options+ (**Haptic feedback**, the settings popover, **Install and uninstall plugins**) and double-click the file while that window is open.

**Haptic Web Plugin.** With the agent haptics or the terminal integration, the installer also offers to install the [Haptic Web Plugin](https://github.com/Fallstop/HapticWebPlugin) by Jasper M-W (MIT license), which their vibrations go through. It downloads `HapticWeb.lplug4` from the latest release of that project, checks that the package is the Haptic Web Plugin, and asks Logi Plugin Service to load it. `--yes` and `--only` install it without asking, `--no-haptic-web` (`-NoHapticWeb` on Windows) skips it without asking. If it is already installed, from the Marketplace or elsewhere, it is left as it is, never replaced. If anything goes wrong (offline, GitHub unavailable, Logi Options+ missing, package refused), the rest of the kit installs anyway and the installer tells you to get it from the Marketplace in Options+ instead. The Windows installer does the same, untested.

Codex ignores new hooks until you trust them, so open Codex once and type `/hooks`.

**Fill the ring.** In Options+, open your device, then **Actions Ring**, and drag actions from **MX Dev Kit** onto a ring. The default ring shows in every app that has no ring of its own. The ring has 8 slots for 16 bubbles. A good start is **Project**, **CI status**, **Pull requests**, **Commit all changes**, **Push**, **Pull**, the bubble of your coding agent (**Claude Code**, **Codex**, **Gemini CLI** or **Copilot CLI**) and **GitHub inbox**.

A new bubble first shows only its name. Click the pencil on its icon, reset the icon with the round arrow above the preview, then **Apply** and **Save**. The live image appears. You do this once per bubble.

Open a new terminal, `cd` into a repository and run `haptic completed` to check the vibration.

## Actions Ring for devs

The bubbles refresh on their own. GitHub is polled every 60 seconds, every 15 while a workflow runs. `git fetch` runs every 3 minutes for the Pull bubble.

| | Bubble | Shows | Click |
|---|---|---|---|
| <img src="docs/img/bubbles/project.png" width="48" alt=""> | Project | Repository and branch | Opens it on GitHub, or the folder |
| <img src="docs/img/bubbles/issues.png" width="48" alt=""> | Issues | Open issues | Opens the list |
| <img src="docs/img/bubbles/pull-requests.png" width="48" alt=""> | Pull requests | Open pull requests | Opens the list |
| <img src="docs/img/bubbles/ci-failed.png" width="48" alt=""> | CI status | Latest Actions run of the branch, vibrates when it ends | Opens the run |
| <img src="docs/img/bubbles/audit.png" width="48" alt=""> | Vulnerabilities | High and critical advisories by default | Scans again |
| <img src="docs/img/bubbles/commit.png" width="48" alt=""> | Commit all changes | Changed files | `git add -A` and commit |
| <img src="docs/img/bubbles/push.png" width="48" alt=""> | Push | Commits to push | `git push`, publishes a new branch |
| <img src="docs/img/bubbles/pull.png" width="48" alt=""> | Pull | Commits to pull | `git pull --ff-only` |
| <img src="docs/img/bubbles/claude.png" width="48" alt=""> | Claude Code | | Starts `claude` in a terminal, in the project |
| <img src="docs/img/bubbles/codex.png" width="48" alt=""> | Codex | | Starts `codex` |
| <img src="docs/img/bubbles/gemini.png" width="48" alt=""> | Gemini CLI | | Starts `gemini` |
| <img src="docs/img/bubbles/copilot.png" width="48" alt=""> | Copilot CLI | | Starts `copilot` |
| <img src="docs/img/bubbles/agent-missing.png" width="48" alt=""> | | The agent is not installed | Opens its install page |
| <img src="docs/img/bubbles/agent.png" width="48" alt=""> | Agent in project | | Starts `agent.command` (`claude` by default), for any other agent or tool |
| <img src="docs/img/bubbles/reviews.png" width="48" alt=""> | Review requests | Pull requests waiting for your review | Opens them |
| <img src="docs/img/bubbles/inbox.png" width="48" alt=""> | GitHub inbox | Last GitHub event for you | Opens it |
| <img src="docs/img/bubbles/focus-running.png" width="48" alt=""> | Focus timer | Time left | Starts or stops |

To try a bubble without the mouse, write its name to the trigger file: `echo commit > ~/.config/mx-dev-kit/trigger`. It also takes `push`, `pull`, `agent`, `claude`, `codex`, `gemini`, `copilot`, `audit`, `focus`, `inbox`, `notify` and `inbox-test`.

## Scripts

The scripts are on your PATH once the terminal integration is loaded:

```sh
haptic completed                       # play a waveform. haptic --list shows them all
./deploy.sh && haptic happy_alert || haptic angry_alert
mx-notify "Deploy done" "api 1.4.2 is live" https://example.com/status
buzz make test                         # vibrate when this command ends, whatever its duration
git push && ci-watch                   # vibrate when the Actions runs of HEAD are done
uptime-watch https://example.com http://localhost:3000/health localhost:5432
docker-watch                           # containers of the compose project in this folder, --all for every one
touch ~/.config/mx-dev-kit/haptics.off   # mute every vibration, delete the file to unmute
```

Options and background setups are in [bin/](bin/), [shell/](shell/) and [hooks/](hooks/). On Windows the scripts end in `.ps1`.

## Configuration

Everything is optional. The installer creates `~/.config/mx-dev-kit/config.json` from [config.example.json](config.example.json), where every key is commented. The plugin reloads the file when you save it. The settings you are most likely to change:

```jsonc
{
  "agent": { "terminal": "iTerm", "commands": { "codex": "codex --full-auto" }, "command": "aider", "label": "Aider" },
  "commit": { "messageCommand": ["claude", "-p", "{prompt}", "--model", "haiku"] },
  "audit": { "minSeverity": "moderate" },
  "github": { "events": { "teamMention": false } },
  "tools": { "gh": "/opt/homebrew/bin/gh" }
}
```

`agent.terminal` and `agent.launch` apply to every agent bubble. `agent.commands` changes the command line of the Claude Code, Codex, Gemini CLI or Copilot CLI bubble; `agent.command` and `agent.label` are those of **Agent in project**. `commit.messageCommand` receives the staged diff on standard input. Without it, or if it fails, the message is built from the file list, like "Update app.ts, api.ts and 3 more files". `tools` is only needed when the plugin does not find a tool, because it does not see your shell's PATH.

## Languages

The plugin is in English and French. With `"language": "auto"` it follows the first preferred language of your macOS account, or the display language of Windows. French when it starts with `fr`, English otherwise. Set `"en"` or `"fr"` to force it, then restart the plugin so that the action names change in Options+. The scripts, hooks and installers are in English.

<img src="docs/img/ring-french.png" width="320" alt="The same ring with French labels">

## Troubleshooting

- **A bubble shows its name instead of an image.** Reset its icon once, as described in [Fill the ring](#install).
- **MX Dev Kit is not listed in Options+.** Open the Options+ settings and click **Restart Logi Plugin Service**.
- **An agent bubble says Install while the agent is installed.** The plugin does not see your shell's PATH. Set its path in `tools`, e.g. `"tools": { "codex": "/Users/you/.npm-global/bin/codex" }`.
- **GitHub bubbles show `–`.** The repository has no GitHub `origin` remote, or `gh` is missing or not signed in. Run `gh auth login`.
- **No vibration.** Only the MX Master 4 vibrates. Check the haptics settings in Options+ and that `haptics.off` does not exist. For the scripts and hooks, `haptic --list` tells you whether the Haptic Web Plugin answers.
- **`haptic` says the Haptic Web Plugin is unreachable.** Check that it is installed: a `HapticWeb` folder in `~/Library/Application Support/Logi/LogiPluginService/Plugins` (`%LOCALAPPDATA%\Logi\LogiPluginService\Plugins` on Windows). If it is there, restart Logi Plugin Service from the Options+ settings, then try `haptic --list` again. If it is not, run the installer again or install it from the Marketplace in Options+.

The plugin log is `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/MxDevKit.log` on macOS and `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\MxDevKit.log` on Windows.

## Uninstall

```sh
~/.config/mx-dev-kit/uninstall.sh          # --purge also removes config.json
```

```powershell
powershell -ExecutionPolicy Bypass -File "$env:APPDATA\mx-dev-kit\uninstall.ps1"   # -Purge also removes config.json
```

It removes the plugin, the shell profile line, the hooks and the scripts, and keeps a backup of every file it edits. The Haptic Web Plugin stays, since other tools may use it: uninstall it from Options+ if you do not need it.

## Build from source

Only for contributors. You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
git clone https://github.com/lesjoiesducode/mx-dev-kit.git
cd mx-dev-kit
dotnet build MxDevKit.slnx
dotnet test tests/MxDevKit.Core.Tests
sh tests/shell/run.sh                        # scripts and installers, with fake tools
./install.sh --link --only plugin            # load your Debug build in Logi Plugin Service
```

The logic lives in `src/MxDevKit.Core`, with its tests. `src/MxDevKitPlugin` is the thin layer that talks to the SDK. Releases and the rest are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Credits and license

Built on the [Logi Actions SDK](https://logitech.github.io/actions-sdk-docs/). Script vibrations go through the [Haptic Web Plugin](https://github.com/Fallstop/HapticWebPlugin) by Jasper M-W, MIT license. The installers download it from its own releases; it is not part of this repository. The agent marks come from [Simple Icons](https://simpleicons.org) (Claude, GitHub Copilot from GitHub's [Primer octicons](https://primer.style/octicons)) and [Lobe Icons](https://github.com/lobehub/lobe-icons) (Codex, Gemini).

[MIT](LICENSE) © 2026 [Les Joies du Code](https://lesjoiesducode.fr).
