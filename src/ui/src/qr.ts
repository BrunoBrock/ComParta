import qrcode from 'qrcode-generator'

/** A QR code as one SVG path: `size` modules across, the quiet border included. */
export type QrDrawing = { size: number; path: string }

/** The empty border a camera needs around the code, in modules (the QR standard's quiet zone). */
const border = 4

/**
 * The QR code for a text, row by row, true where a square is dark; null when the text is too long
 * for one. Medium error correction, so a code read off a screen with a reflection on it still works.
 */
export function qrModules(text: string): boolean[][] | null {
  const code = qrcode(0, 'M')
  // The library writes one byte per character, so the text goes in as its UTF-8 bytes.
  code.addData(String.fromCharCode(...new TextEncoder().encode(text)), 'Byte')
  try {
    code.make()
  } catch {
    return null
  }
  const count = code.getModuleCount()
  return Array.from({ length: count }, (_, row) => Array.from({ length: count }, (_, col) => code.isDark(row, col)))
}

/** The QR code for an invite link (epic 7 story 9), or null when the link is too long for one. */
export function drawQr(text: string): QrDrawing | null {
  const modules = qrModules(text)
  if (!modules) return null
  let path = ''
  modules.forEach((row, y) =>
    row.forEach((dark, x) => {
      if (dark) path += `M${x + border} ${y + border}h1v1h-1z`
    }),
  )
  return { size: modules.length + border * 2, path }
}

/**
 * Whether Receive offers "Scan a QR code": on a phone with a camera the web view can ask for
 * (epic 7 story 9). Windows keeps paste and the download file.
 */
export function canScan(): boolean {
  return /Android/i.test(navigator.userAgent) && typeof navigator.mediaDevices?.getUserMedia === 'function'
}
