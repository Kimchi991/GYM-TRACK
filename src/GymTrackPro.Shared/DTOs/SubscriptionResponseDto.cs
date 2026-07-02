using System;

namespace GymTrackPro.Shared.DTOs;

public class SubscriptionResponseDto
{
    public int SubscriptionID { get; set; }
    public int MemberID { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int PlanID { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
