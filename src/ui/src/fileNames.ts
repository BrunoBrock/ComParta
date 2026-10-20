/** The extension in lower case, or '' when the name has none. */
export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.')
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toLowerCase() : ''
}

/**
 * Keeps the last few characters (the extension and a little before it) apart from the rest, so a
 * long name can be shortened in the middle. Short names come back whole as the second part.
 */
export function splitName(name: string): [string, string] {
  if (name.length <= 20) return ['', name]
  return [name.slice(0, -10), name.slice(-10)]
}

// Photos and videos, the files the app can draw a thumbnail for (same list as core's ThumbnailUseCases).
const pictures = new Set([
  'jpg',
  'jpeg',
  'png',
  'gif',
  'heic',
  'heif',
  'webp',
  'bmp',
  'tif',
  'tiff',
  'mp4',
  'mov',
  'avi',
  'mkv',
  'wmv',
  'm4v',
  'webm',
  '3gp',
  'mpg',
  'mpeg',
])

/** Whether the file is a photo or video, so a thumbnail is worth asking for. */
export const isPicture = (name: string) => pictures.has(extensionOf(name))

// The kinds ComParta shows or plays itself (same list as core's MediaUseCases); core also checks
// that the file's first bytes agree, so a renamed program is never played.
const playable: Record<string, 'picture' | 'song' | 'video'> = {
  jpg: 'picture',
  jpeg: 'picture',
  png: 'picture',
  gif: 'picture',
  webp: 'picture',
  mp3: 'song',
  aac: 'song',
  m4a: 'song',
  wav: 'song',
  flac: 'song',
  ogg: 'song',
  oga: 'song',
  mp4: 'video',
  m4v: 'video',
  webm: 'video',
}

/** Whether the file can be shown or played inside ComParta, and how; null when it cannot. */
export const playableKind = (name: string) => playable[extensionOf(name)] ?? null
