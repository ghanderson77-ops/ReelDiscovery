using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;

public class EmailGeneratorPortTests
{
    public static TheoryData<Type> AllSchemaTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in EmailGenerator.SchemaTypes)
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSchemaTypes))]
    public void AllSchemaTypes_AreStrict(Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{type.Name} exceeds 16 union types");
    }

    [Fact]
    public void SchemaTypes_ListsEveryResponseDto()
    {
        var names = EmailGenerator.SchemaTypes.Select(t => t.Name).ToList();

        Assert.Equal(6, names.Count);
        Assert.Contains("ThreadApiResponse", names);
        Assert.Contains("VoicemailScriptResponse", names);
        Assert.Contains("WordDocResponse", names);
        Assert.Contains("ExcelDocResponse", names);
        Assert.Contains("PowerPointDocResponse", names);
        Assert.Contains("MeetingDetectionResponse", names);
    }
}
