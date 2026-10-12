using ComParta.Contracts;

// Usage: dotnet run --project tools/ComParta.Contracts.TypeGen [repository root]
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var target = Path.Combine(root, TypeScriptWriter.GeneratedFile);
File.WriteAllText(target, TypeScriptWriter.Write());
Console.WriteLine($"Wrote {target}");
var texts = Path.Combine(root, NotificationTextsWriter.GeneratedFile);
File.WriteAllText(texts, NotificationTextsWriter.Write(root));
Console.WriteLine($"Wrote {texts}");
