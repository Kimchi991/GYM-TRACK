using System;
using SQLite;

namespace GymTrackPro.Mobile.Models;

public class LocalAttendance
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int RemoteId { get; set; }
    public int MemberId { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    
    // For Gym Goers caching their own attendance
    public bool IsCurrentUser { get; set; }
    
    public bool NeedsSync { get; set; }
}
