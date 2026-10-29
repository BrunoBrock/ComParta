#!/usr/bin/env bash
# Installs the APK on the emulator, starts ComParta and checks what epic 7 story 1 asks of a first
# Android build: the app starts its engine, the screen loads and the bridge answers it, and the
# app is still running afterwards; and that received files have their place in Downloads (story 3).
# Then (story 12) the phone receives a photo, a video and an MKV video from another device running
# on this computer, through its own screen: the photo shows and the video plays and seeks in the
# phone's web view, and the MKV, which it cannot play, opens the Files app at its folder.
# Leaves screenshots and the app's log next to the APK.
#   check-android.sh <APK folder> <folder with photo.jpg, clip.mp4, film.mkv> <ComParta.CheckSender.dll>
set -euo pipefail

folder="$1"
media="$2"
sender="$3"
apk=$(ls "$folder"/*-Signed.apk | head -n 1)
package=org.comparta.app

adb install -r "$apk"
# A test copy: the app opens its screen to this check over adb (ScreenCheck.cs).
adb shell mkdir -p "/sdcard/Android/data/$package/files"
adb shell touch "/sdcard/Android/data/$package/files/screen-check"
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

# Story 12: another device on this computer shares the three files; from the emulator this computer
# is 10.0.2.2.
wait_for "Screen check open" 5 || status=1
port=47123
invites="$media/invites.txt"
rm -f "$invites"
dotnet "$sender" "$port" 10.0.2.2 "$invites" "$media/photo.jpg" "$media/clip.mp4" "$media/film.mkv" > "$folder/check-sender.txt" 2>&1 &
sender_pid=$!
for _ in $(seq 120); do
  [ -f "$invites" ] && break
  sleep 1
done
if [ ! -f "$invites" ]; then
  echo "The other device never shared the files."
  cat "$folder/check-sender.txt" || true
  status=1
else
  app_pid=$(adb shell pidof "$package" | tr -d '\r')
  adb forward tcp:9333 "localabstract:webview_devtools_remote_$app_pid"
  node .github/scripts/drive-phone.mjs 9333 "$invites" || status=1
  # The web view read the photo whole and the video in parts, as a player does.
  wait_for "Media answered: 200" 10 || status=1
  wait_for "Media answered: 206 for a part" 10 || status=1
  # Open the folder shows the Files app in front.
  shown=0
  for _ in $(seq 15); do
    if adb shell dumpsys activity activities | grep -E "topResumedActivity|mResumedActivity" | grep -qi documentsui; then
      shown=1
      break
    fi
    sleep 1
  done
  if [ "$shown" -eq 1 ]; then
    echo "Seen: the Files app opened"
  else
    echo "The Files app did not open."
    adb shell dumpsys activity activities | grep -E "topResumedActivity|mResumedActivity" || true
    status=1
  fi
  sleep 2
  adb exec-out screencap -p > "$folder/android-files-app.png" || true
  if ! adb shell pidof "$package" > /dev/null; then
    echo "ComParta stopped while playing."
    status=1
  fi
fi
kill "$sender_pid" 2> /dev/null || true
adb logcat -d > "$folder/android-log.txt" || true
grep -E "ComParta|comparta|FATAL|AndroidRuntime" "$folder/android-log.txt" | tail -n 60 || true
exit $status
