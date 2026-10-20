using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComParta.Helper;

/// <summary>The JSON the control port writes, prepared at build time.</summary>
[JsonSerializable(typeof(AddedFile))]
[JsonSerializable(typeof(HelperAbout))]
[JsonSerializable(typeof(IReadOnlyList<HeldFile>))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal sealed partial class HelperJson : JsonSerializerContext;
