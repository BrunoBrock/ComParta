// Drives ComParta on the Android emulator through its own screen, for check-android.sh (epic 7
// story 12). The app opens its web view's debugging socket only for a test copy (ScreenCheck.cs);
// check-android.sh forwards it to <port>. Each step sends a command the way the screen does (the
// same InvokeDotNet call to BridgeHost), so it goes through the real router, use cases, engine and
// storage on the phone, and the pictures and videos are loaded by the phone's own web view.
//   node drive-phone.mjs <port> <invites file>
// The invites are a photo, a video, and a video the phone cannot play, in that order. Prints one
// line per check; exits 1 when any fails.

const [port, invitesFile] = process.argv.slice(2)
const { readFileSync } = await import('node:fs')
const [photoInvite, videoInvite, otherInvite] = readFileSync(invitesFile, 'utf8').trim().split('\n')

async function connect() {
  const until = Date.now() + 120_000
  for (;;) {
    try {
      const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json()
      const page = targets.find((target) => target.type === 'page' && target.webSocketDebuggerUrl)
      if (page) return await open(page.webSocketDebuggerUrl)
    } catch (error) {
      if (Date.now() > until) throw error
    }
    if (Date.now() > until) throw new Error('The screen never loaded in the web view.')
    await new Promise((resolve) => setTimeout(resolve, 2000))
  }
}

/** One page over the debugging protocol: runs a function in it and gives back its answer. */
async function open(address) {
  const socket = new WebSocket(address)
  await new Promise((resolve, reject) => {
    socket.onopen = resolve
    socket.onerror = () => reject(new Error('The screen could not be reached.'))
  })
  // One step at a time: the answer with the id of the step being run is the one it waits for.
  let next = 0
  let waiting = null
  socket.onmessage = (message) => {
    const reply = JSON.parse(message.data)
    if (waiting !== null && reply.id === waiting.id) {
      const { resolve } = waiting
      waiting = null
      resolve(reply)
    }
  }
  return {
    async run(fn, ...args) {
      const id = ++next
      const answered = new Promise((resolve) => {
        waiting = { id, resolve }
      })
      socket.send(JSON.stringify({
        id,
        method: 'Runtime.evaluate',
        params: { expression: `(${fn})(...${JSON.stringify(args)})`, awaitPromise: true, returnByValue: true },
      }))
      const reply = await answered
      if (reply.error || reply.result.exceptionDetails)
        throw new Error(JSON.stringify(reply.error ?? reply.result.exceptionDetails))
      return reply.result.result.value
    },
    close: () => socket.close(),
  }
}

const screen = await connect()

/** One command through the app's bridge, answered as the screen gets it. */
const invoke = (command, args) =>
  screen.run(async (command, args) => {
    // The screen asked something first, so the bridge script is loaded.
    while (window.HybridWebView === undefined) await new Promise((resolve) => setTimeout(resolve, 200))
    const reply = await window.HybridWebView.InvokeDotNet('Invoke', [command, JSON.stringify(args ?? null)])
    return typeof reply === 'string' ? JSON.parse(reply) : reply
  }, command, args)

let failed = false
function check(ok, what, detail = '') {
  console.log(`${ok ? 'OK ' : 'BAD'} ${what}${detail ? `: ${detail}` : ''}`)
  if (!ok) failed = true
}

try {
  // Receive the three files as a person does, with the Receive button.
  const ids = []
  for (const invite of [photoInvite, videoInvite, otherInvite]) {
    const received = await invoke('transfers.receive', { invite })
    check(received.ok, 'Receive started', received.ok ? received.value.name : JSON.stringify(received))
    if (received.ok) ids.push(received.value.id)
  }
  const until = Date.now() + 240_000
  for (;;) {
    const list = await invoke('transfers.list')
    const done = list.ok ? list.value.filter((t) => ids.includes(t.id) && t.state === 'done').length : 0
    if (done === ids.length) break
    if (Date.now() > until) {
      check(false, 'Everything arrived', JSON.stringify(list.value?.map((t) => [t.name, t.state, t.receivedBytes])))
      throw new Error('Not everything arrived.')
    }
    await new Promise((resolve) => setTimeout(resolve, 2000))
  }
  check(true, 'Everything arrived in Downloads/ComParta')
  const [photoId, videoId, otherId] = ids

  // The photo shows, as the viewer shows it.
  const photo = await invoke('media.open', { kind: 'transfer', id: photoId })
  check(photo.ok && photo.value.kind === 'picture', 'The photo opens', JSON.stringify(photo))
  if (photo.ok) {
    const shown = await screen.run((url) => new Promise((resolve) => {
      const image = new Image()
      image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight })
      image.onerror = () => resolve(null)
      image.src = url
    }), photo.value.url)
    check(shown !== null && shown.width > 0, 'The photo shows', JSON.stringify(shown))
  }

  // The video plays, and a seek near the end lands there.
  const video = await invoke('media.open', { kind: 'transfer', id: videoId })
  check(video.ok && video.value.kind === 'video', 'The video opens', JSON.stringify(video))
  if (video.ok) {
    const played = await screen.run((url) => new Promise((resolve) => {
      const player = document.createElement('video')
      player.muted = true
      const give = (answer) => {
        player.pause()
        player.remove()
        resolve(answer)
      }
      const timer = setTimeout(() => give({ error: 'timed out', state: player.readyState }), 60_000)
      player.onerror = () => {
        clearTimeout(timer)
        give({ error: player.error?.message ?? String(player.error?.code) })
      }
      player.onloadedmetadata = async () => {
        const duration = player.duration
        let playing = false
        try {
          await player.play()
          playing = true
        } catch {
          // Reported below.
        }
        player.onseeked = () => {
          clearTimeout(timer)
          give({ duration, playing, at: player.currentTime })
        }
        player.currentTime = Math.max(0, duration - 1)
      }
      player.src = url
      document.body.appendChild(player)
    }), video.value.url)
    check(played.duration > 0 && !played.error, 'The video loads', JSON.stringify(played))
    check(played.playing === true, 'The video plays', JSON.stringify(played))
    check(played.at >= played.duration - 1.5, 'The video seeks', JSON.stringify(played))
  }

  // A file the phone cannot play offers its folder, and the folder opens in the Files app.
  const other = await invoke('media.open', { kind: 'transfer', id: otherId })
  check(!other.ok && other.error.code === 'media.notPlayable', 'The MKV video is refused', JSON.stringify(other))
  const reveal = await invoke('received.reveal', { id: otherId })
  check(reveal.ok, 'Open the folder answers', JSON.stringify(reveal))
} catch (error) {
  check(false, 'The phone was driven', String(error))
} finally {
  screen.close()
}
process.exit(failed ? 1 : 0)
