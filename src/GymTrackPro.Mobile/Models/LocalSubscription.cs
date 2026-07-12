using System;
using SQLite;

namespace GymTrackPro.Mobile.Models;

public class LocalSubscription
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int RemoteId { get; set; }
    public int MemberId { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty; // Active, Paused, Cancelled, Expired
    
    public bool NeedsSync { get; set; }
}
