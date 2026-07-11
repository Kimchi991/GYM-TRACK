using System.Text.Json.Serialization;

namespace GymTrackPro.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionStatus
{
    Trial,
    Active,
    GracePeriod,
    Suspended,
    Expired,
    Cancelled
}
