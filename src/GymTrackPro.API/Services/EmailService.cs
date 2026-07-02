using System.Threading.Tasks;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class EmailService : IEmailService
{
    private readonly IFirebaseEmailService _firebaseProvider;

    public EmailService(IFirebaseEmailService firebaseProvider)
    {
        _firebaseProvider = firebaseProvider;
    }

    public async Task SendVerificationEmailAsync(string email, string token)
    {
        await _firebaseProvider.SendVerificationEmailAsync(email, token);
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        await _firebaseProvider.SendPasswordResetEmailAsync(email, resetToken);
    }

    public async Task SendEmailAsync(string email, string subject, string body)
    {
        await _firebaseProvider.SendEmailAsync(email, subject, body);
    }
}
