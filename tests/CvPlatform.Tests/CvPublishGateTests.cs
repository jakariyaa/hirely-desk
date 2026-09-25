using AwesomeAssertions;
using CvPlatform.Application.Cvs;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Tests;

public class CvPublishGateTests
{
    private static AttributeDefinition Definition(string name, AttributeDataType type) => new()
    {
        Id = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Name = name,
        DataType = type,
    };

    private static PositionAttribute Required(AttributeDefinition definition, int sortOrder = 0) => new()
    {
        AttributeDefinitionId = definition.Id,
        AttributeDefinition = definition,
        IsRequired = true,
        SortOrder = sortOrder,
    };

    [Fact]
    public void Empty_requirements_are_always_satisfied()
    {
        CvPublishGate.IsSatisfied([], new Dictionary<Guid, ProfileAttributeValue>()).Should().BeTrue();
    }

    [Fact]
    public void All_required_filled_is_satisfied()
    {
        var city = Definition("City", AttributeDataType.String);
        var experience = Definition("Experience", AttributeDataType.Numeric);
        var attributes = new List<PositionAttribute> { Required(city, 0), Required(experience, 1) };
        var values = new Dictionary<Guid, ProfileAttributeValue>
        {
            [city.Id] = new() { AttributeDefinitionId = city.Id, AttributeDefinition = city, StringValue = "Warsaw" },
            [experience.Id] = new() { AttributeDefinitionId = experience.Id, AttributeDefinition = experience, NumericValue = 5 },
        };

        CvPublishGate.IsSatisfied(attributes, values).Should().BeTrue();
        CvPublishGate.Missing(attributes, values).Should().BeEmpty();
    }

    [Fact]
    public void Missing_required_value_blocks_publish_and_reports_name()
    {
        var city = Definition("City", AttributeDataType.String);
        var experience = Definition("Experience", AttributeDataType.Numeric);
        var attributes = new List<PositionAttribute> { Required(city, 0), Required(experience, 1) };
        var values = new Dictionary<Guid, ProfileAttributeValue>
        {
            [city.Id] = new() { AttributeDefinitionId = city.Id, AttributeDefinition = city, StringValue = "Warsaw" },
        };

        CvPublishGate.IsSatisfied(attributes, values).Should().BeFalse();
        CvPublishGate.Missing(attributes, values).Should().ContainSingle().Which.Should().Be("Experience");
    }

    [Fact]
    public void Missing_optional_value_does_not_block_publish()
    {
        var city = Definition("City", AttributeDataType.String);
        var notes = Definition("Notes", AttributeDataType.Text);
        var attributes = new List<PositionAttribute>
        {
            Required(city, 0),
            new() { AttributeDefinitionId = notes.Id, AttributeDefinition = notes, IsRequired = false, SortOrder = 1 },
        };
        var values = new Dictionary<Guid, ProfileAttributeValue>
        {
            [city.Id] = new() { AttributeDefinitionId = city.Id, AttributeDefinition = city, StringValue = "Warsaw" },
        };

        CvPublishGate.IsSatisfied(attributes, values).Should().BeTrue();
    }

    [Fact]
    public void Boolean_false_counts_as_filled()
    {
        var willing = Definition("Willing to relocate", AttributeDataType.Boolean);
        var attributes = new List<PositionAttribute> { Required(willing) };
        var values = new Dictionary<Guid, ProfileAttributeValue>
        {
            [willing.Id] = new() { AttributeDefinitionId = willing.Id, AttributeDefinition = willing, BooleanValue = false },
        };

        CvPublishGate.IsSatisfied(attributes, values).Should().BeTrue();
    }

    [Fact]
    public void Period_needs_only_start_date()
    {
        var work = Definition("Work history", AttributeDataType.Period);
        var attributes = new List<PositionAttribute> { Required(work) };

        var startedOnly = new Dictionary<Guid, ProfileAttributeValue>
        {
            [work.Id] = new()
            {
                AttributeDefinitionId = work.Id,
                AttributeDefinition = work,
                PeriodStart = new DateOnly(2020, 1, 1),
            },
        };
        CvPublishGate.IsSatisfied(attributes, startedOnly).Should().BeTrue();

        var endOnly = new Dictionary<Guid, ProfileAttributeValue>
        {
            [work.Id] = new()
            {
                AttributeDefinitionId = work.Id,
                AttributeDefinition = work,
                PeriodEnd = new DateOnly(2022, 1, 1),
            },
        };
        CvPublishGate.IsSatisfied(attributes, endOnly).Should().BeFalse();
    }

    [Fact]
    public void Missing_names_are_ordered_by_sort_order()
    {
        var second = Definition("Second", AttributeDataType.String);
        var first = Definition("First", AttributeDataType.String);
        var attributes = new List<PositionAttribute> { Required(second, 5), Required(first, 1) };

        CvPublishGate.Missing(attributes, new Dictionary<Guid, ProfileAttributeValue>())
            .Should().ContainInOrder("First", "Second");
    }

    [Fact]
    public void Each_data_type_is_recognized_as_filled()
    {
        IsFilled(AttributeDataType.String, v => v.StringValue = "s").Should().BeTrue();
        IsFilled(AttributeDataType.Text, v => v.TextValue = "t").Should().BeTrue();
        IsFilled(AttributeDataType.Numeric, v => v.NumericValue = 1.5m).Should().BeTrue();
        IsFilled(AttributeDataType.Date, v => v.DateValue = new DateOnly(2024, 1, 1)).Should().BeTrue();
        IsFilled(AttributeDataType.Dropdown, v => v.DropdownOption = "A").Should().BeTrue();
        IsFilled(AttributeDataType.Image, v => v.ImageObjectKey = "users/user/profile/image.png").Should().BeTrue();
    }

    private static bool IsFilled(AttributeDataType type, Action<ProfileAttributeValue> set)
    {
        var definition = Definition(type.ToString(), type);
        var value = new ProfileAttributeValue
        {
            AttributeDefinitionId = definition.Id,
            AttributeDefinition = definition,
        };
        set(value);
        return CvPublishGate.IsSatisfied([Required(definition)], new Dictionary<Guid, ProfileAttributeValue>
        {
            [definition.Id] = value,
        });
    }
}
