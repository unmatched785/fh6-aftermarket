using Fh6Aftermarket.Safety;

namespace Fh6Aftermarket.Gui;

public sealed record AutomationTimingSettings
{
    public int InputDelayMilliseconds { get; init; } = 850;

    public int TransitionDelayMilliseconds { get; init; } = 2_500;

    public int FastTravelLoadingMilliseconds { get; init; } = 15_000;

    public int RestartLoadingMilliseconds { get; init; } = 60_000;

    public int PostRestartFirstDelayMilliseconds { get; init; } = 15_000;

    public int PostRestartSecondDelayMilliseconds { get; init; } = 30_000;

    public int OpenWorldMapDelayMilliseconds { get; init; } = 10_000;

    public int GeneralKeyDelayMilliseconds => InputDelayMilliseconds;

    public int StartDelayMilliseconds =>
        Math.Clamp(TransitionDelayMilliseconds, 1_000, 2_000);

    public int RepeatedKeyDelayMilliseconds =>
        Math.Clamp(InputDelayMilliseconds / 10, 30, 120);

    public int PointerSettleMilliseconds =>
        Math.Max(InputDelayMilliseconds / 2, 150);

    public int PauseMenuSettleMilliseconds =>
        Math.Max(TransitionDelayMilliseconds / 3, 250);

    public int MapTransitionDelayMilliseconds => TransitionDelayMilliseconds;

    public int MapZoomDurationMilliseconds =>
        Math.Max(TransitionDelayMilliseconds, 800);

    public int VehicleCardSettleMilliseconds =>
        Math.Max(TransitionDelayMilliseconds / 3, 350);

    public static AutomationTimingSettings FromSafety(SafetySettings safety) =>
        new()
        {
            InputDelayMilliseconds = safety.InputDelayMilliseconds
        };

    public static void Validate(AutomationTimingSettings settings)
    {
        ValidateMilliseconds(settings.InputDelayMilliseconds, 50, 5_000, nameof(InputDelayMilliseconds));
        ValidateMilliseconds(settings.TransitionDelayMilliseconds, 250, 15_000, nameof(TransitionDelayMilliseconds));
        ValidateMilliseconds(settings.FastTravelLoadingMilliseconds, 0, 300_000, nameof(FastTravelLoadingMilliseconds));
        ValidateMilliseconds(settings.RestartLoadingMilliseconds, 0, 600_000, nameof(RestartLoadingMilliseconds));
        ValidateMilliseconds(
            settings.PostRestartFirstDelayMilliseconds,
            0,
            120_000,
            nameof(PostRestartFirstDelayMilliseconds));
        ValidateMilliseconds(
            settings.PostRestartSecondDelayMilliseconds,
            0,
            120_000,
            nameof(PostRestartSecondDelayMilliseconds));
        ValidateMilliseconds(
            settings.OpenWorldMapDelayMilliseconds,
            0,
            120_000,
            nameof(OpenWorldMapDelayMilliseconds));
    }

    private static void ValidateMilliseconds(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"{minimum}..{maximum} ms 범위를 벗어났습니다.");
        }
    }
}
