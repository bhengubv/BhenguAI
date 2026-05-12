namespace Bhengu.AI.Wearable;

/// <summary>
/// Biometric snapshot injected into the Companion context on wearable surfaces.
/// Values are optional — only populated when the sensor is available and consented.
/// </summary>
public sealed record WearableContext(
    double? HeartRateBpm,
    int?    StepCountToday,
    double? SpO2Percent,
    double? SkinTempCelsius,
    bool    IsWorkoutActive,
    DateTimeOffset CapturedAt);
