#!/bin/bash
# Checks, on Linux, the things DeskPicker relies on: a second Desktop copy with its own
# --user-data-dir, which copy a browser sign-in link reaches, whether handing the link to
# a chosen copy works, and whether GIT_CONFIG_GLOBAL reaches Desktop.
#   ci/probe-linux.sh native     the .deb (github-desktop on PATH)
#   ci/probe-linux.sh flatpak    the io.github.shiftey.Desktop Flatpak
# Run it inside a display and a D-Bus session (xvfb-run + dbus-run-session in CI).
# Only a made-up link is used, and links are never printed.
set -u
KIND="${1:-native}"
LINK='x-github-desktop-auth://oauth?code=deskpicker-ci-fake&state=deskpicker-ci-fake'
FLATPAK=io.github.shiftey.Desktop
if [ "$KIND" = flatpak ]; then
  DEFAULT="$HOME/.var/app/$FLATPAK/config/GitHub Desktop"
  run() { flatpak run "$FLATPAK" "$@"; }
  runenv() { flatpak run --env="$1" "$FLATPAK" "${@:2}"; }
else
  DEFAULT="$HOME/.config/GitHub Desktop"
  run() { github-desktop "$@"; }
  runenv() { env "$1" github-desktop "${@:2}"; }
fi
B="$HOME/.config/GitHub Desktop-ProbeB"
C="$HOME/.config/GitHub Desktop-ProbeC"
GITCFG="$HOME/.deskpicker/probe.gitconfig"
mkdir -p "$B" "$C" "$(dirname "$GITCFG")"
printf '[user]\n\tname = DeskPicker Probe\n\temail = probe@example.invalid\n' > "$GITCFG"

