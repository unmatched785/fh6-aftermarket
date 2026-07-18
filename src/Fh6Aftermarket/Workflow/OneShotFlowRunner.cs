using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Input;
using Fh6Aftermarket.Safety;

namespace Fh6Aftermarket.Workflow;

public sealed record ForegroundWindowState(string Title, int Width, int Height);

public sealed record OneShotRunResult(string FlowId, int KeysSent);

public sealed class OneShotFlowRunner
{
    private readonly IKeySender _keySender;
    private readonly Func<ForegroundWindowState> _readForegroundWindow;
    private readonly Action<int> _delay;
    private readonly Action<string> _log;

    public OneShotFlowRunner(
        IKeySender keySender,
        Func<ForegroundWindowState> readForegroundWindow,
        Action<int> delay,
        Action<string> log)
    {
        _keySender = keySender;
        _readForegroundWindow = readForegroundWindow;
        _delay = delay;
        _log = log;
    }

    public OneShotRunResult Run(
        WorkflowFlow flow,
        SafetySettings safety,
        CancellationToken cancellationToken)
    {
        Validate(flow, safety);

        var keysSent = 0;
        foreach (var step in flow.Steps)
        {
            for (var repeat = 0; repeat < step.Repeat; repeat++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_keySender.IsDown(safety.EmergencyStopKey))
                {
                    throw new OperationCanceledException(
                        $"Emergency stop key {safety.EmergencyStopKey} is pressed.",
                        cancellationToken);
                }

                var foreground = _readForegroundWindow();
                if (!string.Equals(
                        foreground.Title,
                        safety.RequiredWindowTitle,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Foreground changed. Expected '{safety.RequiredWindowTitle}', " +
                        $"got '{foreground.Title}'.");
                }

                if (!ScreenGeometry.TryCreate(
                        foreground.Width,
                        foreground.Height,
                        out _,
                        safety.AspectRatioTolerance))
                {
                    throw new InvalidOperationException(
                        $"Unsupported foreground size: {foreground.Width}x{foreground.Height}.");
                }

                _keySender.Send(step.Key!);
                keysSent++;
                _log($"[{keysSent}] {step.Key} - {step.Label}");
                _delay(safety.InputDelayMilliseconds);
            }
        }

        return new OneShotRunResult(flow.Id, keysSent);
    }

    private static void Validate(WorkflowFlow flow, SafetySettings safety)
    {
        SafetySettingsLoader.Validate(safety);

        if (!safety.AutomationEnabled)
        {
            throw new InvalidOperationException(
                "Live input is disabled by safety.json (automationEnabled=false).");
        }

        if (!flow.AutomationReady)
        {
            throw new InvalidOperationException(
                $"Flow '{flow.Id}' has not passed live one-shot validation.");
        }

        if (!safety.SupportedLanguages.Contains(flow.FromLanguage, StringComparer.OrdinalIgnoreCase) ||
            !safety.SupportedLanguages.Contains(flow.ToLanguage, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Flow '{flow.Id}' uses a language outside the safety allowlist.");
        }

        if (flow.Steps.Any(step =>
                !string.Equals(step.Kind, "key", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(step.Key) ||
                step.Repeat is null or < 1))
        {
            throw new InvalidOperationException(
                "One-shot runner accepts validated key-only flows.");
        }

        var keyCount = flow.Steps.Sum(step => step.Repeat!.Value);
        if (keyCount > safety.MaxKeysPerOneShot)
        {
            throw new InvalidOperationException(
                $"Flow needs {keyCount} keys; safety limit is {safety.MaxKeysPerOneShot}.");
        }
    }
}
