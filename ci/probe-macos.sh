#!/bin/bash
# Checks, on macOS, the things DeskPicker relies on: a second Desktop copy started with
# 'open -n', which copy a browser sign-in link reaches, ways of handing the link to a chosen
# copy, and whether 'open --env GIT_CONFIG_GLOBAL=...' reaches Desktop.
# Only a made-up link is used, and links are never printed.
set -u
cd "$(dirname "$0")"
LINK='x-github-desktop-auth://oauth?code=deskpicker-ci-fake&state=deskpicker-ci-fake'
APP="/Applications/GitHub Desktop.app"
SUP="$HOME/Library/Application Support"
DEFAULT="$SUP/GitHub Desktop"
B="$SUP/GitHub Desktop-ProbeB"
C="$SUP/GitHub Desktop-ProbeC"
D="$SUP/GitHub Desktop-ProbeD"
GITCFG="$HOME/.deskpicker/probe.gitconfig"
SHOTS="${SHOTS:-/tmp/shots}"
mkdir -p "$B" "$C" "$D" "$(dirname "$GITCFG")" "$SHOTS"
printf '[user]\n\tname = DeskPicker Probe\n\temail = probe@example.invalid\n' > "$GITCFG"
swiftc -O -o /tmp/mac-helper mac-helper.swift -framework AppKit || exit 1
H=/tmp/mac-helper

