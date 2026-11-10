#!/bin/sh
# Adds the haptic hooks to Claude Code, Gemini CLI, Codex CLI and/or GitHub Copilot CLI.
#
#   hooks/install.sh                every agent found (~/.claude, ~/.gemini, ~/.codex, ~/.copilot)
#   hooks/install.sh --claude       Claude Code only
#   hooks/install.sh --gemini       Gemini CLI only
#   hooks/install.sh --codex        Codex CLI only
#   hooks/install.sh --copilot      Copilot CLI only
#   hooks/install.sh --dry-run      show the resulting settings without writing them
#
# An existing settings.json (hooks.json for Codex) is backed up next to itself before
# any change, and running the script twice does not add the hooks twice. Copilot CLI
# gets a file of its own, ~/.copilot/hooks/mx-dev-kit.json.

set -eu

here=$(cd "$(dirname "$0")" && pwd)
state="$HOME/.config/mx-dev-kit"
claude_dir=${CLAUDE_CONFIG_DIR:-$HOME/.claude}
gemini_dir="$HOME/.gemini"
codex_dir=${CODEX_HOME:-$HOME/.codex}
copilot_dir=${COPILOT_HOME:-$HOME/.copilot}

agents=""
dry_run=0
for arg in "$@"; do
  case $arg in
    --claude) agents="$agents claude" ;;
    --gemini) agents="$agents gemini" ;;
    --codex) agents="$agents codex" ;;
    --copilot) agents="$agents copilot" ;;
    --dry-run) dry_run=1 ;;
    -h | --help) sed -n '2,13s/^# \{0,1\}//p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ -z "$agents" ]; then
  [ -d "$claude_dir" ] || command -v claude >/dev/null 2>&1 && agents="$agents claude"
  [ -d "$gemini_dir" ] || command -v gemini >/dev/null 2>&1 && agents="$agents gemini"
  [ -d "$codex_dir" ] || command -v codex >/dev/null 2>&1 && agents="$agents codex"
  [ -d "$copilot_dir" ] || command -v copilot >/dev/null 2>&1 && agents="$agents copilot"
fi

if [ -z "$agents" ]; then
  echo "No coding agent found (looked for Claude Code, Gemini CLI, Codex CLI and Copilot CLI). Use --claude, --gemini, --codex or --copilot to force."
  exit 0
fi

. "$here/settings-json.sh"
json_tool_missing && exit 1

hook="$state/bin/agent-haptics.sh"
if [ "$dry_run" = 0 ]; then
  mkdir -p "$state/bin"
  cp "$here/agent-haptics.sh" "$hook"
  chmod +x "$hook"
fi

# Paths with spaces stay one argument once the agent runs the command through a shell.
quoted_hook="'$(printf '%s' "$hook" | sed "s/'/'\\\\''/g")'"

# The command line of one event, as a JSON string.
cmd() { json_string "$quoted_hook $1 $2"; }

entries_for() {
  case $1 in
    claude)
      printf '{"Stop":[{"hooks":[{"type":"command","command":%s,"timeout":5}]}],' "$(cmd claude Stop)"
      printf '"Notification":[{"hooks":[{"type":"command","command":%s,"timeout":5}]}],' "$(cmd claude Notification)"
      printf '"StopFailure":[{"hooks":[{"type":"command","command":%s,"timeout":5}]}]}' "$(cmd claude StopFailure)"
      ;;
    gemini)
      printf '{"AfterAgent":[{"hooks":[{"name":"mx-dev-kit-haptics","type":"command","command":%s,"timeout":5000}]}],' "$(cmd gemini AfterAgent)"
      printf '"Notification":[{"hooks":[{"name":"mx-dev-kit-haptics","type":"command","command":%s,"timeout":5000}]}]}' "$(cmd gemini Notification)"
      ;;
    codex)
      printf '{"Stop":[{"hooks":[{"type":"command","command":%s,"timeout":5}]}],' "$(cmd codex Stop)"
      printf '"PermissionRequest":[{"hooks":[{"type":"command","command":%s,"timeout":5}]}]}' "$(cmd codex PermissionRequest)"
      ;;
  esac
}

