using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;

public class LegacyGeneratorPortTests
{
    public static TheoryData<string, Type> AllSchemaTypes()
    {
        var data = new TheoryData<string, Type>();
        foreach (var type in StorylineGenerator.SchemaTypes)
            data.Add(nameof(StorylineGenerator), type);
        foreach (var type in CharacterGenerator.SchemaTypes)
            data.Add(nameof(CharacterGenerator), type);
        foreach (var type in ThemeGenerator.SchemaTypes)
            data.Add(nameof(ThemeGenerator), type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSchemaTypes))]
    public void AllSchemaTypes_AreStrict(string generator, Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{generator}.{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{generator}.{type.Name} exceeds 16 union types");
    }

    [Fact]
    public void SchemaTypes_CoverAllDtos()
    {
        Assert.Contains(StorylineGenerator.SchemaTypes, t => t.Name == "StorylineApiResponse");
        Assert.Contains(CharacterGenerator.SchemaTypes, t => t.Name == "CharacterApiResponse");
        Assert.Contains(ThemeGenerator.SchemaTypes, t => t.Name == "ThemeApiResponse");
    }
}
