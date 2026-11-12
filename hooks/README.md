# Coding agent hooks

Your MX Master 4 vibrates when the agent is done, taps when it waits for you, and buzzes harder when the turn fails. You can read docs, review a PR or make coffee without watching the terminal.

| Agent | Event | Waveform | When |
|---|---|---|---|
| Claude Code | `Stop` | `completed` | The answer is finished |
| Claude Code | `Notification` | `knock` | Permission request or question waiting for you |
| Claude Code | `StopFailure` | `angry_alert` | The turn failed (API error, usage limit) |
| Gemini CLI | `AfterAgent` | `completed` | The answer is finished |
| Gemini CLI | `Notification` | `knock` | Tool permission waiting for you |
| Codex CLI | `Stop` | `completed` | The answer is finished |
| Codex CLI | `PermissionRequest` | `knock` | Codex is about to ask for your approval (shell escalation, network access) |
| Copilot CLI | `agentStop` | `completed` | The answer is finished |
| Copilot CLI | `notification` | `knock` | Permission prompt on screen, or a question for you |
| Copilot CLI | `errorOccurred` | `angry_alert` | An error Copilot cannot recover from |

It works wherever the agent runs its hooks: Claude Code in a terminal, in VS Code and in the desktop app (minimised windows included), Gemini CLI, Codex CLI and Copilot CLI in a terminal.