RESULTS=()
result() { RESULTS+=("| $1 | $2 |"); echo "RESULT: $1 => $2"; }
# A check DeskPicker depends on: the job fails unless it says yes.
FAILED=0
must() { result "$1" "$2"; case "$2" in yes*) ;; *) FAILED=1; echo "::error::$1: $2" ;; esac; }
redact() { sed -E 's#x-github-desktop-auth://[^ ]*#<link>#g'; }
hits() { cat "$1"/logs/*.desktop.production.log 2>/dev/null | grep -c "Processing protocol url"; }

# Command lines of every github-desktop process (main ones and Chromium helpers).
procs() {
  for d in /proc/[0-9]*; do
    a=$(tr '\0' ' ' < "$d/cmdline" 2>/dev/null) || continue
    case "$a" in *github-desktop*|*GitHub\ Desktop*|*"$FLATPAK"*) echo "${d#/proc/} $a" ;; esac
  done
}
mains() { procs | grep -v -- '--type=' ; }
pid_of() { mains | grep -F -- "--user-data-dir=$1" | grep -v -- 'bwrap\|flatpak run\|flatpak-spawn' | tail -1 | cut -d' ' -f1; }

waitlog() { for _ in $(seq 60); do ls "$1"/logs/*.desktop.production.log >/dev/null 2>&1 && return 0; sleep 1; done; return 1; }

echo "== handler"
H=$(xdg-mime query default x-scheme-handler/x-github-desktop-auth)
result "xdg-mime default handler" "${H:-none}"
for f in /usr/share/applications/"$H" "$HOME/.local/share/applications/$H" /var/lib/flatpak/exports/share/applications/"$H" "$HOME/.local/share/flatpak/exports/share/applications/$H"; do
  [ -f "$f" ] && result "handler Exec ($f)" "$(grep -m1 '^Exec=' "$f")"
done

echo "== start default copy, then copy B"
run >/tmp/a.out 2>&1 &
waitlog "$DEFAULT" && result "default copy started" yes || result "default copy started" "no (no log)"
sleep 5
run --user-data-dir="$B" >/tmp/b.out 2>&1 &
waitlog "$B" && must "copy B started alongside" yes || must "copy B started alongside" "no (no log)"
sleep 8
echo "-- main processes"; mains | redact
result "main processes (no --type=)" "$(mains | wc -l)"
mains | grep -F -- "--user-data-dir=$B" | cut -d' ' -f2- | cut -c1-160 | sort -u | while read -r l; do echo "copy B process: $l"; done
result "copy B processes (no --type=)" "$(mains | grep -F -- "--user-data-dir=$B" | cut -d' ' -f2 | sort | uniq -c | tr -s ' ' | tr '\n' ';')"

echo "== browser-style link via xdg-open, watching /proc for the launcher"
( end=$((SECONDS+15)); while [ $SECONDS -lt $end ]; do procs; sleep 0.05; done ) | sort -u -k1,1n > /tmp/seen.txt &
WATCH=$!
sleep 1
a0=$(hits "$DEFAULT"); b0=$(hits "$B")
xdg-open "$LINK" >/tmp/xdg.out 2>&1
sleep 10
wait $WATCH
a1=$(hits "$DEFAULT"); b1=$(hits "$B")
result "xdg-open link reached default copy" "$([ "$a1" -gt "$a0" ] && echo yes || echo no) ($a0->$a1)"
result "xdg-open link reached copy B" "$([ "$b1" -gt "$b0" ] && echo yes || echo no) ($b0->$b1)"
L=$(grep -F 'x-github-desktop-auth://' /tmp/seen.txt | grep -v -- '--type=')
result "processes seen in /proc with link in argv" "$(echo "$L" | grep -c .)"
echo "$L" | while read -r _ cmd; do [ -n "$cmd" ] && echo "link-carrying process: $(echo "$cmd" | redact | cut -c1-220)"; done
echo "$L" | while read -r _ cmd; do [ -n "$cmd" ] && echo "\`$(echo "$cmd" | redact | cut -c1-160)\`"; done | sort -u > /tmp/linkprocs.txt
while read -r l; do result "link-carrying process" "$l"; done < /tmp/linkprocs.txt

echo "== relay: hand the link to copy B directly"
b0=$(hits "$B"); a0=$(hits "$DEFAULT")
run --user-data-dir="$B" "$LINK" >/tmp/relay.out 2>&1
sleep 8
b1=$(hits "$B"); a1=$(hits "$DEFAULT")
result "relay to B reached B" "$([ "$b1" -gt "$b0" ] && echo yes || echo no) ($b0->$b1)"
result "relay to B also reached default" "$([ "$a1" -gt "$a0" ] && echo yes || echo no)"

if [ -n "${DESKPICKER:-}" ]; then
  echo "== DeskPicker itself"
  R=$("$DESKPICKER" --ci-running)
  echo "$R"
  must "DeskPicker sees default copy running" "$(echo "$R" | grep -qxF "$DEFAULT" && echo yes || echo no)"
  must "DeskPicker sees copy B running" "$(echo "$R" | grep -qxF "$B" && echo yes || echo no)"
  result "DeskPicker running folders" "$(echo "$R" | grep -c .)"
  b0=$(hits "$B")
  "$DESKPICKER" --ci-relay "$B" > /tmp/dprelay.out 2>&1 &
  DP=$!
  sleep 3
  xdg-open "$LINK" >/dev/null 2>&1
  wait $DP; rc=$?
  sleep 8
  result "DeskPicker relay" "$(tail -1 /tmp/dprelay.out) (exit $rc)"
  must "DeskPicker relay reached B" "$([ "$(hits "$B")" -gt "$b0" ] && echo yes || echo no) ($b0->$(hits "$B"))"
fi

echo "== GIT_CONFIG_GLOBAL"
runenv "GIT_CONFIG_GLOBAL=$GITCFG" --user-data-dir="$C" >/tmp/c.out 2>&1 &
waitlog "$C"; sleep 5
# Chromium rewrites /proc/<pid>/environ of its own processes (for the process title), so
# also look at the ones it doesn't: anything of copy C's that has the variable got it from
# the main process.
P=$(pid_of "$C"); n=0; tot=0
for d in /proc/[0-9]*; do
  tr '\0' ' ' < "$d/cmdline" 2>/dev/null | grep -qF -- "$C" || continue
  tot=$((tot+1))
  tr '\0' '\n' < "$d/environ" 2>/dev/null | grep -qx "GIT_CONFIG_GLOBAL=$GITCFG" && n=$((n+1))
done
result "GIT_CONFIG_GLOBAL in copy C's /proc environ" "$n of $tot (main pid ${P:-none})"
if [ "$KIND" = flatpak ]; then
  # --env reaches the sandbox, and Desktop inherits it like any process there.
  V=$(flatpak run --env="GIT_CONFIG_GLOBAL=$GITCFG" --command=sh "$FLATPAK" -c 'printenv GIT_CONFIG_GLOBAL' 2>/dev/null)
  must "flatpak run --env reaches the sandbox" "$([ "$V" = "$GITCFG" ] && echo yes || echo "no ($V)")"
else
  # Chromium wipes its environ for the process title, so watch what Desktop's main process
  # passes to the processes it starts (Chromium's helpers) instead.
  if command -v strace >/dev/null; then
    E="$HOME/.config/GitHub Desktop-ProbeE"
    timeout 25 strace -f -qq -e trace=execve -e signal=none -v -s 400 -o /tmp/st.txt \
      env "GIT_CONFIG_GLOBAL=$GITCFG" github-desktop --no-sandbox --user-data-dir="$E" >/dev/null 2>&1
    ex=$(grep -c 'execve(' /tmp/st.txt); wi=$(grep 'execve(' /tmp/st.txt | grep -cF "GIT_CONFIG_GLOBAL=$GITCFG")
    must "execs from a Desktop started with GIT_CONFIG_GLOBAL that pass it on" "$([ "$wi" -gt 0 ] && [ "$wi" = "$ex" ] && echo yes || echo no) ($wi of $ex)"
  fi
fi
if [ "$KIND" = flatpak ]; then
  result "gitconfig readable inside the sandbox" "$(flatpak run --command=cat "$FLATPAK" "$GITCFG" >/dev/null 2>&1 && echo yes || echo no)"
fi

echo "== Desktop log lines about protocols (links redacted)"
for d in "$DEFAULT" "$B"; do echo "-- $d"; cat "$d"/logs/*.desktop.production.log 2>/dev/null | grep -i 'protocol\|url' | redact | tail -10; done

{
  echo "### Linux ($KIND)"; echo; echo "| Check | Result |"; echo "|---|---|"
  printf '%s\n' "${RESULTS[@]}"; echo
} >> "${GITHUB_STEP_SUMMARY:-/dev/stdout}"
pkill -f github-desktop; pkill -f "$FLATPAK"
exit $FAILED
