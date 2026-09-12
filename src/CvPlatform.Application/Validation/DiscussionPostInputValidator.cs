using CvPlatform.Application.Discussions;
using FluentValidation;

namespace CvPlatform.Application.Validation;

public sealed class DiscussionPostInputValidator : AbstractValidator<DiscussionPostInput>
{
    public DiscussionPostInputValidator()
    {
        RuleFor(x => x.TextMarkdown).NotEmpty().MaximumLength(2000);
    }
}
