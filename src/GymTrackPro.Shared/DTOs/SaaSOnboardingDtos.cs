using System;

namespace GymTrackPro.Shared.DTOs;

public class TenantOnboardingDto
{
    public string GymName { get; set; } = string.Empty;
    public string GymAddress { get; set; } = string.Empty;
    public string GymContactNumber { get; set; } = string.Empty;

    public string AdminEmail { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminFirstName { get; set; } = string.Empty;
    public string AdminLastName { get; set; } = string.Empty;
    public string? AdminPassword { get; set; }
    public string? FirebaseUid { get; set; }

    public int PlanID { get; set; } = 1;
}

public class OnboardingResponseDto
{
    public int GymID { get; set; }
    public string GymName { get; set; } = string.Empty;
    public int AdminUserID { get; set; }
    public string AdminUsername { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class GymSubscriptionDto
{
    public int SubscriptionID { get; set; }
    public int GymID { get; set; }
    public string GymName { get; set; } = string.Empty;
    public int PlanID { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
}