RESULTS=()
result() { RESULTS+=("| $1 | $2 |"); echo "RESULT: $1 => $2"; }
# A check DeskPicker depends on: the job fails unless it says yes.
FAILED=0
must() { result "$1" "$2"; case "$2" in yes*) ;; *) FAILED=1; echo "::error::$1: $2" ;; esac; }
redact() { sed -E 's#x-github-desktop-auth://[^ ]*#<link>#g'; }
hits() { cat "$1"/logs/*.desktop.production.log 2>/dev/null | grep -c "Processing protocol url"; }
mains() { ps -axww -o pid=,args= | grep "GitHub Desktop.app/Contents/MacOS/GitHub Desktop" | grep -v -- '--type=' | grep -v grep; }
pid_of() { mains | grep -F -- "--user-data-dir=$1" | head -1 | awk '{print $1}'; }
default_pid() { mains | grep -v -- '--user-data-dir' | head -1 | awk '{print $1}'; }
waitlog() { for _ in $(seq 90); do ls "$1"/logs/*.desktop.production.log >/dev/null 2>&1 && return 0; sleep 1; done; return 1; }
shot() { screencapture -x "$SHOTS/$1.png" 2>/dev/null; }

result "handler before any launch" "$($H handler x-github-desktop-auth)"

echo "== start default copy, then copy B with open -n"
open -a "$APP"
waitlog "$DEFAULT" && result "default copy started" yes || result "default copy started" "no (no log)"
sleep 5
open -n -a "$APP" --args --user-data-dir="$B"
waitlog "$B" && must "copy B started alongside (open -n)" yes || must "copy B started alongside (open -n)" "no (no log)"
sleep 8
mains
result "main processes (no --type=)" "$(mains | wc -l | tr -d ' ')"
result "handler after launch" "$($H handler x-github-desktop-auth)"
shot 1-two-copies

echo "== browser-style link via open"
a0=$(hits "$DEFAULT"); b0=$(hits "$B")
open "$LINK"; sleep 8
a1=$(hits "$DEFAULT"); b1=$(hits "$B")
result "open link reached default copy" "$([ "$a1" -gt "$a0" ] && echo yes || echo no) ($a0->$a1)"
result "open link reached copy B" "$([ "$b1" -gt "$b0" ] && echo yes || echo no) ($b0->$b1)"
result "processes after open link" "$(mains | wc -l | tr -d ' ')"

echo "== relay 1: link as an argument (open -n ... --args --user-data-dir=B <link>)"
b0=$(hits "$B")
open -n -a "$APP" --args --user-data-dir="$B" "$LINK"; sleep 8
b1=$(hits "$B")
result "argv relay reached B" "$([ "$b1" -gt "$b0" ] && echo yes || echo no) ($b0->$b1)"

echo "== relay 2: Apple Event to copy B's process"
PB=$(pid_of "$B"); PA=$(default_pid)
b0=$(hits "$B"); a0=$(hits "$DEFAULT")
result "AESendMessage to B (pid ${PB:-none})" "$($H gurl "${PB:-0}" "$LINK")"
sleep 8
b1=$(hits "$B"); a1=$(hits "$DEFAULT")
result "Apple Event relay reached B" "$([ "$b1" -gt "$b0" ] && echo yes || echo no) ($b0->$b1)"
result "Apple Event relay also reached default" "$([ "$a1" -gt "$a0" ] && echo yes || echo no)"
shot 2-after-apple-event

if [ -n "${DESKPICKER_APP:-}" ]; then
  echo "== DeskPicker.app itself"
  DP="$DESKPICKER_APP/Contents/MacOS/DeskPicker"
  R=$("$DP" --ci-running); echo "$R"
  must "DeskPicker sees default copy running" "$(echo "$R" | grep -qxF "$DEFAULT" && echo yes || echo no)"
  must "DeskPicker sees copy B running" "$(echo "$R" | grep -qxF "$B" && echo yes || echo no)"
  a0=$(hits "$DEFAULT"); b0=$(hits "$B")
  "$DP" --ci-relay "$B" > /tmp/dprelay.out 2>&1 &
  DPP=$!
  sleep 6
  must "handler while DeskPicker waits" "$($H handler x-github-desktop-auth | grep -q DeskPicker.app && echo yes || echo no)"
  open "$LINK"
  wait $DPP; rc=$?
  sleep 6
  result "DeskPicker relay" "$(grep -E 'relayed|no link|relay' /tmp/dprelay.out | tail -1) (exit $rc)"
  must "DeskPicker relay reached B" "$([ "$(hits "$B")" -gt "$b0" ] && echo yes || echo no) ($b0->$(hits "$B"))"
  result "DeskPicker relay also reached default" "$([ "$(hits "$DEFAULT")" -gt "$a0" ] && echo yes || echo no)"
  must "handler after DeskPicker is done" "$($H handler x-github-desktop-auth | grep -q "GitHub Desktop.app" && echo "yes (Desktop)" || echo no)"
  shot 2b-after-deskpicker
fi

echo "== handler swap: a stand-in app catches the link"
rm -rf /tmp/caught
CATCHER="$HOME/Applications/Catcher.app"
mkdir -p "$HOME/Applications"; rm -rf "$CATCHER"
osacompile -o "$CATCHER" -e 'on open location u' -e 'do shell script "touch /tmp/caught"' -e 'end open location'
PL="$CATCHER/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier io.github.theo-erickson.deskpicker.probe" "$PL" 2>/dev/null \
  || /usr/libexec/PlistBuddy -c "Add :CFBundleIdentifier string io.github.theo-erickson.deskpicker.probe" "$PL"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes array" -c "Add :CFBundleURLTypes:0 dict" \
  -c "Add :CFBundleURLTypes:0:CFBundleURLName string probe" -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes array" \
  -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string x-github-desktop-auth" "$PL"
codesign --force --sign - "$CATCHER"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f -R -trusted "$CATCHER"
LSR=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
"$LSR" -dump 2>/dev/null | grep -i -B30 "x-github-desktop-auth" | grep -iE "^path:|bundle id:|claimed schemes|x-github-desktop-auth" | head -20
result "LSSetDefaultHandlerForURLScheme(catcher)" "$($H sethandler x-github-desktop-auth io.github.theo-erickson.deskpicker.probe)"
sleep 2
result "handler after LS swap" "$($H handler x-github-desktop-auth)"
result "NSWorkspace.setDefaultApplication(catcher)" "$($H setapp x-github-desktop-auth "$CATCHER")"
sleep 2
result "handler after NSWorkspace swap" "$($H handler x-github-desktop-auth)"
defaults read com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers 2>/dev/null | grep -B2 -A4 x-github-desktop-auth
open "$CATCHER"; sleep 3
result "handler after opening the catcher once" "$($H handler x-github-desktop-auth)"
$H setapp x-github-desktop-auth "$CATCHER" >/dev/null; sleep 2
result "handler after swap with catcher open" "$($H handler x-github-desktop-auth)"
a0=$(hits "$DEFAULT"); b0=$(hits "$B")
open "$LINK"; sleep 10
result "swapped handler caught the link" "$([ -f /tmp/caught ] && echo yes || echo no)"
result "default copy got it anyway" "$([ "$(hits "$DEFAULT")" -gt "$a0" ] && echo yes || echo no)"
shot 3-after-swap
open -n -a "$APP" --args --user-data-dir="$D"; waitlog "$D"; sleep 8
result "handler after starting another Desktop copy" "$($H handler x-github-desktop-auth)"
result "restore handler to Desktop" "$($H sethandler x-github-desktop-auth com.github.GitHubClient)"

echo "== link with the default copy closed (B and D open)"
kill "$(default_pid)" 2>/dev/null; sleep 5
result "main processes after closing default" "$(mains | wc -l | tr -d ' ')"
b0=$(hits "$B"); d0=$(hits "$D")
open "$LINK"; sleep 10
result "open link reached B (started first)" "$([ "$(hits "$B")" -gt "$b0" ] && echo yes || echo no)"
result "open link reached D (started last)" "$([ "$(hits "$D")" -gt "$d0" ] && echo yes || echo no)"
result "open link started a new default copy" "$([ -n "$(default_pid)" ] && echo yes || echo no)"

echo "== GIT_CONFIG_GLOBAL via open --env"
open -n -a "$APP" --env "GIT_CONFIG_GLOBAL=$GITCFG" --args --user-data-dir="$C"
waitlog "$C"; sleep 5
PC=$(pid_of "$C")
if [ -n "$PC" ] && sudo launchctl procinfo "$PC" 2>/dev/null | grep -q "GIT_CONFIG_GLOBAL => $GITCFG"; then
  must "GIT_CONFIG_GLOBAL in copy C's environment" yes
elif [ -n "$PC" ] && ps eww -o command= -p "$PC" | grep -qF "GIT_CONFIG_GLOBAL=$GITCFG"; then
  must "GIT_CONFIG_GLOBAL in copy C's environment" "yes (ps eww)"
else
  must "GIT_CONFIG_GLOBAL in copy C's environment" "no (pid ${PC:-none})"
  [ -n "$PC" ] && sudo launchctl procinfo "$PC" 2>/dev/null | grep -i -A3 'environment' | head -20
fi
shot 4-end

echo "== Desktop log lines about protocols (links redacted)"
for d in "$DEFAULT" "$B"; do echo "-- $d"; cat "$d"/logs/*.desktop.production.log 2>/dev/null | grep -i 'protocol\|url' | redact | tail -10; done

{
  echo "### macOS $(sw_vers -productVersion)"; echo; echo "| Check | Result |"; echo "|---|---|"
  printf '%s\n' "${RESULTS[@]}"; echo
} >> "${GITHUB_STEP_SUMMARY:-/dev/stdout}"
pkill -f "GitHub Desktop.app/Contents/MacOS/GitHub Desktop"
exit $FAILED
