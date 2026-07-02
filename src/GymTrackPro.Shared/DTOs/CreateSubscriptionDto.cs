using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CreateSubscriptionDto
{
    [Required]
    public int MemberID { get; set; }

    [Required]
    public int PlanID { get; set; }

    [Required]
    public DateTime StartDate { get; set; }
}
