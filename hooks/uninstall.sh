#!/bin/sh
# Removes the haptic hooks added by hooks/install.sh from Claude Code, Gemini CLI, Codex CLI
# and Copilot CLI.
# Other hooks are kept. Each settings.json (hooks.json for Codex) is backed up before it changes.

set -eu

state="$HOME/.config/mx-dev-kit"
claude_dir=${CLAUDE_CONFIG_DIR:-$HOME/.claude}
codex_dir=${CODEX_HOME:-$HOME/.codex}
copilot_dir=${COPILOT_HOME:-$HOME/.copilot}

here=$(cd "$(dirname "$0")" && pwd)
. "$here/settings-json.sh"
json_tool_missing && exit 1

backup_name() {
  b="$1.bak-$(date +%Y%m%d-%H%M%S)"
  n=1
  while [ -e "$b" ]; do n=$((n + 1)); b="$1.bak-$(date +%Y%m%d-%H%M%S)-$n"; done
  printf '%s' "$b"
}

for settings in "$claude_dir/settings.json" "${GEMINI_CLI_HOME:-$HOME}/.gemini/settings.json" "$codex_dir/hooks.json"; do
  [ -s "$settings" ] || continue
  grep -q agent-haptics "$settings" || continue
  if ! json_check "$settings" || ! result=$(json_strip "$settings"); then
    echo "$settings is not a valid JSON object, left untouched." >&2
    continue
  fi

  if [ "$result" = '{}' ] && grep -qxF "$settings" "$state/hooks-created" 2>/dev/null; then
    rm -f "$settings"
    echo "Removed $settings (hooks/install.sh had created it for the hooks alone)"
    continue
  fi

  backup=$(backup_name "$settings")
  cp -p "$settings" "$backup"
  printf '%s\n' "$result" > "$settings"
  echo "Hooks removed from $settings (backup: $backup)"
done

# Copilot CLI: the file is the kit's own, nothing else lives in it.
copilot_file="$copilot_dir/hooks/mx-dev-kit.json"
if [ -f "$copilot_file" ] && grep -q agent-haptics "$copilot_file"; then
  rm -f "$copilot_file"
  rmdir "$copilot_dir/hooks" 2>/dev/null || true
  echo "Removed $copilot_file"
fi

rm -f "$state/bin/agent-haptics.sh" "$state/hooks-created"
