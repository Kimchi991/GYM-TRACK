using System.Threading.Tasks;

namespace GymTrackPro.Shared.Interfaces;

public interface IFirebaseEmailService
{
    Task SendVerificationEmailAsync(string email, string token);
    Task SendPasswordResetEmailAsync(string email, string resetToken);
    Task SendEmailAsync(string email, string subject, string body);
}
