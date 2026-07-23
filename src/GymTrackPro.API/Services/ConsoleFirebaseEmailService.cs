using System.Threading.Tasks;
using GymTrackPro.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.API.Services;

public class ConsoleFirebaseEmailService : IFirebaseEmailService
{
    private readonly ILogger<ConsoleFirebaseEmailService> _logger;

    public ConsoleFirebaseEmailService(ILogger<ConsoleFirebaseEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(string email, string token)
    {
        _logger.LogInformation("DEBUG Verification Email: Sent to {Email} with Token {Token}", email, token);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        _logger.LogInformation("DEBUG Password Reset Email: Sent to {Email} with Token {ResetToken}", email, resetToken);
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string email, string subject, string body)
    {
        _logger.LogInformation("DEBUG General Email: Sent to {Email}\nSubject: {Subject}\nBody:\n{Body}", email, subject, body);
        return Task.CompletedTask;
    }
}
