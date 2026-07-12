using System;
using SQLite;

namespace GymTrackPro.Mobile.Models;

[Table("SyncQueue")]
public class SyncQueue
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string AccountUid { get; set; } = string.Empty;

    [Indexed]
    public string TableName { get; set; } = string.Empty;

    [Indexed]
    public string RecordId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty; // e.g. "CREATE", "UPDATE", "DELETE"

    public string SerializedData { get; set; } = string.Empty; // JSON representation of the payload

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
