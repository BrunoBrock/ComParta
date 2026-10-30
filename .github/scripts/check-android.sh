#!/usr/bin/env bash
# Installs the APK on the emulator, starts ComParta and checks what epic 7 story 1 asks of a first
# Android build: the app starts its engine, the screen loads and the bridge answers it, and the
# app is still running afterwards; that received files have their place in Downloads (story 3); and
# that an invite tapped or shared in another app opens "Get this file?" (story 6).
# Before that (story 12) the phone receives a photo, a video and an MKV video from another device
# running on this computer, through its own screen: the photo shows and the video plays and seeks in
# the phone's web view, and the MKV, which it cannot play, opens the Files app at its folder.
# First of all (story 7) a file keeps arriving with the screen off, shown in a notification, and the
# service stops a minute after, from a computer-side sender (tests/ComParta.PhoneCheck, built to
# publish/phone-check).
# Leaves screenshots and the app's log next to the APK.
#   check-android.sh <APK folder> <folder with photo.jpg, clip.mp4, film.mkv> <ComParta.CheckSender.dll>
set -euo pipefail

folder="$1"
media="$2"
sender="$3"
apk=$(ls "$folder"/*-Signed.apk | head -n 1)
package=org.comparta.app

# The computer the emulator runs on shares a 300 MB file at 1 MB/s for story 7, so it is still
# arriving while the screen is off. Started now: making the file takes a while.
computer="$folder/computer"
rm -rf "$computer"
dotnet publish/phone-check/ComParta.PhoneCheck.dll "$computer" 300 1000000 26881 > "$folder/computer-log.txt" 2>&1 &
computer_pid=$!
trap 'kill $computer_pid 2> /dev/null || true' EXIT

adb install -r "$apk"
# A test copy: the app opens its screen to this check over adb (ScreenCheck.cs).
adb shell mkdir -p "/sdcard/Android/data/$package/files"
adb shell touch "/sdcard/Android/data/$package/files/screen-check"
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

# Taps a button on the screen by its words, found in the screen's description the phone gives.
tap_on() {
  local words="$1"
  for _ in $(seq 20); do
    adb shell uiautomator dump /sdcard/ui.xml > /dev/null 2>&1 || true
    local bounds
    bounds=$(adb exec-out cat /sdcard/ui.xml | tr '>' '\n' | grep -E "(text|content-desc)=\"$words\"" \
      | grep -o 'bounds="\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]"' | head -n 1 || true)
    if [ -n "$bounds" ]; then
      read -r x1 y1 x2 y2 <<< "$(echo "$bounds" | grep -o '[0-9]*' | tr '\n' ' ')"
      adb shell input tap $(( (x1 + x2) / 2 )) $(( (y1 + y2) / 2 ))
      echo "Tapped: $words"
      return 0
    fi
    sleep 2
  done
  adb exec-out cat /sdcard/ui.xml > "$folder/android-screen.xml" || true
  echo "Never found: $words"
  return 1
}

# Bytes the computer has sent the phone so far.
sent() {
  cat "$computer/sent.txt" 2> /dev/null || echo 0
}

# Story 7, first, before story 12 leaves the Files app in front: a file from the computer keeps
# arriving with the screen off, the service that keeps it going runs with its notification, and it
# stops a minute after the bytes stop.
adb shell pm grant "$package" android.permission.POST_NOTIFICATIONS || true
for _ in $(seq 300); do
  [ -s "$computer/invite.txt" ] && break
  sleep 1
done
if [ -s "$computer/invite.txt" ]; then
  invite=$(cat "$computer/invite.txt")
  asked=$(seen "Asked: Get this file?")
  adb shell "am start -W -a android.intent.action.VIEW -d '$invite' $package"
  wait_for "Asked: Get this file?" 60 $((asked + 1)) || status=1
  tap_on "Get the file" || status=1
  wait_for "Receiving started" 30 || status=1
  if ! wait_for "Keeping going with the screen off" 120; then
    status=1
    echo "What the app said:"
    adb logcat -d -s ComParta:I | tail -n 30 || true
    echo "What the computer said:"
    tail -n 20 "$folder/computer-log.txt" || true
  fi
  adb shell cmd statusbar expand-notifications || true
  sleep 2
  adb exec-out screencap -p > "$folder/android-moving.png" || true
  adb shell cmd statusbar collapse || true

  adb shell input keyevent 223
  sleep 10
  before=$(sent)
  sleep 40
  after=$(sent)
  echo "With the screen off the phone got $((after - before)) bytes in 40 seconds ($before to $after)."
  if [ "$after" -le $((before + 5000000)) ]; then
    echo "The file did not keep arriving with the screen off."
    status=1
  fi
  if ! adb shell dumpsys activity services "$package" | grep -q "MovingService"; then
    echo "The service that keeps it going is not running with the screen off."
    status=1
  fi
  if ! adb shell dumpsys notification --noredact | grep -q "pkg=$package"; then
    echo "No notification is shown while the file arrives."
    status=1
  fi
  adb shell input keyevent 224
  adb shell wm dismiss-keyguard || true

  # Nothing moves once the computer stops sharing; a minute later the service is gone.
  kill "$computer_pid" 2> /dev/null || true
  wait_for "Stopped keeping going with the screen off" 150 || status=1
else
  echo "The computer never shared its file:"
  cat "$folder/computer-log.txt" || true
  status=1
fi

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

# Story 6: an invite tapped in an email, and a chat message shared to ComParta from WhatsApp, open
# "Get this file?" in the running app; a message without an invite is answered plainly. Nothing
# starts a second copy of the engine.
asked=$(seen "Asked: Get this file?")
link='magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a&dn=Natal.mp4&xl=1048576'
adb shell "am start -W -a android.intent.action.VIEW -d '$link' $package"
wait_for "Asked: Get this file?" 60 $((asked + 1)) || status=1
adb exec-out screencap -p > "$folder/android-link-tapped.png" || true
# The confirmation stays open: a back press that lands before it shows would leave the app, which
# ends it when nothing is shared (AD-14). A new link replaces what the confirmation asks.
adb shell "am start -W -a android.intent.action.SEND -t text/plain --es android.intent.extra.TEXT 'Oi! Here is the video: $link see you' $package"
wait_for "Asked: Get this file?" 60 $((asked + 2)) || status=1
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
