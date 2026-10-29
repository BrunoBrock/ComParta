#!/usr/bin/env bash
# Installs the APK on the emulator, starts ComParta and checks what epic 7 story 1 asks of a first
# Android build: the app starts its engine, the screen loads and the bridge answers it, and the
# app is still running afterwards; that received files have their place in Downloads (story 3); and
# that an invite tapped or shared in another app opens "Get this file?" (story 6).
# Leaves screenshots and the app's log next to the APK.
set -euo pipefail

folder="$1"
apk=$(ls "$folder"/*-Signed.apk | head -n 1)
package=org.comparta.app

adb install -r "$apk"
adb logcat -c
adb shell am start -W -n "$package/$package.MainActivity"

# How many times a line is in the app's log.
seen() {
  adb logcat -d -s ComParta:I | grep -c "$1" || true
}

# Waits until a line is in the app's log (at least the given number of times), or fails after the
# given number of seconds.
wait_for() {
  local text="$1" seconds="$2" times="${3:-1}"
  for _ in $(seq "$seconds"); do
    if [ "$(seen "$text")" -ge "$times" ]; then
      echo "Seen: $text"
      return 0
    fi
    sleep 1
  done
  echo "Never saw: $text (${times} times)"
  return 1
}

status=0
wait_for "ComParta started" 180 || status=1
wait_for "Screen answered" 120 || status=1
sleep 5
# The engine and storage start once, however often Android makes the screen.
starts=$(adb logcat -d -s ComParta:I | grep -c "ComParta started" || true)
if [ "$starts" -ne 1 ]; then
  echo "ComParta started $starts times."
  status=1
fi
# What arrives goes to Downloads/ComParta, which the app makes when it starts (story 3).
if ! adb shell ls -d /sdcard/Download/ComParta > /dev/null 2>&1; then
  echo "Downloads/ComParta was not made."
  status=1
fi
if ! adb shell pidof "$package" > /dev/null; then
  echo "ComParta is no longer running."
  status=1
fi

adb exec-out screencap -p > "$folder/android-first-screen.png" || true

# Story 6: an invite tapped in an email, and a chat message shared to ComParta from WhatsApp, open
# "Get this file?" in the running app; a message without an invite is answered plainly. Nothing
# starts a second copy of the engine.
link='magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a&dn=Natal.mp4&xl=1048576'
adb shell "am start -W -a android.intent.action.VIEW -d '$link' $package"
wait_for "Asked: Get this file?" 60 1 || status=1
adb exec-out screencap -p > "$folder/android-link-tapped.png" || true
# The confirmation stays open: a back press that lands before it shows would leave the app, which
# ends it when nothing is shared (AD-14). A new link replaces what the confirmation asks.
adb shell "am start -W -a android.intent.action.SEND -t text/plain --es android.intent.extra.TEXT 'Oi! Here is the video: $link see you' $package"
wait_for "Asked: Get this file?" 60 2 || status=1
adb shell "am start -W -a android.intent.action.SEND -t text/plain --es android.intent.extra.TEXT 'See you on Sunday' $package"
wait_for "Said: invite.notInMessage" 60 || status=1
starts=$(seen "ComParta started")
if [ "$starts" -ne 1 ]; then
  echo "ComParta started $starts times after links were opened."
  status=1
fi

# The same tapped link when ComParta is not running starts it and asks too.
adb logcat -d > "$folder/android-log.txt" || true
adb shell am force-stop "$package"
adb logcat -c
adb shell "am start -W -a android.intent.action.VIEW -d '$link' $package"
wait_for "ComParta started" 180 || status=1
wait_for "Asked: Get this file?" 120 || status=1
adb exec-out screencap -p > "$folder/android-link-cold.png" || true
adb logcat -d >> "$folder/android-log.txt" || true
grep -E "ComParta|comparta|FATAL|AndroidRuntime" "$folder/android-log.txt" | tail -n 60 || true
exit $status
