# MX Dev Kit: shell integration for zsh. Source it from ~/.zshrc.
#
#   - tells the MX Dev Kit plugin which folder you are working in
#   - commands that run longer than MX_LONG_COMMAND_SECONDS vibrate when they end
#   - buzz <command>        vibrates when the command ends, whatever its duration
#   - ci-watch [sha]        waits for the GitHub Actions runs of a commit, vibrates at the end
#   - haptic, mx-notify, uptime-watch, docker-watch  added to PATH (see bin/)
#
# Settings, before the source line:
#   MX_LONG_COMMAND_SECONDS=30          0 turns long-command vibrations off
#   MX_LONG_COMMAND_IGNORE="vim ssh"    commands (first word) that never vibrate
#   MX_HAPTIC_URL=https://local.jmw.nz:41443/haptic

[[ -o interactive ]] || return 0

typeset -g MX_DEV_KIT_DIR=${${(%):-%x}:A:h:h}
typeset -g MX_STATE_DIR=${MX_STATE_DIR:-$HOME/.config/mx-dev-kit}
typeset -g MX_HAPTIC_URL=${MX_HAPTIC_URL:-https://local.jmw.nz:41443/haptic}
typeset -g MX_LONG_COMMAND_SECONDS=${MX_LONG_COMMAND_SECONDS:-30}
typeset -g MX_LONG_COMMAND_IGNORE=${MX_LONG_COMMAND_IGNORE:-"vim nvim vi nano emacs micro less more man ssh mosh tmux screen top htop btop watch tail claude gemini codex"}

[[ -d $MX_DEV_KIT_DIR/bin ]] && path=("$MX_DEV_KIT_DIR/bin" ${path:#$MX_DEV_KIT_DIR/bin})
mkdir -p "$MX_STATE_DIR" 2>/dev/null

zmodload zsh/datetime

# Non-blocking: the prompt never waits for the mouse.
mx_haptic() {
  [[ -e $MX_STATE_DIR/haptics.off ]] && return 0
  curl -s -m 2 -o /dev/null -X POST -d '' --resolve local.jmw.nz:41443:127.0.0.1 \
    "${MX_HAPTIC_URL%/}/$1" &>/dev/null &!
}

buzz() {
  "$@"
  local code=$?
  typeset -g _mx_buzzed=1
  if (( code == 0 )); then mx_haptic completed; else mx_haptic angry_alert; fi
  return $code
}

ci-watch() {
  if ! command -v gh >/dev/null; then
    print -u2 "ci-watch: the GitHub CLI (gh) is not installed"
    return 1
  fi

  local sha=${1:-$(git --no-optional-locks rev-parse HEAD 2>/dev/null)} i id failed=0
  local -a ids
  if [[ -z $sha ]]; then
    print -u2 "ci-watch: not in a git repository"
    return 1
  fi

  print "Waiting for the workflow runs of ${sha[1,7]}..."
  for i in {1..24}; do
    ids=(${(f)"$(gh run list --commit "$sha" --json databaseId -q '.[].databaseId' 2>/dev/null)"})
    (( $#ids )) && break
    sleep 5
  done

  if (( ! $#ids )); then
    print -u2 "ci-watch: no workflow run for ${sha[1,7]} after two minutes"
    return 1
  fi

  for id in $ids; do
    gh run watch "$id" --exit-status || failed=1
  done

  typeset -g _mx_buzzed=1
  if (( failed )); then mx_haptic angry_alert; else mx_haptic happy_alert; fi
  return $failed
}

_mx_preexec() {
  typeset -g _mx_cmd=$1 _mx_start=$EPOCHSECONDS
}

_mx_precmd() {
  local code=$? buzzed=${_mx_buzzed-} start=${_mx_start-} cmd=${_mx_cmd-}
  unset _mx_buzzed _mx_start _mx_cmd

  # Rewritten at every prompt: the plugin follows the last folder used.
  print -r -- "$PWD" >| "$MX_STATE_DIR/current-dir" 2>/dev/null

  [[ -n $start && -z $buzzed ]] || return 0
  (( MX_LONG_COMMAND_SECONDS > 0 && EPOCHSECONDS - start >= MX_LONG_COMMAND_SECONDS )) || return 0
  (( code == 130 || code == 148 )) && return 0   # Ctrl-C, Ctrl-Z

  local first=${${(z)cmd}[1]}
  (( ${${=MX_LONG_COMMAND_IGNORE}[(Ie)$first]} )) && return 0

  if (( code == 0 )); then mx_haptic completed; else mx_haptic angry_alert; fi
}

autoload -Uz add-zsh-hook
add-zsh-hook preexec _mx_preexec
add-zsh-hook precmd _mx_precmd
