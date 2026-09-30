using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Generates and caches one strict JSON schema per DTO type (additionalProperties:false,
/// all properties required, no constraint keywords) for provider structured outputs.
/// </summary>
public static partial class JsonSchemaRegistry
{
    private static readonly ConcurrentDictionary<Type, string> _schemas = new();

    private static readonly JsonSerializerOptions SchemaSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly AIJsonSchemaCreateOptions SchemaOptions = new()
    {
        TransformOptions = new AIJsonSchemaTransformOptions
        {
            DisallowAdditionalProperties = true,
            RequireAllProperties = true,
            MoveDefaultKeywordToDescription = true
        }
    };

    /// <summary>
    /// Returns the compact strict schema JSON for <paramref name="type"/>. The same string
    /// instance is returned on every call so requests stay byte-identical (prompt caching).
    /// </summary>
    public static string GetSchemaJson(Type type) => _schemas.GetOrAdd(type, Build);

    /// <summary>
    /// Returns the compact strict schema JSON for <typeparamref name="T"/>.
    /// </summary>
    public static string GetSchemaJson<T>() => GetSchemaJson(typeof(T));

    /// <summary>
    /// Returns a provider-safe schema name (<c>^[A-Za-z0-9_-]{1,64}$</c>) derived from the type name.
    /// </summary>
    public static string GetSchemaName(Type type)
    {
        var name = InvalidSchemaNameChars().Replace(type.Name, "_");
        if (name.Length == 0)
            name = "Schema";
        return name.Length > 64 ? name[..64] : name;
    }

    private static string Build(Type type)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            type,
            serializerOptions: SchemaSerializerOptions,
            inferenceOptions: SchemaOptions);

        var node = JsonNode.Parse(schema.GetRawText());
        if (node is JsonObject root && root.ContainsKey("$schema"))
            root.Remove("$schema");

        return node!.ToJsonString();
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex InvalidSchemaNameChars();
}
