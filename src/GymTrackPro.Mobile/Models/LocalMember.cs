using System;
using SQLite;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.Models;

[Table("Members")]
public class LocalMember
{
    [PrimaryKey]
    public int MemberID { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;

    public DateTime BirthDate { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string EmergencyContact { get; set; } = string.Empty;

    public string? ProfilePicture { get; set; }

    public string QRCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime DateRegistered { get; set; }

    public DateTime LastModified { get; set; }

    [Indexed]
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Synced;
}
