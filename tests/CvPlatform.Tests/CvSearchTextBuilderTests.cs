using AwesomeAssertions;
using CvPlatform.Application.Cvs;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Tests;

public class CvSearchTextBuilderTests
{
    [Fact]
    public void Build_excludes_deleted_attribute_and_value()
    {
        var excludedId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        var category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Me" };
        var excluded = new AttributeDefinition
        {
            Id = excludedId,
            CategoryId = category.Id,
            Name = "IELTS Band",
            DataType = AttributeDataType.Dropdown,
        };
        var kept = new AttributeDefinition
        {
            Id = keptId,
            CategoryId = category.Id,
            Name = "City",
            DataType = AttributeDataType.String,
        };
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            User = new ApplicationUser { UserName = "candidate@example.test" },
            AttributeValues =
            [
                new ProfileAttributeValue
                {
                    AttributeDefinitionId = excludedId,
                    AttributeDefinition = excluded,
                    DropdownOption = "9",
                },
                new ProfileAttributeValue
                {
                    AttributeDefinitionId = keptId,
                    AttributeDefinition = kept,
                    StringValue = "Warsaw",
                },
            ],
        };
        var cv = new Cv
        {
            Profile = profile,
            Position = new Position
            {
                Attributes =
                [
                    new PositionAttribute { AttributeDefinitionId = excludedId, AttributeDefinition = excluded, SortOrder = 0 },
                    new PositionAttribute { AttributeDefinitionId = keptId, AttributeDefinition = kept, SortOrder = 1 },
                ],
            },
        };

        var result = CvSearchTextBuilder.Build(cv, excludedId);

        result.Should().Contain("candidate@example.test");
        result.Should().Contain("City Warsaw");
        result.Should().NotContain("IELTS");
        result.Should().NotContain("9");
    }
}
