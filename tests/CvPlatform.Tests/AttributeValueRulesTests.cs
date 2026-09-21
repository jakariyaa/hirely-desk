using CvPlatform.Application.Attributes;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using AwesomeAssertions;

namespace CvPlatform.Tests;

public class AttributeValueRulesTests
{
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
            Guid.NewGuid(), DropdownOption: "A"))
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
            Guid.NewGuid(), StringValue: "value"))
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
            Guid.NewGuid(), BooleanValue: false))
            .Should().BeNull();
    }

    [Fact]
    public void Validate_rejects_non_cloudinary_image_urls()
    {
        var definition = new AttributeDefinition
        {
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), ImageUrl: "https://images.example.test/photo.jpg"))
            .Should().Be("Image must be uploaded through Cloudinary.");
    }

    [Fact]
    public void Validate_accepts_cloudinary_image_urls()
    {
        var definition = new AttributeDefinition
        {
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
        };

        AttributeValueRules.Validate(definition, new AttributeValueInput(
            Guid.NewGuid(), ImageUrl: "https://res.cloudinary.com/demo/image/upload/photo.jpg"))
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
