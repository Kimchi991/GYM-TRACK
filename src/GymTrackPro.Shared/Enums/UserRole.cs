using System.Text.Json.Serialization;

namespace GymTrackPro.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    Administrator = 0,
    Receptionist = 1,
    GymGoer = 2,
    Trainer = 3
}
