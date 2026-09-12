using CvPlatform.Application.Positions;
using FluentValidation;

namespace CvPlatform.Application.Validation;

public sealed class PositionInputValidator : AbstractValidator<PositionInput>
{
    public PositionInputValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortDescription).MaximumLength(2000).When(x => x.ShortDescription is not null);
        RuleFor(x => x.Company).MaximumLength(200).When(x => x.Company is not null);
        RuleFor(x => x.Level).MaximumLength(100).When(x => x.Level is not null);
        RuleFor(x => x.MaxProjects).InclusiveBetween(0, 100);
    }
}
