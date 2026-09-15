namespace CvPlatform.Core.Email;

public interface IAppEmailSender
{
    Task SendConfirmationLinkAsync(string email, string confirmationLink, CancellationToken ct = default);
}
