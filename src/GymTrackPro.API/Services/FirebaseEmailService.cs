using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class FirebaseEmailService : IFirebaseEmailService
{
    private readonly ILogger<FirebaseEmailService> _logger;

    public FirebaseEmailService(ILogger<FirebaseEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(string email, string token)
    {
        _logger.LogInformation("Sending verification email to {Email} with verification token: {Token}", email, token);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        _logger.LogInformation("Sending password reset email to {Email} with reset token: {ResetToken}", email, resetToken);
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string email, string subject, string body)
    {
        _logger.LogInformation("Sending email to {Email}. Subject: '{Subject}', Body: '{Body}'", email, subject, body);
        return Task.CompletedTask;
    }
}
