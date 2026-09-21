using AwesomeAssertions;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Discussions;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Projects;
using CvPlatform.Application.Validation;

namespace CvPlatform.Tests;

public class ValidationTests
{
    [Fact]
    public void Position_validator_rejects_empty_title_and_bad_maxprojects()
    {
        var validator = new PositionInputValidator();
        validator.Validate(new PositionInput("", "d", null, null, true, 3)).IsValid.Should().BeFalse();
        validator.Validate(new PositionInput("T", "d", null, null, true, 101)).IsValid.Should().BeFalse();
        validator.Validate(new PositionInput("T", "d", null, null, true, 3)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Project_validator_rejects_bad_period_and_long_tags()
    {
        var validator = new ProjectInputValidator();
        validator.Validate(new ProjectInput("", null, null, "", [], null)).IsValid.Should().BeFalse();
        validator.Validate(new ProjectInput("N", new DateOnly(2024, 2, 1), new DateOnly(2024, 1, 1), "", [], null))
            .IsValid.Should().BeFalse();
        validator.Validate(new ProjectInput("N", null, null, "", [new string('x', 51)], null))
            .IsValid.Should().BeFalse();
        validator.Validate(new ProjectInput("N", null, null, "", ["dotnet"], null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AttributeDefinition_validator_rejects_bad_options()
    {
        var validator = new AttributeDefinitionInputValidator();
        validator.Validate(new AttributeDefinitionInput(Guid.NewGuid(), "", null, Core.Enums.AttributeDataType.String, null))
            .IsValid.Should().BeFalse();
        validator.Validate(new AttributeDefinitionInput(Guid.NewGuid(), "N", null, Core.Enums.AttributeDataType.Numeric, """{"min":5,"max":1}"""))
            .IsValid.Should().BeFalse();
        validator.Validate(new AttributeDefinitionInput(Guid.NewGuid(), "N", null, Core.Enums.AttributeDataType.String, null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Discussion_validator_enforces_length()
    {
        var validator = new DiscussionPostInputValidator();
        validator.Validate(new DiscussionPostInput("")).IsValid.Should().BeFalse();
        validator.Validate(new DiscussionPostInput(new string('x', 2001))).IsValid.Should().BeFalse();
        validator.Validate(new DiscussionPostInput("Hello")).IsValid.Should().BeTrue();
    }
}
