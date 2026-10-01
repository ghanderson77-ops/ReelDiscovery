using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

public class CaseSchemaTests
{
    public static TheoryData<string, Type> CaseSchemaTypes()
    {
        var data = new TheoryData<string, Type>();
        foreach (var type in RealEntityDetector.SchemaTypes)
            data.Add(nameof(RealEntityDetector), type);
        foreach (var type in CoreDocumentGenerator.SchemaTypes)
            data.Add(nameof(CoreDocumentGenerator), type);
        foreach (var type in CaseGuideGenerator.SchemaTypes)
            data.Add(nameof(CaseGuideGenerator), type);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseSchemaTypes))]
    public void AllCaseSchemaTypes_AreStrict(string generator, Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{generator}.{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{generator}.{type.Name} exceeds 16 union types");
    }
}
