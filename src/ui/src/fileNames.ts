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
