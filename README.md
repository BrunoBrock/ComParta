# ComParta

Send files straight to people you know. No account, no cloud.

ComParta is a Windows desktop app for people who are not technical. You pick a file, copy an
invite, and send the invite by email or chat. The other person pastes it into their ComParta and
the file travels directly from your computer to theirs.

The vision, spec and architecture are in [`Planning`](Planning/ReadMe.md). To run it from this
repository, see [Running ComParta locally](RUNNING.md).

## Using it

**To send a file**

1. Open ComParta and choose **Share a file**. The file stays where it is; ComParta never copies,
   moves or changes it.
2. When it says **Ready to send**, choose **Copy invite** and paste the invite into an email or
   chat message.
3. Keep ComParta running until the other person has the file. Closing the window leaves it next to the clock while something is shared; quit it from that icon.

**To receive a file**

1. Open ComParta, paste the invite into **Paste the invite you were sent** and choose **Get the file**.
2. When it says **Received**, choose **Open my received files**. Files arrive in
   `Downloads\ComParta`.

**The first time**, Windows asks whether ComParta may use the network. Choose **Allow**. If it is
refused, other computers cannot reach yours and transfers will stay on "Looking for the other
person's computer".

### What to expect

- Two computers on the same home network connect directly.
- Between two homes, at least one of the two computers must be reachable from the internet. Most
  home routers open the needed port automatically; some do not. This is the main known limitation.
- A share is unlisted, not secret: only someone with the invite can ask for the file, but the
  file's fingerprint is announced on a public lookup network so the two computers can find each
  other. See AD-8 in the architecture.

## Building

You need Windows 10 or 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) with the MAUI
Windows workload (`dotnet workload install maui-windows`), and [Node.js](https://nodejs.org) 22 or newer.

```powershell
.\build.ps1           # shows a menu of what you can do
.\build.ps1 Publish   # build, test, then create publish\ComParta, a folder to run here or copy to another PC
.\build.ps1 Publish -Run   # same, then start the published ComParta.exe
.\build.ps1 Build     # build the screen and the app, without tests
.\build.ps1 Test      # run every test
.\build.ps1 Check     # build and run every test; what CI does, and what runs when there is no menu to show
```

Add `-SkipTests` to `Publish` or `Check` to leave the tests out.

Run `publish\ComParta\ComParta.exe`. Close it before publishing again; Windows will not let the
folder be replaced while the app is running from it. The folder needs nothing installed on the other
PC except the Microsoft Edge WebView2 Runtime, which Windows 11 and current Windows 10 already have.

The app is not code-signed yet. Windows SmartScreen will warn on first run, and a PC with Smart
App Control switched on will refuse to run it.

### Working on one part at a time

| Part | Command |
| --- | --- |
| Screen in a browser, against a fake app | `cd src\ui` then `npm run dev` |
| Screen tests | `cd src\ui` then `npm test` |
| C# tests | `dotnet test` |
| Regenerate the TypeScript contract after changing `ComParta.Contracts` | `dotnet run --project tools/ComParta.Contracts.TypeGen` |

To run two copies on one PC for testing, give each its own data folder before starting it:
`$env:COMPARTA_HOME = 'C:\Temp\deviceA'`.

## Layout

```text
src/ComParta.Core                 rules and ports; depends on nothing else
src/ComParta.Contracts            the bridge between screen and app: commands, events, error codes
src/ComParta.Engine.MonoTorrent   the transfer engine, behind one port
src/ComParta.Storage.Sqlite       the list of shares and transfers
src/ComParta.App                  the Windows window (.NET MAUI) that hosts the screen
src/ui                            the screen (React)
tests/                            C# tests
```

## License

To be decided before the repository is made public.
