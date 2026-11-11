# MX Dev Kit: shell integration for fish (3.1 or later). The installer loads it from
# ~/.config/fish/conf.d/mx-dev-kit.fish; by hand, source it from ~/.config/fish/config.fish.
#
#   - tells the MX Dev Kit plugin which folder and which terminal app you are working in
#   - commands that run longer than MX_LONG_COMMAND_SECONDS vibrate when they end
#   - buzz <command>        vibrates when the command ends, whatever its duration
#   - ci-watch [sha]        waits for the GitHub Actions runs of a commit, vibrates at the end
#   - haptic, mx-notify, uptime-watch, docker-watch  added to PATH (see bin/)
#
# Settings, before the source line (set -g or set -gx):
#   MX_LONG_COMMAND_SECONDS 30          0 turns long-command vibrations off
#   MX_LONG_COMMAND_IGNORE "vim ssh"    commands (first word) that never vibrate
#   MX_HAPTIC_URL https://local.jmw.nz:41443/haptic

# In a file being sourced, exit only stops reading it.
status is-interactive; or exit 0

set -g MX_DEV_KIT_DIR (dirname (dirname (realpath (status --current-filename))))
set -q MX_STATE_DIR; or set -g MX_STATE_DIR $HOME/.config/mx-dev-kit
set -q MX_HAPTIC_URL; or set -g MX_HAPTIC_URL https://local.jmw.nz:41443/haptic
set -q MX_LONG_COMMAND_SECONDS; or set -g MX_LONG_COMMAND_SECONDS 30
set -q MX_LONG_COMMAND_IGNORE; or set -g MX_LONG_COMMAND_IGNORE "vim nvim vi nano emacs micro less more man ssh mosh tmux screen top htop btop watch tail claude gemini codex"

if test -d $MX_DEV_KIT_DIR/bin; and not contains -- $MX_DEV_KIT_DIR/bin $PATH
    set -gx PATH $MX_DEV_KIT_DIR/bin $PATH
end
mkdir -p $MX_STATE_DIR 2>/dev/null

# The terminal app, for the agent bubbles: TERM_PROGRAM, and the bundle id of the app (it survives tmux).
set -g __mx_terminal (string join \t -- "$TERM_PROGRAM" "$__CFBundleIdentifier")

# Non-blocking: the prompt never waits for the mouse. sh starts curl and returns at once, so fish
# keeps no background job to report.
function mx_haptic
    test -e $MX_STATE_DIR/haptics.off; and return 0
    command sh -c 'curl -s -m 2 -o /dev/null -X POST -d "" --resolve local.jmw.nz:41443:127.0.0.1 "$1" >/dev/null 2>&1 &' \
        sh (string replace -r '/$' '' -- $MX_HAPTIC_URL)/$argv[1]
end

function buzz
    $argv
    set -l code $status
    set -g __mx_buzzed 1
    if test $code -eq 0
        mx_haptic completed
    else
        mx_haptic angry_alert
    end
    return $code
end

function ci-watch
    if not command -q gh
        echo "ci-watch: the GitHub CLI (gh) is not installed" >&2
        return 1
    end

    set -l sha $argv[1]
    test -n "$sha"; or set sha (git --no-optional-locks rev-parse HEAD 2>/dev/null)
    if test -z "$sha"
        echo "ci-watch: not in a git repository" >&2
        return 1
    end

    echo "Waiting for the workflow runs of "(string sub -l 7 -- $sha)"..."
    set -l ids
    for i in (seq 24)
        set ids (gh run list --commit $sha --json databaseId -q '.[].databaseId' 2>/dev/null)
        test (count $ids) -gt 0; and break
        sleep 5
    end

    if test (count $ids) -eq 0
        echo "ci-watch: no workflow run for "(string sub -l 7 -- $sha)" after two minutes" >&2
        return 1
    end

    set -l failed 0
    for id in $ids
        gh run watch $id --exit-status; or set failed 1
    end

    set -g __mx_buzzed 1
    if test $failed -eq 1
        mx_haptic angry_alert
    else
        mx_haptic happy_alert
    end
    return $failed
end

# Rewritten at every prompt: the plugin follows the last folder and terminal used.
function __mx_prompt --on-event fish_prompt
    printf '%s\n' $PWD >$MX_STATE_DIR/current-dir 2>/dev/null
    printf '%s\n' $__mx_terminal >$MX_STATE_DIR/current-terminal 2>/dev/null
end

# Right after each command: its line in $argv, its status in $status, its duration in $CMD_DURATION (ms).
function __mx_postexec --on-event fish_postexec
    set -l code $status
    set -l buzzed $__mx_buzzed
    set -e __mx_buzzed
    test -z "$buzzed"; or return 0

    set -l cmd (string trim -- "$argv[1]")
    test -n "$cmd"; or return 0
    test "$MX_LONG_COMMAND_SECONDS" -gt 0 2>/dev/null; or return 0
    test "$CMD_DURATION" -ge (math "$MX_LONG_COMMAND_SECONDS * 1000"); or return 0
    contains -- $code 130 148; and return 0 # Ctrl-C, Ctrl-Z

    set -l first (string split ' ' -- $cmd)[1]
    contains -- $first (string split ' ' -- $MX_LONG_COMMAND_IGNORE); and return 0

    if test $code -eq 0
        mx_haptic completed
    else
        mx_haptic angry_alert
    end
end
