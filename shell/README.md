# Shell integration

One file to source from your shell profile. It gives you four things:

| | What happens |
|---|---|
| Project tracking | Every prompt writes the current folder to `current-dir`, so the plugin's bubbles follow the project you are in, and the terminal app to `current-terminal`, so the agent bubbles open the terminal you use. |
| Long commands | Any command that runs for 30 seconds or more vibrates when it ends: `completed` on success, `angry_alert` on failure. No prefix to remember. |
| `buzz <command>` | Same thing for one command, whatever its duration: `buzz make test`, `buzz docker compose build`. |
| `ci-watch [sha]` | Waits for the GitHub Actions runs of a commit (HEAD by default), follows them with `gh run watch`, then vibrates: `happy_alert` when everything passed, `angry_alert` otherwise. `git push && ci-watch` and you can look away. |

It also puts [`haptic`, `mx-notify`, `uptime-watch` and `docker-watch`](../bin) on your PATH.

Vibrations need an MX Master 4 with the [Haptic Web Plugin](https://marketplace.logi.com/plugin/HapticWeb/en). Project tracking works with any Actions Ring device.

## Install

`./install.sh` (or `.\install.ps1`) does it for you, in the file your shell reads, with a backup of your profile. A shell it does not know (nushell, tcsh...) gets nothing: the installer says so. By hand:

| Shell | Add to | Line |
|---|---|---|
| zsh | `~/.zshrc` (`$ZDOTDIR/.zshrc` when it is set) | `source ~/.config/mx-dev-kit/shell/mx.zsh` |
| bash (macOS) | `~/.bash_profile` (or the `~/.bash_login` or `~/.profile` you have) | `source ~/.config/mx-dev-kit/shell/mx.bash` |
| fish 3.1+ | `~/.config/fish/conf.d/mx-dev-kit.fish` | `source ~/.config/mx-dev-kit/shell/mx.fish` |
| Git Bash (Windows) | `~/.bashrc` | `source "$APPDATA/mx-dev-kit/shell/mx.bash"` |
| PowerShell (Windows) | `$PROFILE.CurrentUserAllHosts` (`profile.ps1`), for Windows PowerShell and PowerShell 7 | `. "$env:APPDATA\mx-dev-kit\shell\mx.ps1"` |

You can also source the files straight from this repository (`shell/mx.zsh`).

## Settings

Set them before the `source` line (`set -g` in fish).

| Variable | Default | |
|---|---|---|
| `MX_LONG_COMMAND_SECONDS` | `30` | Threshold for long commands. `0` turns the feature off. |
| `MX_LONG_COMMAND_IGNORE` | editors, pagers, `ssh`, `tmux`, `top`, `watch`, `tail`, `claude`, `gemini`, `codex` | First words that never vibrate (interactive programs you leave open). |
| `MX_HAPTIC_URL` | `https://local.jmw.nz:41443/haptic` | Haptic endpoint. |

Commands stopped with Ctrl-C or suspended with Ctrl-Z do not vibrate. `touch ~/.config/mx-dev-kit/haptics.off` mutes everything.

## Notes

- zsh uses `preexec`/`precmd` hooks and plays well with prompt themes.
- fish uses the `fish_prompt` and `fish_postexec` events and `$CMD_DURATION`. The installer puts a file of its own in `conf.d`, so `config.fish` is never edited.
- bash uses [bash-preexec](https://github.com/rcaloras/bash-preexec) when it is loaded. Otherwise it sets its own `DEBUG` trap; if another tool already owns that trap, long-command vibrations are skipped and everything else still works. Tested with the bash 3.2 that ships with macOS.
- PowerShell wraps your `prompt` function: source the file after any prompt theme (oh-my-posh, Starship). The previous status is passed on, so themes still colour failed commands.
- The vibration request runs in the background: your prompt never waits for the mouse. On Windows it goes through the `curl.exe` of Windows 10 and 11, without DNS, like on macOS.
