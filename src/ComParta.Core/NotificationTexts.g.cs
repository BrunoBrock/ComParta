// Generated from the UI catalogs by tools/ComParta.Contracts.TypeGen. Do not edit.
namespace ComParta.Core;

public static partial class NotificationTexts
{
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new()
    {
        ["en"] = new()
        {
            ["notify.arrived.title"] = "A file arrived",
            ["notify.arrived.body"] = "“{name}” is in your Received files.",
            ["notify.finished.title"] = "Someone has your file",
            ["notify.finished.body"] = "Someone finished getting “{name}”.",
            ["notify.waiting.title"] = "A file is still waiting",
            ["notify.waiting.body"] = "“{name}” has waited over an hour. Ask the other person to open ComParta.",
        },
        ["pt-BR"] = new()
        {
            ["notify.arrived.title"] = "Um arquivo chegou",
            ["notify.arrived.body"] = "“{name}” está nos seus arquivos recebidos.",
            ["notify.finished.title"] = "Alguém recebeu seu arquivo",
            ["notify.finished.body"] = "Alguém terminou de receber “{name}”.",
            ["notify.waiting.title"] = "Um arquivo ainda está esperando",
            ["notify.waiting.body"] = "“{name}” está esperando há mais de uma hora. Peça para a outra pessoa abrir o ComParta.",
        },
    };
}
