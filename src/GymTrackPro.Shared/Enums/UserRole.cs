using System.Text.Json.Serialization;

namespace GymTrackPro.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    Administrator,
    Receptionist
}
