# Shell integration

One file to source from your shell profile. It gives you four things:

| | What happens |
|---|---|
| Project tracking | Every prompt writes the current folder to `current-dir`, so the plugin's bubbles follow the project you are in. |
| Long commands | Any command that runs for 30 seconds or more vibrates when it ends: `completed` on success, `angry_alert` on failure. No prefix to remember. |
| `buzz <command>` | Same thing for one command, whatever its duration: `buzz make test`, `buzz docker compose build`. |
| `ci-watch [sha]` | Waits for the GitHub Actions runs of a commit (HEAD by default), follows them with `gh run watch`, then vibrates: `happy_alert` when everything passed, `angry_alert` otherwise. `git push && ci-watch` and you can look away. |

It also puts [`haptic`, `mx-notify`, `uptime-watch` and `docker-watch`](../bin) on your PATH.

Vibrations need an MX Master 4 with the [Haptic Web Plugin](https://marketplace.logi.com/plugin/HapticWeb/en). Project tracking works with any Actions Ring device.

## Install

`./install.sh` (or `.\install.ps1`) does it for you, with a backup of your profile. By hand:

| Shell | Add to | Line |
|---|---|---|
| zsh | `~/.zshrc` | `source ~/.config/mx-dev-kit/shell/mx.zsh` |
| bash (macOS) | `~/.bash_profile` | `source ~/.config/mx-dev-kit/shell/mx.bash` |
| Git Bash (Windows) | `~/.bashrc` | `source "$APPDATA/mx-dev-kit/shell/mx.bash"` |
| PowerShell (Windows) | `$PROFILE` | `. "$env:APPDATA\mx-dev-kit\shell\mx.ps1"` |

You can also source the files straight from this repository (`shell/mx.zsh`).

Windows (Git Bash and PowerShell): untested on Windows, please test before relying on it, issues and PRs welcome.

## Settings

Set them before the `source` line.

| Variable | Default | |
|---|---|---|
| `MX_LONG_COMMAND_SECONDS` | `30` | Threshold for long commands. `0` turns the feature off. |
| `MX_LONG_COMMAND_IGNORE` | editors, pagers, `ssh`, `tmux`, `top`, `watch`, `tail`, `claude`, `gemini`, `codex` | First words that never vibrate (interactive programs you leave open). |
| `MX_HAPTIC_URL` | `https://local.jmw.nz:41443/haptic` | Haptic endpoint. |

Commands stopped with Ctrl-C or suspended with Ctrl-Z do not vibrate. `touch ~/.config/mx-dev-kit/haptics.off` mutes everything.

## Notes

- zsh uses `preexec`/`precmd` hooks and plays well with prompt themes.
- bash uses [bash-preexec](https://github.com/rcaloras/bash-preexec) when it is loaded. Otherwise it sets its own `DEBUG` trap; if another tool already owns that trap, long-command vibrations are skipped and everything else still works. Tested with the bash 3.2 that ships with macOS.
- PowerShell wraps your `prompt` function: source the file after any prompt theme (oh-my-posh, Starship). The previous status is passed on, so themes still colour failed commands.
- The vibration request runs in the background: your prompt never waits for the mouse.
