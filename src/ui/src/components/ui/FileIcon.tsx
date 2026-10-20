import { createElement } from 'react'
import { extensionOf } from '../../fileNames.ts'
import {
  AppWindow,
  File,
  FileArchive,
  FileAudio,
  FileImage,
  FileSpreadsheet,
  FileText,
  FileVideo,
  Folder,
  Presentation,
  type LucideIcon,
} from 'lucide-react'

const kinds: [LucideIcon, string[]][] = [
  [FileVideo, ['mp4', 'mov', 'avi', 'mkv', 'wmv', 'm4v', 'webm', '3gp', 'mpg', 'mpeg']],
  [FileImage, ['jpg', 'jpeg', 'png', 'gif', 'heic', 'heif', 'webp', 'bmp', 'tif', 'tiff', 'raw']],
  [FileAudio, ['mp3', 'wav', 'm4a', 'aac', 'flac', 'ogg', 'opus', 'wma']],
  [FileSpreadsheet, ['xls', 'xlsx', 'ods', 'csv']],
  [Presentation, ['ppt', 'pptx', 'odp', 'key']],
  [FileArchive, ['zip', 'rar', '7z', 'tar', 'gz', 'iso']],
  [AppWindow, ['exe', 'msi', 'bat', 'cmd', 'ps1', 'apk', 'lnk', 'scr', 'vbs', 'js']],
  [FileText, ['pdf', 'doc', 'docx', 'odt', 'txt', 'rtf', 'md']],
]

const byExtension = new Map(kinds.flatMap(([icon, extensions]) => extensions.map((e) => [e, icon] as const)))

interface FileIconProps {
  name: string
  size?: number
  /** A folder, or several files shared together. */
  folder?: boolean
}

/** A picture for the kind of file, chosen from its extension (UX design, Items). */
export default function FileIcon({ name, size = 40, folder = false }: FileIconProps) {
  const icon = folder ? Folder : (byExtension.get(extensionOf(name)) ?? File)
  return createElement(icon, { className: 'file-icon', size, strokeWidth: 1.5, 'aria-hidden': true })
}
