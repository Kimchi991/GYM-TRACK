using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Events.Authentication;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDomainEventPublisher _eventPublisher;

    public AuthenticationService(
        IUserRepository userRepository,
        IEmailService emailService,
        IConfiguration configuration,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        IDomainEventPublisher eventPublisher)
    {
        _userRepository = userRepository;
        _emailService = emailService;
        _configuration = configuration;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _eventPublisher = eventPublisher;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<UserResponseDto?> AuthenticateAsync(LoginDto loginDto)
    {
        var user = await _userRepository.GetByUsernameAsync(loginDto.Username);
        if (user == null)
        {
            await _auditService.LogActivityAsync(null, "Login Failure", $"Failed login attempt for username: {loginDto.Username}. User not found.", GetClientIpAddress());
            return null;
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
        {
            await _auditService.LogActivityAsync(user.UserID, "Login Failure", $"Failed login attempt for username: {user.Username}. Invalid password.", GetClientIpAddress());
            return null;
        }

        // Enforce business rules
        if (!user.IsActive)
        {
            await _auditService.LogActivityAsync(user.UserID, "Login Failure", $"Failed login attempt for username: {user.Username}. Account is inactive.", GetClientIpAddress());
            throw new InvalidOperationException("User account is inactive. Please contact the administrator.");
        }

        // Allow bypassing email verification if configured in development (default to strict check)
        bool bypassVerification = _configuration.GetValue<bool>("Development:BypassEmailVerification", false);
        if (!user.EmailVerified && !bypassVerification)
        {
            await _auditService.LogActivityAsync(user.UserID, "Login Failure", $"Failed login attempt for username: {user.Username}. Email not verified.", GetClientIpAddress());
            throw new InvalidOperationException("Email address is not verified. Please verify your email first.");
        }

        // Update last login timestamp
        await _userRepository.UpdateLastLoginAsync(user.UserID);

        // Log successful login
        await _auditService.LogActivityAsync(user.UserID, "Login Success", $"User {user.Username} logged in successfully.", GetClientIpAddress());

        // Generate JWT Token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? "GymTrackProSecretKeyPlaceholder123456");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("FirstName", user.FirstName),
                new Claim("LastName", user.LastName)
            }),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = _configuration["Jwt:Issuer"] ?? "GymTrackProAPI",
            Audience = _configuration["Jwt:Audience"] ?? "GymTrackProClient",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return new UserResponseDto
        {
            UserID = user.UserID,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt,
            Token = tokenString
        };
    }

    public async Task<UserResponseDto> RegisterAsync(RegisterUserDto registerUserDto)
    {
        if (await _userRepository.UsernameExistsAsync(registerUserDto.Username))
        {
            throw new InvalidOperationException("Username is already taken.");
        }

        if (await _userRepository.EmailExistsAsync(registerUserDto.Email))
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var verificationToken = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Username = registerUserDto.Username,
            Email = registerUserDto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerUserDto.Password),
            FirstName = registerUserDto.FirstName,
            LastName = registerUserDto.LastName,
            Role = UserRole.Receptionist,
            IsActive = true,
            EmailVerified = false,
            VerificationToken = verificationToken,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        await _auditService.LogActivityAsync(user.UserID, "Register Success", $"User {user.Username} registered successfully.", GetClientIpAddress());

        // Send verification email statefully
        await _emailService.SendVerificationEmailAsync(user.Email, verificationToken);

        return new UserResponseDto
        {
            UserID = user.UserID,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        if (user == null)
        {
            // Fail silently or return false to prevent user enumeration
            return false;
        }

        var resetToken = Guid.NewGuid().ToString("N");
        user.ResetToken = resetToken;
        user.ResetTokenExpires = DateTime.UtcNow.AddHours(2);
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        await _auditService.LogActivityAsync(user.UserID, "Password Reset Request", $"Password reset requested for {user.Username}.", GetClientIpAddress());

        // Send password reset email
        await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken);

        // Publish Domain Event
        await _eventPublisher.PublishAsync(new PasswordResetRequestedEvent
        {
            UserId = user.UserID,
            Username = user.Username,
            Email = user.Email,
            ResetToken = resetToken
        });

        return true;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
    {
        var user = await _userRepository.GetByEmailAsync(resetPasswordDto.Email);
        if (user == null)
        {
            return false;
        }

        if (user.ResetToken != resetPasswordDto.Token || user.ResetTokenExpires < DateTime.UtcNow)
        {
            return false;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(resetPasswordDto.NewPassword);
        user.ResetToken = null;
        user.ResetTokenExpires = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        await _auditService.LogActivityAsync(user.UserID, "Password Reset Success", $"Password reset successfully for {user.Username}.", GetClientIpAddress());

        return true;
    }

    public async Task<bool> VerifyEmailAsync(string email, string token)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        if (user == null)
        {
            return false;
        }

        if (user.VerificationToken != token)
        {
            return false;
        }

        user.EmailVerified = true;
        user.VerificationToken = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        await _auditService.LogActivityAsync(user.UserID, "Email Verified", $"User {user.Username} email verified successfully.", GetClientIpAddress());

        return true;
    }
}