Codex has no event for a failed turn, so it never plays `angry_alert` (see [Codex CLI](#codex-cli) below).

**Only the turns that took a while.** The end of a turn vibrates when the turn lasted 30 seconds or more (`MX_TURN_SECONDS`), the same rule as for long commands in the terminal: a quick answer while you watch stays silent. One more hook notes when each turn starts: `UserPromptSubmit` (Claude Code, Codex), `BeforeAgent` (Gemini CLI), `userPromptSubmitted` (Copilot CLI). A permission request, a question and a failed turn always vibrate, and so does a turn whose start the script did not see (Codex through `notify`, for instance).

## Requirements

- MX Master 4 and Logi Options+.
- The [Haptic Web Plugin](https://marketplace.logi.com/plugin/HapticWeb/en) from the Logi Marketplace (community plugin, MIT, by Jasper M-W). It exposes the haptic motor on `https://local.jmw.nz:41443`, a public name that points to `127.0.0.1`.
- Nothing else: macOS uses its built-in `curl`, and the installer edits the settings with the JavaScript built into macOS (`osascript`), so neither `jq` nor Node.js is needed. Windows: Windows PowerShell 5.1, built in. (Elsewhere, the installer uses `jq`.)

Check that the mouse answers:

```sh
curl -X POST -d '' https://local.jmw.nz:41443/haptic/completed
```

## Install

The [one-command installer](../README.md#install) adds them for you. To add the hooks alone, from the release archive or a clone:

macOS:

```sh
hooks/install.sh            # every agent found
hooks/install.sh --claude   # or --gemini, --codex, --copilot, or --dry-run to preview
```

Windows:

```powershell
.\hooks\install.ps1          # or -Claude, -Gemini, -Codex, -Copilot, -DryRun
```

The installer copies `agent-haptics.sh` (or `.ps1`) to `~/.config/mx-dev-kit/bin` (`%APPDATA%\mx-dev-kit\bin` on Windows), so the hooks keep working if you move or delete this folder. Then it merges four entries into `~/.claude/settings.json` (or `$CLAUDE_CONFIG_DIR/settings.json`), three into `~/.gemini/settings.json` (or `$GEMINI_CLI_HOME/.gemini/settings.json`) and three into `~/.codex/hooks.json` (or `$CODEX_HOME/hooks.json`). Copilot CLI gets a file of its own, `~/.copilot/hooks/mx-dev-kit.json` (or `$COPILOT_HOME/hooks/`), as it loads every file of that folder. For the other three:

- the file is backed up first (`settings.json.bak-20261110-093000`),
- your other hooks and settings are kept,
- running it again changes nothing,
- an invalid JSON file is left alone.

`hooks/uninstall.sh` (or `uninstall.ps1`) removes only these entries, with a backup too, and deletes the Copilot file.

**Codex: trust the hooks once.** Codex skips a new or changed hook until you approve it. After installing, open Codex, type `/hooks` and trust the three `agent-haptics` entries (Codex reminds you at startup until you do).

## Install by hand

Claude Code, in `~/.claude/settings.json` (merge with what is already there):

```json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh claude UserPromptSubmit", "timeout": 5 }] }],
    "Stop": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh claude Stop", "timeout": 5 }] }],
    "Notification": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh claude Notification", "timeout": 5 }] }],
    "StopFailure": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh claude StopFailure", "timeout": 5 }] }]
  }
}
```

Gemini CLI, in `~/.gemini/settings.json`:

```json
{
  "hooks": {
    "BeforeAgent": [{ "hooks": [{ "name": "mx-dev-kit-haptics", "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh gemini BeforeAgent", "timeout": 5000 }] }],
    "AfterAgent": [{ "hooks": [{ "name": "mx-dev-kit-haptics", "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh gemini AfterAgent", "timeout": 5000 }] }],
    "Notification": [{ "hooks": [{ "name": "mx-dev-kit-haptics", "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh gemini Notification", "timeout": 5000 }] }]
  }
}
```

Codex CLI, in `~/.codex/hooks.json` (same layout as Claude Code, `timeout` in seconds), then `/hooks` in Codex to trust them:

```json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh codex UserPromptSubmit", "timeout": 5 }] }],
    "Stop": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh codex Stop", "timeout": 5 }] }],
    "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "~/.config/mx-dev-kit/bin/agent-haptics.sh codex PermissionRequest", "timeout": 5 }] }]
  }
}
```

Copilot CLI, in `~/.copilot/hooks/mx-dev-kit.json` (any file name in that folder works; `powershell` instead of `bash` on Windows). Copilot reads it when it starts:

```json
{
  "version": 1,
  "hooks": {
    "userPromptSubmitted": [{ "type": "command", "bash": "~/.config/mx-dev-kit/bin/agent-haptics.sh copilot userPromptSubmitted", "timeoutSec": 5 }],
    "agentStop": [{ "type": "command", "bash": "~/.config/mx-dev-kit/bin/agent-haptics.sh copilot agentStop", "timeoutSec": 5 }],
    "notification": [{ "type": "command", "bash": "~/.config/mx-dev-kit/bin/agent-haptics.sh copilot notification", "timeoutSec": 5 }],
    "errorOccurred": [{ "type": "command", "bash": "~/.config/mx-dev-kit/bin/agent-haptics.sh copilot errorOccurred", "timeoutSec": 5 }]
  }
}
```

If you keep your hooks inline in `~/.codex/config.toml` (`[[hooks.Stop]]` tables) rather than in `hooks.json`, put these there instead: Codex merges both files but warns at startup when one folder uses both.

The script needs nothing else than `curl`. On Windows, `agent-haptics.ps1` uses the `curl.exe` built into Windows 10 and 11, started without waiting and with `local.jmw.nz` resolved locally, so it works offline and never resolves the name through DNS. If you prefer a single line per event without the script, a plain `curl -s -m 1 -X POST -d '' https://local.jmw.nz:41443/haptic/completed` works too, but you lose the filters below.

## Codex CLI

Codex offers two ways to run a program, and the script handles both:

- **Hooks** (what the installer sets up): `Stop` at the end of each turn, `PermissionRequest` when Codex is about to ask for an approval. The event comes as JSON on stdin, like Claude Code.
- **`notify`**, the older setting in `~/.codex/config.toml`, for Codex versions without hooks. It only fires at the end of a turn (`agent-turn-complete`, the one type Codex sends), and Codex passes the JSON as the last argument, with nothing on stdin. It takes an absolute path, as Codex starts the program without a shell, and it must sit above the first `[table]` of the file:

  ```toml
  notify = ["/Users/you/.config/mx-dev-kit/bin/agent-haptics.sh", "codex"]
  ```

  Codex runs a single `notify` program, which is why the installer does not touch it. If you already have one, keep it: hooks and `notify` work side by side. Do not set both up for the vibration, or you will feel two per turn.

What Codex does not offer yet, so the kit does not do it:

- **No failed-turn event.** Codex has no counterpart to Claude Code's `StopFailure`. `PostModelRequest` reports each model request, retries included, and its output is only informative: an `angry_alert` there would also fire on errors Codex recovers from.
- **No "waiting for you" reminder.** `PermissionRequest` covers approvals; a question asked in plain text ends the turn and plays `completed`.
- **Headless runs are not told apart.** Neither the hooks nor `notify` say whether Codex runs interactively or through `codex exec`, so `MX_HAPTICS_ALL_SESSIONS` has no effect on Codex.

## Copilot CLI

GitHub Copilot CLI runs the hooks of every file in `~/.copilot/hooks/`, next to those of a repository (`.github/hooks/`). The kit uses four of its events:

- **`userPromptSubmitted`**, which notes when a turn starts. Its payload carries no session id, so turns are told apart by their folder.

- **`agentStop`** at the end of each turn of the main agent. Subagents have their own event, `subagentStop`, which stays silent.
- **`notification`**, which Copilot sends for several things. The script only vibrates for `permission_prompt` (a permission prompt is on screen) and `elicitation_dialog` (Copilot asks you something). The end of background agents and shell commands (`agent_idle`, `agent_completed`, `shell_completed`) stays silent.
- **`errorOccurred`**, only when the error is not `recoverable`: Copilot retries the others on its own.

Like Codex, Copilot does not say whether it runs interactively or through `copilot -p`, so `MX_HAPTICS_ALL_SESSIONS` has no effect on it.

## What the script filters

- **Headless runs stay silent.** `claude -p` and scripts built on the Agent SDK (`CLAUDE_CODE_ENTRYPOINT=sdk-*`) do not vibrate: nobody is waiting for them. `MX_HAPTICS_ALL_SESSIONS=1` turns this off.
- **The idle reminder is ignored.** Claude Code also sends a `Notification` when it has been waiting for a minute (`idle_prompt`), right after the `Stop` vibration. Set `MX_IDLE_REMINDER=1` to get that second tap.
- **Mute everything** with `touch ~/.config/mx-dev-kit/haptics.off` (delete the file to unmute). The plugin and the shell scripts honour it as well.

## Settings

Environment variables, read when the agent runs the hook (export them in your shell profile, before the agent starts):

| Variable | Default | |
|---|---|---|
| `MX_TURN_SECONDS` | `30` | Shortest turn that vibrates when it ends. `0`: every turn |
| `MX_WAVE_DONE` | `completed` | Turn finished |
| `MX_WAVE_ATTENTION` | `knock` | Waiting for you |
| `MX_WAVE_ERROR` | `angry_alert` | Turn failed |
| `MX_IDLE_REMINDER` | `0` | `1`: also tap after a minute of inactivity |
| `MX_HAPTICS_ALL_SESSIONS` | `0` | `1`: also vibrate for headless runs |
| `MX_HAPTIC_URL` | `https://local.jmw.nz:41443/haptic` | Haptic endpoint |

The 15 waveforms: `sharp_state_change`, `damp_state_change`, `sharp_collision`, `damp_collision`, `subtle_collision`, `happy_alert`, `angry_alert`, `completed`, `square`, `wave`, `firework`, `mad`, `knock`, `jingle`, `ringing`. Try them with `haptic <name>` (see [`bin/`](../bin)).

## Good to know

- If the Claude Desktop plugin from the Logi Marketplace is installed with its haptics on, you may feel two vibrations for one event: turn one of them off.
- The Gemini CLI hook format was checked against the documentation shipped with Gemini CLI 0.36. The Claude Code events were checked against Claude Code 2.1. The Codex CLI hooks and `notify` were checked against the Codex documentation of November 2026 (hooks page and advanced configuration) and simulated, not run inside Codex yet. The Copilot CLI hooks were checked against GitHub's hooks reference of November 2026 (Copilot CLI 1.0.108) and simulated, not run inside Copilot yet. Reports welcome.
