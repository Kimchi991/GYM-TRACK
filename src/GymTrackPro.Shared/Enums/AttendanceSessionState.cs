using System.Text.Json.Serialization;

namespace GymTrackPro.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttendanceSessionState
{
    CheckedOut = 0,
    CheckedIn = 1
}
