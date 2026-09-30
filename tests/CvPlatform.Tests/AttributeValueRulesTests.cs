using CvPlatform.Application.Attributes;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using AwesomeAssertions;

namespace CvPlatform.Tests;

public class AttributeValueRulesTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public void ValidateOptions_accepts_type_specific_tuning()
    {
        AttributeValueRules.ValidateOptions(
            AttributeDataType.String, "{\"maxLength\":20,\"regex\":\"^[A-Z]+$\"}")
            .Should().BeNull();
        AttributeValueRules.ValidateOptions(
            AttributeDataType.Numeric, "{\"min\":1,\"max\":10}")
            .Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_rejects_invalid_ranges_and_shapes()
    {
        AttributeValueRules.ValidateOptions(
            AttributeDataType.Numeric, "{\"min\":10,\"max\":1}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(
            AttributeDataType.String, "{\"min\":1}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(
            AttributeDataType.Dropdown, "{\"choices\":[\"A\",\"A\"]}")
            .Should().NotBeNull();
    }

    [Fact]
    public void Validate_accepts_trimmed_dropdown_choice()
    {
        var definition = new AttributeDefinition
        {
            Name = "Band",
            DataType = AttributeDataType.Dropdown,
            OptionsJson = "{\"choices\":[\"  A  \"]}",
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DropdownOption: "A"), Today)
            .Should().BeNull();
    }

    [Fact]
    public void Validate_rejects_invalid_regex_configuration()
    {
        var definition = new AttributeDefinition
        {
            Name = "Name",
            DataType = AttributeDataType.String,
            OptionsJson = "{\"regex\":\"[\"}",
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), StringValue: "value"), Today)
            .Should().Be("Attribute options are invalid.");
    }

    [Fact]
    public void Validate_counts_false_boolean_as_valid_shape()
    {
        var definition = new AttributeDefinition
        {
            Name = "Available",
            DataType = AttributeDataType.Boolean,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), BooleanValue: false), Today)
            .Should().BeNull();
    }

    [Fact]
    public void Validate_rejects_invalid_image_object_keys()
    {
        var definition = new AttributeDefinition
        {
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), ImageObjectKey: "../photo.jpg"), Today)
            .Should().Be("Image object key is invalid.");
    }

    [Fact]
    public void Validate_accepts_image_object_keys()
    {
        var definition = new AttributeDefinition
        {
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), ImageObjectKey: "users/user/profile/image.jpg"), Today)
            .Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_accepts_date_bounds_for_date_attributes()
    {
        AttributeValueRules.ValidateOptions(
            AttributeDataType.Date, "{\"minDate\":\"2000-01-01\",\"maxDate\":\"2030-12-31\"}")
            .Should().BeNull();
        AttributeValueRules.ValidateOptions(AttributeDataType.Date, "{\"minAgeDays\":0,\"maxAgeDays\":43800}")
            .Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_rejects_misplaced_or_inconsistent_date_bounds()
    {
        AttributeValueRules.ValidateOptions(AttributeDataType.String, "{\"maxDate\":\"2030-01-01\"}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(AttributeDataType.Period, "{\"maxAgeDays\":10}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(AttributeDataType.Date, "{\"minDate\":\"2030-01-01\",\"maxDate\":\"2020-01-01\"}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(AttributeDataType.Date, "{\"minAgeDays\":-1}")
            .Should().NotBeNull();
        AttributeValueRules.ValidateOptions(AttributeDataType.Date, "{\"maxAgeDays\":10,\"minAgeDays\":20}")
            .Should().NotBeNull();
    }

    [Fact]
    public void Validate_rejects_dates_in_the_future()
    {
        var definition = new AttributeDefinition
        {
            Name = "Me.BirthDate",
            DataType = AttributeDataType.Date,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: Today.AddDays(1)), Today)
            .Should().Be("Date cannot be in the future.");
    }

    [Fact]
    public void Validate_accepts_today_and_rejects_dates_beyond_the_age_band()
    {
        var definition = new AttributeDefinition
        {
            Name = "Me.BirthDate",
            DataType = AttributeDataType.Date,
            OptionsJson = "{\"minAgeDays\":0,\"maxAgeDays\":43830}",
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: Today), Today)
            .Should().BeNull();
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: Today.AddYears(-30)), Today)
            .Should().BeNull();
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: Today.AddYears(-121)), Today)
            .Should().NotBeNull();
    }

    [Fact]
    public void Validate_honours_absolute_date_bounds()
    {
        var definition = new AttributeDefinition
        {
            Name = "GraduationYear",
            DataType = AttributeDataType.Date,
            OptionsJson = "{\"minDate\":\"2015-01-01\"}",
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: new DateOnly(2014, 12, 31)), Today)
            .Should().NotBeNull();
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: new DateOnly(2016, 1, 1)), Today)
            .Should().BeNull();
    }

    [Fact]
    public void Validate_allows_future_dates_when_the_maximum_is_explicitly_widened()
    {
        var definition = new AttributeDefinition
        {
            Name = "AvailableFrom",
            DataType = AttributeDataType.Date,
            OptionsJson = "{\"maxDate\":\"2030-12-31\"}",
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: new DateOnly(2027, 1, 1)), Today)
            .Should().BeNull();
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), DateValue: new DateOnly(2031, 1, 1)), Today)
            .Should().NotBeNull();
    }

    [Fact]
    public void Validate_rejects_future_period_bounds()
    {
        var definition = new AttributeDefinition
        {
            Name = "Experience",
            DataType = AttributeDataType.Period,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), PeriodStart: Today.AddMonths(-6), PeriodEnd: Today.AddMonths(6)), Today)
            .Should().Be("Period end cannot be in the future.");
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), PeriodStart: Today.AddMonths(1)), Today)
            .Should().Be("Period start cannot be in the future.");
        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), PeriodStart: Today.AddMonths(-6)), Today)
            .Should().BeNull();
    }

    [Fact]
    public void ValidateComparison_rejects_invalid_operator_and_value()
    {
        var definition = new AttributeDefinition
        {
            Name = "Score",
            DataType = AttributeDataType.Numeric,
        };

        AttributeValueRules.ValidateComparison(definition, RuleOperator.Contains, "x")
            .Should().NotBeNull();
        AttributeValueRules.ValidateComparison(definition, RuleOperator.GreaterThan, "x")
            .Should().NotBeNull();
    }
}