# settings.json.bak-20261110-093000, then -2, -3... if run twice in the same second.
backup_name() {
  b="$1.bak-$(date +%Y%m%d-%H%M%S)"
  n=1
  while [ -e "$b" ]; do n=$((n + 1)); b="$1.bak-$(date +%Y%m%d-%H%M%S)-$n"; done
  printf '%s' "$b"
}

# Copilot CLI loads every file of ~/.copilot/hooks: the kit's hooks get one of their own.
# Notification fires for more than the prompts that wait for you; the script keeps those.
copilot_file() {
  printf '{\n  "version": 1,\n  "hooks": {\n'
  printf '    "agentStop": [{ "type": "command", "bash": %s, "timeoutSec": 5 }],\n' "$(cmd copilot agentStop)"
  printf '    "notification": [{ "type": "command", "bash": %s, "timeoutSec": 5 }],\n' "$(cmd copilot notification)"
  printf '    "errorOccurred": [{ "type": "command", "bash": %s, "timeoutSec": 5 }]\n' "$(cmd copilot errorOccurred)"
  printf '  }\n}\n'
}

install_copilot() {
  file="$copilot_dir/hooks/mx-dev-kit.json"
  content=$(copilot_file)
  if [ "$dry_run" = 1 ]; then
    echo "== Copilot CLI: $file would be"
    printf '%s\n' "$content"
    return
  fi
  if [ -f "$file" ] && [ "$(cat "$file")" = "$content" ]; then
    echo "Copilot CLI: hooks already up to date in $file"
    return
  fi
  mkdir -p "$copilot_dir/hooks"
  if [ -e "$file" ]; then
    backup=$(backup_name "$file")
    cp -p "$file" "$backup"
    echo "Copilot CLI: backup saved to $backup"
  fi
  printf '%s\n' "$content" > "$file"
  echo "Copilot CLI: haptic hooks added to $file (Copilot reads it when it starts)"
}

for agent in $agents; do
  if [ "$agent" = copilot ]; then
    install_copilot
    continue
  fi

  case $agent in
    claude) settings="$claude_dir/settings.json"; name="Claude Code" ;;
    gemini) settings="$gemini_dir/settings.json"; name="Gemini CLI" ;;
    codex) settings="$codex_dir/hooks.json"; name="Codex CLI" ;;
  esac

  if ! json_check "$settings"; then
    echo "$name: $settings is not a valid JSON object, left untouched. Fix it, then run this again." >&2
    continue
  fi
  if ! result=$(json_merge "$settings" "$(entries_for "$agent")"); then
    echo "$name: could not merge the hooks into $settings, left untouched." >&2
    continue
  fi

  if [ "$dry_run" = 1 ]; then
    echo "== $name: $settings would become"
    printf '%s\n' "$result"
    continue
  fi

  if [ -s "$settings" ] && [ "$(json_format "$settings")" = "$result" ]; then
    echo "$name: hooks already up to date in $settings"
    continue
  fi

  mkdir -p "$(dirname "$settings")"
  if [ -e "$settings" ]; then
    backup=$(backup_name "$settings")
    cp -p "$settings" "$backup"
    echo "$name: backup saved to $backup"
  else
    # Created here: hooks/uninstall.sh deletes it again if nothing else ends up in it.
    mkdir -p "$state"
    grep -qxF "$settings" "$state/hooks-created" 2>/dev/null || printf '%s\n' "$settings" >> "$state/hooks-created"
  fi

  # Rewrite in place so the file keeps its permissions.
  printf '%s\n' "$result" > "$settings"
  echo "$name: haptic hooks added to $settings"
  if [ "$agent" = codex ]; then
    echo "$name: Codex skips new hooks until you trust them: open Codex and type /hooks once."
    # Same layer in hooks.json and config.toml: Codex runs both but warns at startup.
    if grep -qE '^[[:space:]]*\[\[?hooks[].]' "$codex_dir/config.toml" 2>/dev/null; then
      echo "$name: $codex_dir/config.toml also has [hooks] tables. Codex merges both files and warns at startup; move them to hooks.json to silence it."
    fi
  fi
done

[ "$dry_run" = 1 ] || echo "Hook script: $hook (its vibrations go through the Haptic Web Plugin)"
