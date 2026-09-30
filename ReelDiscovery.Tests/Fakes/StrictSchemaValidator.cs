using System.Text.Json.Nodes;

namespace ReelDiscovery.Tests.Fakes;

/// <summary>
/// Checks a JSON schema against the strict structured-output rules shared by OpenAI strict mode
/// and Anthropic structured outputs.
/// </summary>
public static class StrictSchemaValidator
{
    private static readonly string[] ForbiddenKeywords =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "maxItems", "allOf", "not", "if",
        "patternProperties", "$schema"
    ];

    public static List<string> Validate(string schemaJson)
    {
        var violations = new List<string>();
        var root = JsonNode.Parse(schemaJson);

        if (root is not JsonObject rootObj)
        {
            violations.Add("$: root is not a JSON object");
            return violations;
        }

        if (!TypeIncludes(rootObj, "object") || rootObj["type"] is JsonArray)
            violations.Add("$: root type must be \"object\"");

        ValidateSchema(rootObj, "$", violations);
        return violations;
    }

    /// <summary>
    /// Number of schema nodes whose "type" is a union (JSON array). Anthropic allows at most 16.
    /// </summary>
    public static int CountUnionTypes(string schemaJson)
    {
        var count = 0;
        Walk(JsonNode.Parse(schemaJson));
        return count;

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["type"] is JsonArray)
                        count++;
                    foreach (var kv in obj)
                        Walk(kv.Value);
                    break;
                case JsonArray arr:
                    foreach (var item in arr)
                        Walk(item);
                    break;
            }
        }
    }

    private static void ValidateSchema(JsonNode? node, string path, List<string> violations)
    {
        if (node is not JsonObject schema)
            return;

        foreach (var keyword in ForbiddenKeywords)
        {
            if (schema.ContainsKey(keyword))
                violations.Add($"{path}: forbidden keyword '{keyword}'");
        }

        if (schema["minItems"] is JsonValue minItems &&
            minItems.TryGetValue<int>(out var min) && min > 1)
        {
            violations.Add($"{path}: minItems {min} > 1");
        }

        if (TypeIncludes(schema, "object"))
        {
            if (schema["additionalProperties"] is not JsonValue ap ||
                !ap.TryGetValue<bool>(out var apValue) || apValue)
            {
                violations.Add($"{path}: object without additionalProperties:false");
            }

            var propertyKeys = (schema["properties"] as JsonObject)?.Select(kv => kv.Key).ToHashSet()
                ?? new HashSet<string>();
            var required = (schema["required"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? string.Empty).ToHashSet()
                ?? new HashSet<string>();

            if (!propertyKeys.SetEquals(required))
                violations.Add($"{path}: required [{string.Join(",", required)}] != properties [{string.Join(",", propertyKeys)}]");
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var kv in properties)
                ValidateChild(kv.Value, $"{path}.properties.{kv.Key}", violations);
        }

        if (schema.ContainsKey("items"))
            ValidateChild(schema["items"], $"{path}.items", violations);

        if (schema["additionalProperties"] is JsonObject apSchema)
            ValidateChild(apSchema, $"{path}.additionalProperties", violations);

        foreach (var combinator in new[] { "anyOf", "oneOf" })
        {
            if (schema[combinator] is JsonArray options)
            {
                for (var i = 0; i < options.Count; i++)
                    ValidateChild(options[i], $"{path}.{combinator}[{i}]", violations);
            }
        }

        foreach (var defsKey in new[] { "$defs", "definitions" })
        {
            if (schema[defsKey] is JsonObject defs)
            {
                foreach (var kv in defs)
                    ValidateChild(kv.Value, $"{path}.{defsKey}.{kv.Key}", violations);
            }
        }
    }

    private static void ValidateChild(JsonNode? node, string path, List<string> violations)
    {
        if (node is JsonObject obj && obj.Count == 0)
        {
            violations.Add($"{path}: empty schema {{}}");
            return;
        }

        ValidateSchema(node, path, violations);
    }

    private static bool TypeIncludes(JsonObject schema, string type) => schema["type"] switch
    {
        JsonValue v => v.TryGetValue<string>(out var s) && s == type,
        JsonArray a => a.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == type),
        _ => false
    };
}
