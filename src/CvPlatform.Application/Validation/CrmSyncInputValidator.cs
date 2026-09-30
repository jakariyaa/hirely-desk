using CvPlatform.Application.Crm;
using FluentValidation;

namespace CvPlatform.Application.Validation;

public sealed class CrmSyncInputValidator : AbstractValidator<CrmSyncInput>
{
    public CrmSyncInputValidator()
    {
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Industry).MaximumLength(255).When(x => x.Industry is not null);
        RuleFor(x => x.AccountPhone).MaximumLength(40).When(x => x.AccountPhone is not null);
        RuleFor(x => x.Website).MaximumLength(255).When(x => x.Website is not null);
        RuleFor(x => x.ContactTitle).MaximumLength(128).When(x => x.ContactTitle is not null);
        RuleFor(x => x.ContactMobilePhone).MaximumLength(40).When(x => x.ContactMobilePhone is not null);
        RuleFor(x => x.MailingCity).MaximumLength(255).When(x => x.MailingCity is not null);
        RuleFor(x => x.MailingCountry).MaximumLength(255).When(x => x.MailingCountry is not null);
        RuleFor(x => x.Description).MaximumLength(32000).When(x => x.Description is not null);
    }
}
