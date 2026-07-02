using System;

namespace GymTrackPro.Shared.DTOs;

public class MembershipPlanResponseDto
{
    public int PlanID { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
