using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai;

public class JsonSchemaRegistryTests
{
    public class NestedDto
    {
        public string Label { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class SampleDto
    {
        [Description("The display name")]
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int Count { get; set; }
        public bool Flag { get; set; }
        public List<string> Tags { get; set; } = new();
        public NestedDto Child { get; set; } = new();
        public List<NestedDto> Items { get; set; } = new();
    }

    public class Outer
    {
        public class Inner_Dto
        {
            public string Text { get; set; } = string.Empty;
        }
    }

    [Fact]
    public void Schema_IsStrict()
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(typeof(SampleDto));

        var violations = StrictSchemaValidator.Validate(schema);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations) + Environment.NewLine + schema);

        var root = JsonNode.Parse(schema)!.AsObject();
        Assert.Equal("object", root["type"]!.GetValue<string>());
        Assert.False(root.ContainsKey("$schema"));

        var required = root["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(7, required.Count);
        Assert.Equal(
            new[] { "name", "note", "count", "flag", "tags", "child", "items" }.OrderBy(x => x),
            required.OrderBy(x => x));

        Assert.Equal("The display name", root["properties"]!["name"]!["description"]!.GetValue<string>());
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16);
    }

    [Fact]
    public void Schema_IsCachedByteIdentical()
    {
        var first = JsonSchemaRegistry.GetSchemaJson(typeof(SampleDto));
        var second = JsonSchemaRegistry.GetSchemaJson<SampleDto>();

        Assert.True(ReferenceEquals(first, second));
        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public void SchemaName_IsSanitized()
    {
        var name = JsonSchemaRegistry.GetSchemaName(typeof(Outer.Inner_Dto));
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{1,64}$"), name);

        var genericName = JsonSchemaRegistry.GetSchemaName(typeof(List<SampleDto>));
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{1,64}$"), genericName);
    }

    [Fact]
    public void Validator_FlagsViolations()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "a": { "type": "integer", "minimum": 1 },
                "b": { "type": "array", "items": {} }
              },
              "required": ["a", "b"]
            }
            """;

        var violations = StrictSchemaValidator.Validate(schema);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Contains("minimum"));
        Assert.Contains(violations, v => v.Contains("additionalProperties"));
        Assert.Contains(violations, v => v.Contains("empty schema"));
    }
}
