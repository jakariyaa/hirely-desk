using CvPlatform.Application.Projects;
using FluentValidation;

namespace CvPlatform.Application.Validation;

public sealed class ProjectInputValidator : AbstractValidator<ProjectInput>
{
    public ProjectInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionMarkdown).MaximumLength(20000).When(x => x.DescriptionMarkdown is not null);
        RuleFor(x => x)
            .Must(x => x.PeriodStart is null || x.PeriodEnd is null || x.PeriodEnd >= x.PeriodStart)
            .WithMessage("Period end cannot be earlier than period start.");
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50).When(x => x.Tags is not null);
        RuleFor(x => x.Tags.Count).LessThanOrEqualTo(20).When(x => x.Tags is not null);
    }
}
