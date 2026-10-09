// Every word the person sees lives here (AD-11). No technical file-sharing terms (AD-5).
export const en = {
  'app.title': 'ComParta',
  'app.tagline': 'Send files straight to people you know. No account, no cloud.',
  'notice.dismiss': 'Close this message',

  'shares.heading': 'Files I am sharing',
  'shares.add': 'Share a file',
  'shares.empty': 'You are not sharing anything yet. Choose “Share a file” to pick one from this computer.',
  'shares.status.preparing': 'Getting ready…',
  'shares.status.ready': 'Ready to send',
  'shares.status.unavailable': 'File not found. It was moved, renamed, changed or deleted.',
  'shares.copyInvite': 'Copy invite',
  'shares.copyInviteFor': 'Copy invite for {name}',
  'shares.inviteCopied':
    'Invite for “{name}” copied. Paste it into an email or chat message to the person you want to send it to, and keep ComParta open until they have the file.',
  'shares.remove': 'Stop sharing',
  'shares.removeFor': 'Stop sharing {name}',

  'remove.title': 'Stop sharing “{name}”?',
  'remove.body':
    'The file stays on your computer, exactly where it is. People who have its invite will no longer be able to get it.',
  'remove.confirm': 'Stop sharing',
  'remove.cancel': 'Keep sharing',

  'transfers.heading': 'Files I am receiving',
  'transfers.inviteLabel': 'Paste the invite you were sent',
  'transfers.receive': 'Get the file',
  'transfers.empty': 'Nothing here yet. When someone sends you an invite, paste it above.',
  'transfers.unnamed': 'File on its way',
  'transfers.state.connecting': 'Looking for the other person’s computer… They need to have ComParta open.',
  'transfers.state.receiving': 'Receiving: {percent}% ({received} of {total})',
  'transfers.state.done': 'Received. It is in your ComParta folder.',
  'transfers.state.failed': 'This file could not be received. Remove it and ask for a new invite.',
  'transfers.cancel': 'Cancel',
  'transfers.cancelFor': 'Cancel receiving {name}',
  'transfers.clear': 'Clear from list',
  'transfers.clearFor': 'Clear {name} from list',
  'transfers.openFolder': 'Open my received files',

  'error.share.duplicate': 'You are already sharing this file.',
  'error.share.notFound': 'That file is no longer in your list.',
  'error.share.notReady': 'This file is still getting ready. Try again in a moment.',
  'error.file.unreadable':
    'ComParta could not read this file. Check that it is not empty or open in another program, then try again.',
  'error.invite.invalid':
    'That does not look like a ComParta invite. Copy the whole invite from the message you received and paste it again.',
  'error.transfer.duplicate': 'You are already receiving this file.',
  'error.transfer.notFound': 'That file is no longer in your list.',
  'error.unexpected': 'Something went wrong. Close ComParta, open it again and try once more.',

  'size.bytes': '{value} bytes',
  'size.kb': '{value} KB',
  'size.mb': '{value} MB',
  'size.gb': '{value} GB',
} as const

export type MessageKey = keyof typeof en
