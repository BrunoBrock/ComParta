using ComParta.Notices;

// Usage: dotnet run --project tools/ComParta.Notices [-- [repository root] [--check] [--without-app]]
//   (nothing)      writes THIRD-PARTY-NOTICES.txt from what restore and npm ci installed
//   --check        fails when a package cannot ship or the committed file is out of date (build.ps1 Check)
//   --without-app  leaves out the app's own Windows packages, for a machine that cannot restore the app
//                  (Linux, no MAUI workload). Its file is incomplete: never commit it; take the file from
//                  a Windows run instead (CI uploads it when Check finds the committed one out of date).
var options = args.Where(arg => arg.StartsWith("--", StringComparison.Ordinal)).ToHashSet();
var root = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Directory.GetCurrentDirectory();
return NoticesTool.Run(Path.GetFullPath(root), options.Contains("--check"), options.Contains("--without-app"), Console.Out);
