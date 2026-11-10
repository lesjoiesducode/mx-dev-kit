#!/bin/sh
# Removes what install.sh added. Your config.json stays unless you pass --purge.
# install.sh leaves a copy of this script in ~/.config/mx-dev-kit.
#
#   ~/.config/mx-dev-kit/uninstall.sh            plugin, shell integration, hooks, scripts
#   ~/.config/mx-dev-kit/uninstall.sh --purge    also config.json and the rest of the folder
#
# The Haptic Web Plugin stays: other tools may use it. Uninstall it from Logi Options+.

set -eu

root=$(cd "$(dirname "$0")" && pwd)
state="$HOME/.config/mx-dev-kit"
purge=0
for arg in "$@"; do
  case $arg in
    --purge) purge=1 ;;
    -h | --help) sed -n '2,9s/^# \{0,1\}//p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ -z "${DOTNET_ROOT:-}" ] && command -v brew >/dev/null 2>&1 && [ -d "$(brew --prefix)/opt/dotnet/libexec" ]; then
  export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
fi
PATH="$PATH:$HOME/.dotnet/tools"

echo "== Plugin"
plugins="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
if [ -e "$plugins/MxDevKitPlugin.link" ]; then
  rm -f "$plugins/MxDevKitPlugin.link"
  echo "Removed the development link. Restart Logi Plugin Service (Logi Options+ settings) to unload it."
fi
if [ -d "$plugins/MxDevKit" ]; then
  # Contributors have LogiPluginTool. Otherwise the folder goes, as an uninstall from Options+ does.
  if command -v logiplugintool >/dev/null 2>&1 && logiplugintool uninstall MxDevKit >/dev/null 2>&1; then
    echo "Uninstalled MX Dev Kit from Logi Plugin Service."
  else
    rm -rf "$plugins/MxDevKit"
    open -g "loupedeck://plugin/MxDevKit/reload" 2>/dev/null || true
    echo "Removed $plugins/MxDevKit. If its actions still show in Logi Options+, restart Logi Plugin Service (Options+ settings)."
  fi
fi
if [ -d "$plugins/HapticWeb" ]; then
  echo "The Haptic Web Plugin stays installed: other tools may use it. Uninstall it from Logi Options+ if you do not need it."
fi

echo "== Shell"
for rc in "$HOME/.zshrc" "$HOME/.bashrc" "$HOME/.bash_profile"; do
  [ -e "$rc" ] && grep -q '>>> mx-dev-kit >>>' "$rc" || continue
  backup="$rc.bak-$(date +%Y%m%d-%H%M%S)"
  n=1
  while [ -e "$backup" ]; do n=$((n + 1)); backup="$rc.bak-$(date +%Y%m%d-%H%M%S)-$n"; done
  cp -p "$rc" "$backup"
  # Drops the block and the empty line install.sh put before it. Rewrites in place (keeps
  # permissions and symlinks).
  awk -v m="# >>> mx-dev-kit >>>" -v e="# <<< mx-dev-kit <<<" '
    skip { if ($0 == e) skip = 0; next }
    $0 == m { skip = 1; blank = 0; next }
    blank { print ""; blank = 0 }
    $0 == "" { blank = 1; next }
    { print }
    END { if (blank) print "" }
  ' "$backup" > "$rc"
  echo "Removed from $rc (backup: $backup)"
done

echo "== Hooks"
sh "$root/hooks/uninstall.sh" || echo "Remove the agent-haptics entries from ~/.claude/settings.json, ~/.gemini/settings.json and ~/.codex/hooks.json, and delete ~/.copilot/hooks/mx-dev-kit.json, by hand."

echo "== Files"
rm -rf "$state/bin" "$state/shell" "$state/hooks" "$state/preview" "$state/plugin.new" "$state/plugin.old" "$state/haptic-web.new"
rm -f "$state/config.example.json" "$state/current-dir" "$state/trigger" "$state/MxDevKit.lplug4" "$state/HapticWeb.lplug4.part" "$state/uninstall.sh"
if [ "$purge" = 1 ]; then
  rm -f "$state/config.json" "$state/haptics.off" "$state/notifications.off"
  rmdir "$state" 2>/dev/null || echo "Left $state (it still holds files you added)."
else
  echo "Kept $state/config.json (--purge removes it)."
fi
if command -v terminal-notifier >/dev/null 2>&1; then
  echo "terminal-notifier stays installed (brew uninstall terminal-notifier if you do not use it elsewhere)."
fi
