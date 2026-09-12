using CvPlatform.Application.Attributes;
using FluentValidation;

namespace CvPlatform.Application.Validation;

public sealed class AttributeDefinitionInputValidator : AbstractValidator<AttributeDefinitionInput>
{
    public AttributeDefinitionInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.OptionsJson).Custom((options, context) =>
        {
            var error = AttributeValueRules.ValidateOptions(context.InstanceToValidate.DataType, options);
            if (error is not null)
                context.AddFailure("OptionsJson", error);
        });
    }
}
