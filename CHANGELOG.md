# Changelog

## 1.1.1 (2026-11-10)

- New plugin icon in Logi Options+: `</>` on a dark background, easier to read at small sizes. Its source is `tools/icon/icon.svg`.

## 1.1.0 (2026-11-10)

- **The installers install the Haptic Web Plugin.** With the agent haptics or the terminal integration, `install.sh` and `install.ps1` offer to install the [Haptic Web Plugin](https://github.com/Fallstop/HapticWebPlugin) by Jasper M-W (MIT license), which their vibrations need: one question, Enter meaning yes. `--yes` and `--only` install it without asking, `--no-haptic-web` / `-NoHapticWeb` skip it. The package comes from the latest release of that project and is checked (`name: HapticWeb`) before it goes to the plugins folder of Logi Plugin Service, which is asked to load it. A Haptic Web Plugin already installed, from the Marketplace or elsewhere, is never replaced. Any failure leaves the rest of the installation as it was and falls back to the Marketplace instructions. Windows is untested.
- The uninstallers leave the Haptic Web Plugin in place, since other tools may use it, and say so.

## 1.0.0 (2026-11-10)

First public release.

- **Actions Ring plugin**, `MxDevKit.lplug4`, prebuilt, one file for macOS and Windows: double-click it with the plugins manager of Logi Options+ open. Bubbles for the project and its branch, issues, pull requests, CI, vulnerabilities, commit all changes, push, pull, coding agents, review requests, GitHub inbox and a focus timer (25 minutes, then a 5-minute break). They follow the folder of your last terminal prompt and, on macOS, of the Finder. Push never forces, pull only fast-forwards, and an AI CLI can write the commit message. English and French, following the system language.
- **A bubble for each coding agent**, with its mark: Claude Code, Codex, Gemini CLI and GitHub Copilot CLI. A click opens a terminal in the current project and starts `claude`, `codex`, `gemini` or `copilot`, on macOS and Windows, in the terminal set in `agent.terminal` (or `agent.launch`). An agent that is not found on the computer shows a grey bubble with "Install", and the click opens its install page instead. `agent.commands` changes the command line of one of them, e.g. `{ "codex": "codex --full-auto" }`. The marks are drawn by the plugin from SVG and belong to their owners. Without a current project, a click shows a notification.
- **Agent in project**, for any other agent or tool: it starts `agent.command`, `claude` by default.
- **Native vibrations on the MX Master 4** from the plugin: CI finished, review request, mention or assignment, pull request approved, merged, with changes requested or a failed check, git action result, focus and break over.
- **GitHub inbox** across all your repositories, through `gh api` only, with a clickable notification for each event. Each event can be turned off in `github.events`. Nothing is marked as read on GitHub.
- **Vulnerabilities for any stack** with osv-scanner (every lockfile of the repository), `npm audit` when osv-scanner is not installed. Scans again a few seconds after a lockfile changes. `audit.tool` picks `auto`, `osv` or `npm`.
- **Coding agent hooks** for Claude Code, Gemini CLI, Codex CLI and GitHub Copilot CLI: a vibration when the agent is done, waits for you or fails. The installers back up and merge `settings.json` and `hooks.json`, without jq on macOS. Copilot CLI hooks (`agentStop`, `notification`, `errorOccurred`) go to a file of their own, `~/.copilot/hooks/mx-dev-kit.json`, which the uninstallers delete: `hooks/install.sh --copilot`, `hooks\install.ps1 -Copilot`. The trigger file takes `claude`, `codex`, `gemini` and `copilot`.
- **Shell integration** for zsh, bash and PowerShell: project tracking, a vibration when a command longer than 30 seconds ends, `buzz` and `ci-watch`.
- **Scripts**: `haptic` plays a waveform, `mx-notify` shows a notification that opens a URL when clicked (the plugin carries it too), `uptime-watch` watches websites, health endpoints and local ports, `docker-watch` the containers of your compose project (dies, restart loop, healthy again).
- **Installers** for macOS (tested) and Windows 10/11 (untested): `curl -fsSL .../releases/latest/download/install.sh | sh` or `irm .../install.ps1 | iex`, or `mx-dev-kit.zip`. Nothing to build and nothing to install beyond Logi Options+. One question per part (plugin, agent hooks, terminal integration), `--only` / `-Only` to pick parts without asking, `--yes` for everything. Downloads are checked against `SHA256SUMS`, every edited file is backed up, and `uninstall.sh` / `uninstall.ps1` reverse it. `--from-source` builds the plugin from a clone, for contributors.
- **Releases** are built by `tools/build-release.sh`, the same script locally and in the release workflow, which also checks the package and the installers on Windows.
