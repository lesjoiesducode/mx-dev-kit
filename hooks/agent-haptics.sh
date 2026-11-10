#!/bin/sh
# Vibrates the MX Master 4 when a coding agent needs you, through the Haptic Web Plugin.
# The agent calls it from its hooks with its name and the event:
#
#   agent-haptics.sh claude Stop           turn finished           completed
#   agent-haptics.sh claude Notification   waiting for you         knock
#   agent-haptics.sh claude StopFailure    turn failed (API, limit) angry_alert
#   agent-haptics.sh gemini AfterAgent     turn finished           completed
#   agent-haptics.sh gemini Notification   waiting for you         knock
#   agent-haptics.sh codex Stop            turn finished           completed
#   agent-haptics.sh codex PermissionRequest  approval needed      knock
#   agent-haptics.sh codex '<json>'        turn finished (notify)  completed
#   agent-haptics.sh copilot agentStop     turn finished           completed
#   agent-haptics.sh copilot notification  permission or question  knock
#   agent-haptics.sh copilot errorOccurred error it cannot recover angry_alert
#
# Hooks get the event as JSON on stdin. Codex's older `notify` program gets it as the
# last argument instead, {"type":"agent-turn-complete",...}, with nothing on stdin.
#
# Environment:
#   MX_WAVE_DONE, MX_WAVE_ATTENTION, MX_WAVE_ERROR   other waveforms
#   MX_IDLE_REMINDER=1          also knock when Claude Code has been idle for a minute
#   MX_HAPTICS_ALL_SESSIONS=1   also vibrate for headless runs (claude -p, Agent SDK)
#   MX_HAPTIC_URL               endpoint, default https://local.jmw.nz:41443/haptic

# A terminal on stdin means a run by hand: do not wait for input that never comes.
if [ -t 0 ]; then input=; else input=$(cat); fi
agent=${1:-}
event=${2:-}

if [ -n "${APPDATA:-}" ] && [ -d "${APPDATA:-}" ]; then
  state="$APPDATA/mx-dev-kit"
else
  state="$HOME/.config/mx-dev-kit"
fi

# Gemini CLI reads a JSON object on stdout; Claude Code and Codex are fine with nothing.
done_() {
  [ "$agent" = gemini ] && printf '{}\n'
  exit 0
}

[ -e "$state/haptics.off" ] && done_

if [ "$agent" = codex ]; then
  case $event in
    "{"*)
      type=$(printf '%s' "$event" | sed -n 's/.*"type"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
      event=notify:$type
      ;;
  esac
fi

case "$agent:$event" in
  claude:Stop | gemini:AfterAgent | codex:Stop | codex:notify:agent-turn-complete)
    wave=${MX_WAVE_DONE:-completed}
    ;;
  claude:Notification | gemini:Notification)
    type=$(printf '%s' "$input" | sed -n 's/.*"notification_type"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
    case $type in
      idle_prompt) [ "${MX_IDLE_REMINDER:-0}" = 1 ] || done_ ;;
      auth_success) done_ ;;
    esac
    wave=${MX_WAVE_ATTENTION:-knock}
    ;;
  codex:PermissionRequest)
    wave=${MX_WAVE_ATTENTION:-knock}
    ;;
  copilot:agentStop)
    wave=${MX_WAVE_DONE:-completed}
    ;;
  copilot:notification)
    # Only what waits for you: a permission prompt on screen, or a question. Not the end of
    # background agents and shell commands.
    type=$(printf '%s' "$input" | sed -n 's/.*"notification_type"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
    case $type in
      permission_prompt | elicitation_dialog) ;;
      *) done_ ;;
    esac
    wave=${MX_WAVE_ATTENTION:-knock}
    ;;
  copilot:errorOccurred)
    # Copilot retries the errors it can recover from: only the others end the turn.
    printf '%s' "$input" | grep -q '"recoverable"[[:space:]]*:[[:space:]]*true' && done_
    wave=${MX_WAVE_ERROR:-angry_alert}
    ;;
  claude:StopFailure)
    wave=${MX_WAVE_ERROR:-angry_alert}
    ;;
  *)
    done_
    ;;
esac

# Headless sessions (claude -p, scripts using the Agent SDK) have no one waiting for them.
if [ "$agent" = claude ] && [ "${MX_HAPTICS_ALL_SESSIONS:-0}" != 1 ]; then
  case ${CLAUDE_CODE_ENTRYPOINT:-cli} in
    sdk-*) done_ ;;
  esac
fi

url=${MX_HAPTIC_URL:-https://local.jmw.nz:41443/haptic}
curl -s -m 2 -o /dev/null -X POST -d '' --resolve local.jmw.nz:41443:127.0.0.1 "${url%/}/$wave" >/dev/null 2>&1 &
done_
