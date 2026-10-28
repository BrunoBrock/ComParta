# Contributing to ComParta

## License

ComParta is released under the Zero-Clause BSD license (see `LICENSE`). That license asks for
nothing to be kept in each file, so new files carry no license header.

## New packages

Every NuGet package the app or the helper ships, and every npm package bundled into the screen, must
have a license ComParta can ship: a permissive one such as MIT, BSD, Apache-2.0 or ISC. The allow-list
is in `tools/ComParta.Notices/LicensePolicy.cs`. A package with no license, an unknown one, or one
that would change ComParta's own license (GPL, AGPL, LGPL, SSPL and the like) fails the build, naming
the package.

`THIRD-PARTY-NOTICES.txt` lists every shipped package with its license and copyright, and the app
shows it in Settings, About. It is generated, never edited by hand. After adding, removing or updating
a package, regenerate it on Windows once the app has been built:

```powershell
.\build.ps1 Build
dotnet run --project tools\ComParta.Notices
```

and commit the new file with the change. When Check finds the committed file out of date it leaves
the right one in `publish\THIRD-PARTY-NOTICES.txt`, and CI uploads it as the `third-party-notices`
download.

Some packages give their license as a file instead of a license name. Read that file, and if it lets
ComParta ship the package, add the package to `Exceptions` in `LicensePolicy.cs` with the license to
show and one sentence on why it is enough.

## Before a pull request

Run `.\build.ps1 Check`. It builds everything, runs every test and checks the third-party notices, as
CI does.
