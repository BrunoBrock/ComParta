#!/usr/bin/env bash
# Installs the APK on the emulator, starts ComParta and checks what epic 7 story 1 asks of a first
# Android build: the app starts its engine, the screen loads and the bridge answers it, and the
# app is still running afterwards. Leaves a screenshot and the app's log next to the APK.
set -euo pipefail

folder="$1"
apk=$(ls "$folder"/*-Signed.apk | head -n 1)
package=org.comparta.app

adb install -r "$apk"
adb logcat -c
adb shell am start -W -n "$package/$package.MainActivity"

# Waits for a line in the app's log, or fails after the given number of seconds.
wait_for() {
  local text="$1" seconds="$2"
  for _ in $(seq "$seconds"); do
    if adb logcat -d -s ComParta:I | grep -q "$text"; then
      echo "Seen: $text"
      return 0
    fi
    sleep 1
  done
  echo "Never saw: $text"
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
if ! adb shell pidof "$package" > /dev/null; then
  echo "ComParta is no longer running."
  status=1
fi

adb exec-out screencap -p > "$folder/android-first-screen.png" || true
adb logcat -d > "$folder/android-log.txt" || true
grep -E "ComParta|comparta|FATAL|AndroidRuntime" "$folder/android-log.txt" | tail -n 60 || true
exit $status
