using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ComParta.Contracts;

/// <summary>Writes the TypeScript side of the contract so it is never typed by hand (AD-3).</summary>
public static class TypeScriptWriter
{
    public const string GeneratedFile = "src/ui/src/bridge/contract.generated.ts";

    public static string Write()
    {
        var types = new List<Type>();
        foreach (var spec in Contract.CommandSpecs)
        {
            Collect(spec.Args, types);
            Collect(spec.Result, types);
        }
        foreach (var spec in Contract.EventSpecs)
            Collect(spec.Payload, types);

        var ts = new StringBuilder();
        ts.Append("// Generated from ComParta.Contracts by tools/ComParta.Contracts.TypeGen. Do not edit.\n");

        foreach (var type in types.Where(t => t.IsEnum))
        {
            var names = Enum.GetNames(type).Select(n => $"\"{Camel(n)}\"");
            ts.Append($"\nexport type {type.Name} = {string.Join(" | ", names)};\n");
        }

        var nullability = new NullabilityInfoContext();
        foreach (var type in types.Where(t => !t.IsEnum))
        {
            ts.Append($"\nexport interface {type.Name} {{\n");
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null
                    || nullability.Create(property).ReadState == NullabilityState.Nullable;
                ts.Append($"  {Camel(property.Name)}: {Name(property.PropertyType)}{(nullable ? " | null" : "")};\n");
            }
            ts.Append("}\n");
        }

        ts.Append("\nexport const errorCodes = [\n");
        foreach (var code in ErrorCodes.All)
            ts.Append($"  \"{code}\",\n");
        ts.Append("] as const;\n\nexport type ErrorCode = (typeof errorCodes)[number];\n");

        ts.Append("\nexport interface Commands {\n");
        foreach (var spec in Contract.CommandSpecs)
        {
            var args = spec.Args is null ? "undefined" : Name(spec.Args);
            var result = spec.Result is null ? "null" : Name(spec.Result) + (spec.ResultMayBeNull ? " | null" : "");
            ts.Append($"  \"{spec.Name}\": {{ args: {args}; result: {result} }};\n");
        }
        ts.Append("}\n");

        ts.Append("\nexport interface Events {\n");
        foreach (var spec in Contract.EventSpecs)
            ts.Append($"  \"{spec.Name}\": {Name(spec.Payload)};\n");
        ts.Append("}\n");

        ts.Append("""

            export type CommandResult<T> = { ok: true; value: T } | { ok: false; error: { code: ErrorCode } };

            export type EventEnvelope = { [E in keyof Events]: { event: E; payload: Events[E] } }[keyof Events];

            """.ReplaceLineEndings("\n"));
        return ts.ToString();
    }

    private static void Collect(Type? type, List<Type> types)
    {
        if (type is null)
            return;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            Collect(type.GetElementType(), types);
            return;
        }
        if (type.Assembly != typeof(Contract).Assembly || types.Contains(type))
            return;
        types.Add(type);
        if (!type.IsEnum)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Collect(property.PropertyType, types);
        }
    }

    private static string Name(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
            return Name(type.GetElementType()!) + "[]";
        if (type == typeof(string))
            return "string";
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double))
            return "number";
        if (type.Assembly == typeof(Contract).Assembly)
            return type.Name;
        throw new NotSupportedException($"No TypeScript mapping for {type}.");
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}
