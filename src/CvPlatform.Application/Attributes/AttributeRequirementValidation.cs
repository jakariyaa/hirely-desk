namespace CvPlatform.Application.Attributes;

public static class AttributeRequirementValidation
{
    public static string? Validate(IReadOnlyList<AttributeRequirementInput>? attributes)
    {
        if (attributes is null or { Count: 0 })
            return null;

        if (attributes.Any(a => a.AttributeDefinitionId == Guid.Empty))
            return "Attribute definition is required.";
        if (attributes.Select(a => a.AttributeDefinitionId).Distinct().Count() != attributes.Count)
            return "An attribute cannot be added more than once.";
        if (attributes.Any(a => a.SortOrder < 0))
            return "Attribute order cannot be negative.";
        if (attributes.Select(a => a.SortOrder).Distinct().Count() != attributes.Count)
            return "Attribute order values must be unique.";

        return null;
    }
}
