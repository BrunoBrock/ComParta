# Running ComParta locally

How to get ComParta running on your own Windows PC from this repository. For what the app does
and how to use it, see the [README](README.md).

## What you need

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The MAUI Windows workload: `dotnet workload install maui-windows`
- [Node.js](https://nodejs.org) 22 or newer
- Microsoft Edge WebView2 Runtime, which Windows 11 and current Windows 10 already have

Check what you have:

```powershell
dotnet --version
dotnet workload list
node --version
```

## Run the app

All commands are run in PowerShell from the repository root.

1. Build the screen and the app. The first run also installs the UI packages.

   ```powershell
   .\build.ps1
   ```

   This runs every test too. Add `-SkipTests` to build only.

2. Start the app.

   ```powershell
   dotnet run --project src\ComParta.App -c Release --no-build
   ```

The first time, Windows asks whether ComParta may use the network. Choose **Allow**, or other
computers cannot reach yours.

The screen is not built by `dotnet build`. It is built by `npm run build` in `src\ui`, which
writes into `src\ComParta.App\Resources\Raw\wwwroot`; that folder is not in git. If the window
opens empty, the screen has not been built: run `.\build.ps1` again.

## Run only the screen, in a browser

The screen runs without the app, against a fake one. This is the quickest way to work on the UI
and needs only Node.js.

```powershell
cd src\ui
npm ci
npm run dev
```

Open the address Vite prints, normally <http://localhost:5173>. Nothing is really shared or sent
in this mode.

## Try a transfer with two copies on one PC

Each copy needs its own data folder, set through `COMPARTA_HOME` before it starts. Open two
PowerShell windows at the repository root.

Window A:

```powershell
$env:COMPARTA_HOME = 'C:\Temp\deviceA'
dotnet run --project src\ComParta.App -c Release --no-build
```

Window B:

```powershell
$env:COMPARTA_HOME = 'C:\Temp\deviceB'
dotnet run --project src\ComParta.App -c Release --no-build
```

Share a file in A, copy the invite, and paste it into B. The file arrives in
`C:\Temp\deviceB\Received`.

Only one copy runs per data folder. Starting the app again, or clicking a `comparta://` invite
link in mail or chat, brings the running copy to the front with "Get this file?" open. Each start
registers the `comparta:` link for your Windows user, pointing at the copy that started last.

## Where the app keeps its data

| What | Default | With `COMPARTA_HOME` set |
| --- | --- | --- |
| List of shares and transfers | `%LOCALAPPDATA%\ComParta\comparta.db` | `<home>\comparta.db` |
| Engine data | `%LOCALAPPDATA%\ComParta\engine` | `<home>\engine` |
| Logs | `%LOCALAPPDATA%\ComParta\logs` | `<home>\logs` |
| Received files | `Downloads\ComParta` | `<home>\Received` |

To start from scratch, close the app and delete `%LOCALAPPDATA%\ComParta` (or the
`COMPARTA_HOME` folder). Files you shared are never touched; they stay where they were.

## Run the tests

| Tests | Command |
| --- | --- |
| Everything | `.\build.ps1` |
| C# only | `dotnet test` |
| Screen only | `cd src\ui` then `npm test` |

## Make a folder to copy to another PC

```powershell
.\build.ps1 -Publish
```

This creates `publish\ComParta`. Copy the whole folder and run `ComParta.exe` in it; nothing else
needs installing there. The app is not code-signed yet, so Windows SmartScreen warns on first run.
