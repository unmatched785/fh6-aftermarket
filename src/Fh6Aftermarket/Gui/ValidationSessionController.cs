using System.Diagnostics;
using System.Drawing;
using System.Media;
using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Input;
using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Safety;
using Fh6Aftermarket.Vision;

namespace Fh6Aftermarket.Gui;

public enum ValidationSessionState
{
    Stopped,
    Running,
    Paused,
    NeedsAttention,
    TargetFound,
    CycleComplete
}

public sealed record VehicleObservation(
    DateTimeOffset ObservedAt,
    string Name,
    double Confidence,
    bool IsTarget);

public sealed record ValidationSessionSnapshot(
    ValidationSessionState State,
    string Status,
    string CurrentStep,
    TimeSpan Elapsed,
    int RecognitionRetries,
    int DuplicateObservations,
    IReadOnlyList<VehicleObservation> Vehicles,
    IReadOnlyList<string> LogLines,
    int? RemainingSeconds,
    bool PracticalInputEnabled);

public sealed class ValidationSessionController
{
    private sealed record ActivePhase(
        string Status,
        string Detail,
        DateTimeOffset Deadline);

    private readonly SafetySettings _safety;
    private readonly AftermarketMapCardAnalyzer _cardAnalyzer;
    private readonly PauseMenuLanguageDetector _languageDetector;
    private readonly IKeySender _keySender;
    private readonly IMouseSender _mouseSender;
    private AutomationTimingSettings _timingSettings;
    private readonly Stopwatch _stopwatch = new();
    private readonly List<VehicleObservation> _vehicles = [];
    private readonly List<string> _logLines = [];
    private readonly HashSet<string> _uniqueVehicleNames = new(StringComparer.Ordinal);
    private readonly string _logDirectory;
    private string? _lastObservedName;
    private string? _lastObservationLogKey;
    private string? _logPath;
    private int _recognitionRetries;
    private int _duplicateObservations;
    private int _tickInProgress;
    private int _observationGeneration;
    private int _restartInProgress;
    private GameLanguage? _currentLanguage;
    private ActivePhase? _activePhase;

    public ValidationSessionController(
        SafetySettings safety,
        AftermarketMapCardAnalyzer cardAnalyzer,
        PauseMenuLanguageDetector languageDetector,
        string logDirectory,
        IKeySender? keySender = null,
        IMouseSender? mouseSender = null)
    {
        _safety = safety;
        _cardAnalyzer = cardAnalyzer;
        _languageDetector = languageDetector;
        _logDirectory = logDirectory;
        _keySender = keySender ?? new WindowsKeySender();
        _mouseSender = mouseSender ?? new WindowsMouseSender();
        _timingSettings = AutomationTimingSettings.FromSafety(safety);
        Snapshot = CreateSnapshot(
            ValidationSessionState.Stopped,
            "준비됨 — 검증 모드",
            "F1 또는 시작 버튼을 누르세요");
    }

    public event Action<ValidationSessionSnapshot>? SnapshotChanged;

    public ValidationSessionSnapshot Snapshot { get; private set; }

    public AutomationTimingSettings TimingSettings => Volatile.Read(ref _timingSettings);

    public void UpdateTimingSettings(AutomationTimingSettings settings)
    {
        AutomationTimingSettings.Validate(settings);
        Volatile.Write(ref _timingSettings, settings);
    }

    public void StartOrResume()
    {
        if (Snapshot.State == ValidationSessionState.Running)
        {
            return;
        }

        if (_safety.PracticalStartEnabled &&
            Snapshot.State is ValidationSessionState.Stopped or
                ValidationSessionState.CycleComplete or
                ValidationSessionState.TargetFound)
        {
            StartLiveFromOpenWorld();
            return;
        }

        if (Snapshot.State is ValidationSessionState.Stopped or
            ValidationSessionState.CycleComplete or
            ValidationSessionState.TargetFound)
        {
            ResetCycle();
            Directory.CreateDirectory(_logDirectory);
            _logPath = Path.Combine(
                _logDirectory,
                $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _stopwatch.Restart();
            AddLog("검증 세션 시작. 키보드와 마우스 입력은 비활성화되어 있습니다.");
        }
        else if (Snapshot.State == ValidationSessionState.NeedsAttention)
        {
            _recognitionRetries = 0;
            AddLog("사용자가 확인 후 검증을 재개했습니다.");
        }
        else if (Snapshot.State == ValidationSessionState.Paused)
        {
            AddLog("검증 세션 재개.");
        }

        Interlocked.Increment(ref _observationGeneration);

        Publish(
            ValidationSessionState.Running,
            "FH6 화면 대기 중",
            "게임을 전경에 두고 지도 카드를 수동으로 선택하세요");
    }

    private async void StartLiveFromOpenWorld()
    {
        ResetCycle();
        Directory.CreateDirectory(_logDirectory);
        _logPath = Path.Combine(
            _logDirectory,
            $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _stopwatch.Restart();
        var generation = Interlocked.Increment(ref _observationGeneration);
        AddLog("실전 1회 시작: KOR/ENG 오픈월드 정차 상태에서 시작합니다.");
        var timing = Timing;
        AddLog(
            $"GUI 타이밍: 입력={timing.InputDelayMilliseconds}ms, " +
            $"전환={timing.TransitionDelayMilliseconds}ms, " +
            $"F1대기={timing.StartDelayMilliseconds}ms, " +
            $"빠른이동={timing.FastTravelLoadingMilliseconds}ms, " +
            $"전진={timing.ForwardDurationMilliseconds}ms(" +
            $"조향={timing.SteeringKey}/{timing.SteeringDurationMilliseconds}ms), " +
            $"재시작={timing.RestartLoadingMilliseconds}ms, " +
            $"시작화면1={timing.PostRestartFirstDelayMilliseconds}ms, " +
            $"시작화면2={timing.PostRestartSecondDelayMilliseconds}ms, " +
            $"오픈월드M={timing.OpenWorldMapDelayMilliseconds}ms");
        Publish(
            ValidationSessionState.Running,
            "오픈월드 시작 조건 확인 중",
            "FH6 전경과 16:9 화면을 확인합니다");

        try
        {
            if (!await DelayWithProgressAsync(
                    generation,
                    timing.StartDelayMilliseconds,
                    "F1 시작 준비 중",
                    "FH6 입력 전 잠시 대기"))
            {
                return;
            }

            using var capture = ForegroundWindowCapture.Capture();
            ValidateLiveForeground(capture);
            ThrowIfEmergencyStopIsDown();

            _keySender.Send("M");
            AddLog(
                $"실전 입력 1/50: M — 오픈월드에서 전체 지도 열기 " +
                $"(screen={capture.Image.Width}x{capture.Image.Height})");
            Publish(
                ValidationSessionState.Running,
                "전체 지도 열기 입력 완료",
                "Evolving World 필터를 적용합니다");
            _ = RunPracticalCycleAsync(generation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddLog($"실전 시작 거부: {exception.Message}");
            _stopwatch.Stop();
            Publish(
                ValidationSessionState.NeedsAttention,
                "실전 시작 거부 — 입력 없음",
                exception.Message);
        }
        catch (OperationCanceledException exception)
        {
            AddLog($"실전 시작 취소: {exception.Message}");
            _stopwatch.Stop();
            Publish(
                ValidationSessionState.Stopped,
                "중지됨",
                exception.Message);
        }
    }

    private async Task RunPracticalCycleAsync(int generation)
    {
        try
        {
            const int totalInputs = 50;
            var inputNumber = 2;

            Publish(
                ValidationSessionState.Running,
                "지도 필터 적용 중",
                "Evolving World 필터를 적용합니다");

            var locationFilterSteps = new List<(string Key, string Label, int DelayMs)>
            {
                ("PageDown", "지도 전환 대기 후 필터 열기", Timing.MapTransitionDelayMilliseconds),
                ("Enter", "All 필터 전환 1/2", Timing.GeneralKeyDelayMilliseconds),
                ("Enter", "All 필터 전환 2/2", Timing.GeneralKeyDelayMilliseconds)
            };

            for (var down = 1; down <= 4; down++)
            {
                locationFilterSteps.Add(("Down", $"Evolving World로 이동 {down}/4", Timing.RepeatedKeyDelayMilliseconds));
            }

            locationFilterSteps.Add(("Enter", "Evolving World 활성화", Timing.GeneralKeyDelayMilliseconds));
            locationFilterSteps.Add(("Escape", "지도 필터 닫기", Timing.GeneralKeyDelayMilliseconds));
            locationFilterSteps.Add(("Escape", "지도 닫기", Timing.GeneralKeyDelayMilliseconds));
            locationFilterSteps.Add(("M", "오픈월드에서 지도 다시 열기", Timing.GeneralKeyDelayMilliseconds));

            foreach (var step in locationFilterSteps)
            {
                if (!await SendPracticalKeyAfterDelayAsync(
                        generation,
                        inputNumber++,
                        totalInputs,
                        step.Key,
                        step.Label,
                        step.DelayMs))
                {
                    return;
                }
            }

            if (!await DelayWithProgressAsync(
                    generation,
                    Timing.MapTransitionDelayMilliseconds,
                    "지도 로딩 중",
                    "지구본 표시 대기"))
            {
                return;
            }

            using (var markerCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(markerCapture);
                var marker = AftermarketScreenObserver.Observe(markerCapture.Image);
                if (marker.Candidates.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"지구본을 하나로 찾지 못했습니다: " +
                        $"state={marker.State}, candidates={marker.Candidates.Count}");
                }

                var center = marker.Candidates[0].Center;
                AddLog(
                    $"지구본 후보 1개 사용: state={marker.State}, " +
                    $"center=({center.X},{center.Y})");
                _mouseSender.MoveTo(
                    markerCapture.ScreenX + center.X,
                    markerCapture.ScreenY + center.Y);
                AddLog($"마우스 이동: 지구본 중심 ({center.X},{center.Y})");
                Publish(
                    ValidationSessionState.Running,
                    "지구본 선택",
                    "빠른 이동 X와 확인 Enter를 진행합니다");
            }

            if (!await SendPracticalKeyAfterDelayAsync(
                    generation,
                    inputNumber++,
                    totalInputs,
                    "X",
                    "애프터마켓으로 빠른 이동",
                    Timing.PointerSettleMilliseconds) ||
                !await SendPracticalKeyAfterDelayAsync(
                    generation,
                    inputNumber++,
                    totalInputs,
                    "Enter",
                    "빠른 이동 확인",
                    Timing.GeneralKeyDelayMilliseconds))
            {
                return;
            }

            if (!await DelayWithProgressAsync(
                    generation,
                    Timing.FastTravelLoadingMilliseconds,
                    "애프터마켓으로 빠른 이동 중",
                    "도착 후 Aftermarket Cars 필터 적용"))
            {
                return;
            }

            ThrowIfEmergencyStopIsDown();
            using (var movementCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(movementCapture);
            }

            Publish(
                ValidationSessionState.Running,
                "플레이어 위치 분리 중",
                $"W {Timing.ForwardDurationMilliseconds}ms · " +
                $"조향 {Timing.SteeringKey} {Timing.SteeringDurationMilliseconds}ms");
            var forwardDuration = Timing.ForwardDurationMilliseconds;
            var steeringKey = Timing.SteeringKey;
            var steeringDuration = Timing.SteeringDurationMilliseconds;
            if (!await DriveForwardWithSteeringAsync(
                    generation,
                    forwardDuration,
                    steeringKey,
                    steeringDuration))
            {
                return;
            }

            AddLog(
                $"실전 입력 {inputNumber}/{totalInputs}: W hold {forwardDuration}ms + " +
                $"{steeringKey} hold {steeringDuration}ms — 아이콘 군집에서 전진 분리");
            inputNumber++;

            if (!await DelayWithProgressAsync(
                    generation,
                    Timing.PostMovementSettleMilliseconds,
                    "전진 완료",
                    "화면 안정화 대기"))
            {
                return;
            }

            var carFilterSteps = new List<(string Key, string Label, int DelayMs)>
            {
                ("M", "전진한 오픈월드에서 전체 지도 바로 열기", Timing.GeneralKeyDelayMilliseconds),
                ("PageDown", "지도 전환 대기 후 필터 열기", Timing.MapTransitionDelayMilliseconds),
                ("Enter", "All 필터 전환 1/2", Timing.GeneralKeyDelayMilliseconds),
                ("Enter", "All 필터 전환 2/2", Timing.GeneralKeyDelayMilliseconds)
            };

            for (var down = 1; down <= 26; down++)
            {
                carFilterSteps.Add(("Down", $"Aftermarket Cars로 이동 {down}/26", Timing.RepeatedKeyDelayMilliseconds));
            }

            carFilterSteps.Add(("Enter", "Aftermarket Cars 활성화", Timing.GeneralKeyDelayMilliseconds));
            carFilterSteps.Add(("Escape", "지도 필터 닫기", Timing.GeneralKeyDelayMilliseconds));
            carFilterSteps.Add(("Escape", "지도 닫기", Timing.GeneralKeyDelayMilliseconds));
            carFilterSteps.Add(("M", "오픈월드에서 지도 다시 열기", Timing.GeneralKeyDelayMilliseconds));

            foreach (var step in carFilterSteps)
            {
                if (!await SendPracticalKeyAfterDelayAsync(
                        generation,
                        inputNumber++,
                        totalInputs,
                        step.Key,
                        step.Label,
                        step.DelayMs))
                {
                    return;
                }
            }

            if (!await DelayWithProgressAsync(
                    generation,
                    Timing.MapTransitionDelayMilliseconds,
                    "지도 로딩 중",
                    "차량 아이콘 표시 대기"))
            {
                return;
            }

            ThrowIfEmergencyStopIsDown();
            using (var zoomCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(zoomCapture);
            }

            var zoomDuration = Timing.MapZoomDurationMilliseconds;
            if (!await HoldPracticalKeyAsync(
                    generation,
                    "Up",
                    zoomDuration,
                    "지도 최대 확대 중",
                    "위쪽 방향키 유지"))
            {
                return;
            }

            AddLog($"실전 입력 {inputNumber}/{totalInputs}: Up hold {zoomDuration}ms — 지도 최대 확대");
            inputNumber++;

            await Task.Delay(Timing.GeneralKeyDelayMilliseconds);
            if (!IsLiveGenerationActive(generation))
            {
                return;
            }

            using var clusterCapture = ForegroundWindowCapture.Capture();
            ValidateLiveForeground(clusterCapture);
            var clusters = AftermarketMapIconClusterDetector.Inspect(clusterCapture.Image);
            if (!clusters.HasSingleCluster)
            {
                throw new InvalidOperationException(
                    $"최대 확대 후 초록 차량 아이콘 군집을 하나로 찾지 못했습니다: " +
                    $"candidates={clusters.Candidates.Count}");
            }

            var cluster = clusters.Candidates[0];
            AddLog(
                $"초록 차량 아이콘 군집 확인: bounds={cluster.Bounds}, " +
                $"green={cluster.GreenPixelCount}, targets={cluster.ClickTargets.Count}");
            Publish(
                ValidationSessionState.Running,
                "초록 차량 아이콘 군집 확인 완료",
                "세 아이콘을 순서대로 클릭해 차량명을 판독합니다");

            await ScanClusterVehiclesAsync(
                generation,
                clusterCapture.ScreenX,
                clusterCapture.ScreenY,
                cluster);
        }
        catch (OperationCanceledException exception)
        {
            Stop(exception.Message);
        }
        catch (Exception exception)
        {
            AddLog($"실전 사이클 중단: {exception.Message}");
            _stopwatch.Stop();
            Publish(
                ValidationSessionState.NeedsAttention,
                "실전 사이클 중단",
                exception.Message);
        }
    }

    private async Task<bool> SendPracticalKeyAfterDelayAsync(
        int generation,
        int number,
        int total,
        string key,
        string label,
        int delayMilliseconds)
    {
        if (delayMilliseconds >= 1_000)
        {
            if (!await DelayWithProgressAsync(
                    generation,
                    delayMilliseconds,
                    "화면 전환 대기",
                    label))
            {
                return false;
            }
        }
        else
        {
            await Task.Delay(delayMilliseconds);
        }

        if (!IsLiveGenerationActive(generation))
        {
            return false;
        }

        ThrowIfEmergencyStopIsDown();
        using var capture = ForegroundWindowCapture.Capture();
        ValidateLiveForeground(capture);
        _keySender.Send(key);
        AddLog($"실전 입력 {number}/{total}: {key} — {label}");
        Publish(
            ValidationSessionState.Running,
            $"실전 입력 {number}/{total}",
            label);
        return true;
    }

    private async Task<bool> DelayWithProgressAsync(
        int generation,
        int milliseconds,
        string status,
        string detail)
    {
        if (milliseconds <= 0)
        {
            return IsLiveGenerationActive(generation);
        }

        var phase = new ActivePhase(
            status,
            detail,
            DateTimeOffset.UtcNow.AddMilliseconds(milliseconds));
        Volatile.Write(ref _activePhase, phase);
        AddLog($"{status}: {detail} ({Math.Ceiling(milliseconds / 1000d):F0}초 대기)");

        try
        {
            var remaining = milliseconds;
            while (remaining > 0)
            {
                if (!IsLiveGenerationActive(generation))
                {
                    return false;
                }

                Publish(ValidationSessionState.Running, status, detail);
                var step = Math.Min(250, remaining);
                await Task.Delay(step);
                remaining -= step;
            }

            return IsLiveGenerationActive(generation);
        }
        finally
        {
            Interlocked.CompareExchange(ref _activePhase, null, phase);
        }
    }

    private async Task<bool> HoldPracticalKeyAsync(
        int generation,
        string key,
        int durationMilliseconds,
        string status,
        string detail)
    {
        var phase = new ActivePhase(
            status,
            detail,
            DateTimeOffset.UtcNow.AddMilliseconds(durationMilliseconds));
        Volatile.Write(ref _activePhase, phase);
        _keySender.KeyDown(key);
        try
        {
            var elapsed = 0;
            while (elapsed < durationMilliseconds)
            {
                var delay = Math.Min(100, durationMilliseconds - elapsed);
                await Task.Delay(delay);
                elapsed += delay;

                if (!IsLiveGenerationActive(generation))
                {
                    return false;
                }

                ThrowIfEmergencyStopIsDown();
            }

            return true;
        }
        finally
        {
            Interlocked.CompareExchange(ref _activePhase, null, phase);
            _keySender.KeyUp(key);
        }
    }

    private async Task<bool> DriveForwardWithSteeringAsync(
        int generation,
        int durationMilliseconds,
        string steeringKey,
        int steeringDurationMilliseconds)
    {
        var steeringEnabled = steeringKey != "None" && steeringDurationMilliseconds > 0;
        var steeringDescription = steeringEnabled
            ? $"{steeringKey} {steeringDurationMilliseconds}ms 조향"
            : "조향 없음";
        var phase = new ActivePhase(
            "플레이어 위치 분리 중",
            $"W 전진 · {steeringDescription}",
            DateTimeOffset.UtcNow.AddMilliseconds(durationMilliseconds));
        Volatile.Write(ref _activePhase, phase);

        var steeringIsDown = false;
        _keySender.KeyDown("W");
        try
        {
            if (steeringEnabled)
            {
                _keySender.KeyDown(steeringKey);
                steeringIsDown = true;
            }
            var elapsed = 0;
            while (elapsed < durationMilliseconds)
            {
                var delay = Math.Min(100, durationMilliseconds - elapsed);
                await Task.Delay(delay);
                elapsed += delay;

                if (steeringIsDown && elapsed >= steeringDurationMilliseconds)
                {
                    _keySender.KeyUp(steeringKey);
                    steeringIsDown = false;
                }

                if (!IsLiveGenerationActive(generation))
                {
                    return false;
                }

                ThrowIfEmergencyStopIsDown();
            }

            return true;
        }
        finally
        {
            if (steeringIsDown)
            {
                _keySender.KeyUp(steeringKey);
            }

            _keySender.KeyUp("W");
            Interlocked.CompareExchange(ref _activePhase, null, phase);
        }
    }

    private async Task ScanClusterVehiclesAsync(
        int generation,
        int screenX,
        int screenY,
        AftermarketMapIconCluster cluster)
    {
        var targets = CreateVehicleClickScan(cluster);
        for (var index = 0; index < targets.Count; index++)
        {
            if (!IsLiveGenerationActive(generation) ||
                Volatile.Read(ref _restartInProgress) != 0 ||
                Snapshot.State is ValidationSessionState.TargetFound or
                    ValidationSessionState.CycleComplete)
            {
                return;
            }

            ThrowIfEmergencyStopIsDown();
            var target = targets[index];
            _mouseSender.MoveTo(screenX + target.X, screenY + target.Y);
            await Task.Delay(Timing.PointerSettleMilliseconds);

            if (!IsLiveGenerationActive(generation))
            {
                return;
            }

            using (var clickCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(clickCapture);
            }

            ThrowIfEmergencyStopIsDown();
            _mouseSender.ClickLeft();
            AddLog(
                $"차량 아이콘 클릭 {index + 1}/{targets.Count}: " +
                $"({target.X},{target.Y})");
            Publish(
                ValidationSessionState.Running,
                $"차량 아이콘 클릭 {index + 1}/{targets.Count}",
                "지도 카드가 나타나면 차량명을 OCR합니다");

            await Task.Delay(Timing.VehicleCardSettleMilliseconds);
            if (!IsLiveGenerationActive(generation))
            {
                return;
            }

            Publish(
                ValidationSessionState.Running,
                "차량명 판독 중",
                $"차량 아이콘 {index + 1}/{targets.Count} OCR");

            AftermarketMapCardScanResult result;
            using (var cardCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(cardCapture);
                result = await Task.Run(() => _cardAnalyzer.Analyze(cardCapture.Image));
            }

            if (!IsLiveGenerationActive(generation))
            {
                return;
            }

            if (result.Recognition?.HasReadableText == true)
            {
                ApplyCard(result);
                if (Snapshot.State is ValidationSessionState.TargetFound or
                    ValidationSessionState.CycleComplete ||
                    Volatile.Read(ref _restartInProgress) != 0)
                {
                    return;
                }
            }
            else
            {
                AddLog(
                    $"클릭 지점 ({target.X},{target.Y})에서 읽을 수 있는 차량 카드 없음 — " +
                    "다음 지점 계속");
            }
        }

        Publish(
            ValidationSessionState.Running,
            $"자동 판독 {_vehicles.Count}/3",
            "중복 또는 가려짐이 남았습니다 — 지도에서 수동 클릭하면 계속 판독합니다");
    }

    private static IReadOnlyList<Point> CreateVehicleClickScan(
        AftermarketMapIconCluster cluster)
    {
        var targets = new List<Point>(cluster.ClickTargets);
        var offset = Math.Max(3, (int)Math.Round(cluster.Bounds.Width * 0.08));

        foreach (var target in cluster.ClickTargets)
        {
            targets.Add(new Point(
                Math.Clamp(target.X - offset, cluster.Bounds.Left, cluster.Bounds.Right - 1),
                target.Y));
            targets.Add(new Point(
                Math.Clamp(target.X + offset, cluster.Bounds.Left, cluster.Bounds.Right - 1),
                target.Y));
        }

        return targets.Distinct().ToArray();
    }

    private bool IsLiveGenerationActive(int generation)
        => Snapshot.State == ValidationSessionState.Running &&
           generation == Volatile.Read(ref _observationGeneration);

    private AutomationTimingSettings Timing => Volatile.Read(ref _timingSettings);

    private void ValidateLiveForeground(CapturedWindow capture)
    {
        if (!string.Equals(capture.Title, _safety.RequiredWindowTitle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FH6가 전경이 아닙니다: {(capture.Title.Length == 0 ? "제목 없음" : capture.Title)}");
        }

        if (!ScreenGeometry.TryCreate(
                capture.Image.Width,
                capture.Image.Height,
                out _,
                _safety.AspectRatioTolerance))
        {
            throw new InvalidOperationException(
                $"지원하지 않는 화면 크기: {capture.Image.Width}×{capture.Image.Height}");
        }
    }

    private void ThrowIfEmergencyStopIsDown()
    {
        if (_keySender.IsDown(_safety.EmergencyStopKey))
        {
            throw new OperationCanceledException(
                $"{_safety.EmergencyStopKey}가 눌린 상태라 다음 입력을 보내지 않았습니다.");
        }
    }

    public void Pause()
    {
        if (Snapshot.State != ValidationSessionState.Running)
        {
            return;
        }

        Interlocked.Increment(ref _observationGeneration);
        _keySender.KeyUp("A");
        _keySender.KeyUp("D");
        _keySender.KeyUp("W");
        Volatile.Write(ref _activePhase, null);
        AddLog("검증 세션 일시정지.");
        Publish(
            ValidationSessionState.Paused,
            "일시정지됨",
            "재개 버튼 또는 F1을 누르세요");
    }

    public void Stop(string reason = "사용자 중지")
    {
        if (Snapshot.State == ValidationSessionState.Stopped)
        {
            return;
        }

        Interlocked.Increment(ref _observationGeneration);
        _keySender.KeyUp("A");
        _keySender.KeyUp("D");
        _keySender.KeyUp("W");
        Volatile.Write(ref _activePhase, null);
        AddLog($"검증 세션 중지: {reason}");
        _stopwatch.Stop();
        Publish(
            ValidationSessionState.Stopped,
            "중지됨",
            reason);
    }

    public async Task TickAsync()
    {
        if (Snapshot.State != ValidationSessionState.Running)
        {
            return;
        }

        var activePhase = Volatile.Read(ref _activePhase);
        if (activePhase is not null)
        {
            Publish(
                ValidationSessionState.Running,
                activePhase.Status,
                activePhase.Detail);
            return;
        }

        if (Volatile.Read(ref _restartInProgress) != 0 ||
            Interlocked.Exchange(ref _tickInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var observationGeneration = Volatile.Read(ref _observationGeneration);
            var observation = await Task.Run(ObserveForeground);
            if (Snapshot.State != ValidationSessionState.Running ||
                observationGeneration != Volatile.Read(ref _observationGeneration))
            {
                return;
            }

            ApplyObservation(observation);
        }
        finally
        {
            Interlocked.Exchange(ref _tickInProgress, 0);
        }
    }

    private ForegroundObservation ObserveForeground()
    {
        try
        {
            using var capture = ForegroundWindowCapture.Capture();
            if (!string.Equals(
                    capture.Title,
                    _safety.RequiredWindowTitle,
                    StringComparison.Ordinal))
            {
                return ForegroundObservation.Waiting(
                    $"전경 창: {(capture.Title.Length == 0 ? "제목 없음" : capture.Title)}");
            }

            if (!ScreenGeometry.TryCreate(capture.Image.Width, capture.Image.Height, out _))
            {
                return ForegroundObservation.Attention(
                    $"지원하지 않는 화면 크기: {capture.Image.Width}×{capture.Image.Height}");
            }

            if (AftermarketMapCardDetector.TryFind(capture.Image, out _))
            {
                return ForegroundObservation.Card(_cardAnalyzer.Analyze(capture.Image));
            }

            var clusters = AftermarketMapIconClusterDetector.Inspect(capture.Image);
            return clusters.HasSingleCluster
                ? ForegroundObservation.IconCluster(
                    clusters.Candidates[0],
                    capture.Image.Width,
                    capture.Image.Height)
                : ForegroundObservation.Waiting("지도 카드 또는 초록 아이콘 군집 대기 중");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ForegroundObservation.Error(exception.Message);
        }
    }

    private void ApplyObservation(ForegroundObservation observation)
    {
        switch (observation.Kind)
        {
            case ForegroundObservationKind.Waiting:
                LogObservationOnce($"waiting:{observation.Message}", observation.Message);
                Publish(
                    ValidationSessionState.Running,
                    "입력 일시정지 — FH6 전경 대기",
                    observation.Message);
                return;

            case ForegroundObservationKind.Attention:
                AddLog($"확인 필요: {observation.Message}");
                Publish(
                    ValidationSessionState.NeedsAttention,
                    "확인 필요",
                    observation.Message);
                return;

            case ForegroundObservationKind.Error:
                RegisterRecognitionRetry(observation.Message);
                return;

            case ForegroundObservationKind.IconCluster:
                _recognitionRetries = 0;
                var cluster = observation.Cluster!;
                LogObservationOnce(
                    "cluster",
                    $"초록 차량 아이콘 군집 감지: " +
                    $"screen={observation.ScreenWidth}x{observation.ScreenHeight}, " +
                    $"bounds={cluster.Bounds}, green={cluster.GreenPixelCount}");
                Publish(
                    ValidationSessionState.Running,
                    "초록 차량 아이콘 3개 감지",
                    "아이콘을 하나씩 수동 선택하면 카드가 자동 판독됩니다");
                return;

            case ForegroundObservationKind.Card:
                _lastObservationLogKey = "card";
                ApplyCard(observation.CardResult!);
                return;
        }
    }

    private void ApplyCard(AftermarketMapCardScanResult result)
    {
        if (result.Recognition is null || !result.Recognition.HasReadableText)
        {
            RegisterRecognitionRetry("차량명 카드가 보이지만 OCR 신뢰도가 부족합니다.");
            return;
        }

        _recognitionRetries = 0;
        var bestAttempt = result.Recognition.PreferredVehicleNameAttempt!;
        var name = bestAttempt.Text.Trim();
        var normalized = TargetTextMatcher.Normalize(name);

        if (string.Equals(normalized, _lastObservedName, StringComparison.Ordinal))
        {
            Publish(
                ValidationSessionState.Running,
                "같은 카드 유지 중",
                "중복 프레임은 실패로 세지 않습니다 — 다른 아이콘 선택 대기");
            return;
        }

        _lastObservedName = normalized;
        if (!_uniqueVehicleNames.Add(normalized))
        {
            _duplicateObservations++;
            AddLog($"중복 차량명 관찰: {name}. 정지하지 않고 다음 선택을 기다립니다.");
            Publish(
                ValidationSessionState.Running,
                "중복 카드 — 재시도 가능",
                "커서 이슈일 수 있으므로 주변 지점 또는 다음 아이콘을 선택하세요");
            return;
        }

        var isTarget = result.TargetMatches.Count > 0;
        _vehicles.Add(new VehicleObservation(
            DateTimeOffset.Now,
            name,
            bestAttempt.BestWordConfidence,
            isTarget));
        AddLog(
            $"차량 판독: {name} / confidence={bestAttempt.BestWordConfidence:F1}" +
            (isTarget ? " / TARGET" : string.Empty));

        if (isTarget)
        {
            _stopwatch.Stop();
            PlayTargetAlert();
            Publish(
                ValidationSessionState.TargetFound,
                "목표 차량 발견",
                result.TargetMatches[0].Target.DisplayName);
            return;
        }

        if (_vehicles.Count >= 3)
        {
            Publish(
                ValidationSessionState.Running,
                "3대 검증 완료 — 목표 없음",
                "현재 언어를 확인한 뒤 반대 언어로 변경해 재시작합니다");

            if (Interlocked.CompareExchange(ref _restartInProgress, 1, 0) == 0)
            {
                var generation = Volatile.Read(ref _observationGeneration);
                _ = RestartAfterClearAsync(generation);
            }

            return;
        }

        Publish(
            ValidationSessionState.Running,
            $"차량 {_vehicles.Count}/3 판독 완료",
            "다음 초록 차량 아이콘을 선택하세요");
    }

    private async Task RestartAfterClearAsync(int generation)
    {
        try
        {
            var pauseMenuSteps = new List<(string Key, string Label, int DelayMs)>
            {
                ("Escape", "차량 카드가 열린 지도 닫기", Timing.GeneralKeyDelayMilliseconds),
                ("Escape", "오픈월드에서 일시정지 메뉴 열기", Timing.GeneralKeyDelayMilliseconds)
            };

            for (var index = 0; index < pauseMenuSteps.Count; index++)
            {
                var step = pauseMenuSteps[index];
                if (!await SendPracticalKeyAfterDelayAsync(
                        generation,
                        index + 1,
                        pauseMenuSteps.Count,
                        step.Key,
                        step.Label,
                        step.DelayMs))
                {
                    return;
                }
            }

            if (!await DelayWithProgressAsync(
                    generation,
                    Timing.PauseMenuSettleMilliseconds,
                    "언어 확인 준비",
                    "일시정지 메뉴 표시 대기"))
            {
                return;
            }

            Publish(
                ValidationSessionState.Running,
                "현재 언어 판별 중",
                "재시작 방향을 정하기 위해 메뉴 문구를 읽습니다");
            PauseMenuLanguageDetection languageDetection;
            using (var languageCapture = ForegroundWindowCapture.Capture())
            {
                ValidateLiveForeground(languageCapture);
                languageDetection = await Task.Run(() =>
                    _languageDetector.Detect(languageCapture.Image));
            }

            if (!IsLiveGenerationActive(generation))
            {
                return;
            }

            if (languageDetection.Language is null)
            {
                throw new InvalidOperationException(
                    "재시작 직전 메뉴에서 현재 언어를 ENG/KOR로 확정하지 못했습니다: " +
                    $"eng={languageDetection.EnglishScore}, kor={languageDetection.KoreanScore}, " +
                    $"ocr='{languageDetection.RecognizedText}'");
            }

            _currentLanguage = languageDetection.Language;
            AddLog(
                $"재시작 직전 언어 판정: {_currentLanguage} " +
                $"(eng={languageDetection.EnglishScore}, kor={languageDetection.KoreanScore})");

            var (fromLabel, toLabel, directionKey, moveCount) = _currentLanguage switch
            {
                GameLanguage.English => ("English US", "한국어", "Up", 4),
                GameLanguage.Korean => ("한국어", "English US", "Down", 6),
                _ => throw new InvalidOperationException(
                    "현재 언어가 확정되지 않아 언어 변경 입력을 보내지 않습니다.")
            };

            AddLog($"목표 차량 없음: {fromLabel} → {toLabel} 언어 변경 재시작 시작.");
            var steps = new List<(string Key, string Label, int DelayMs)>
            {
                ("Down", "Settings 행으로 이동", Timing.GeneralKeyDelayMilliseconds),
                ("Right", "Settings 타일로 이동", Timing.GeneralKeyDelayMilliseconds),
                ("Enter", "Settings 열기", Timing.GeneralKeyDelayMilliseconds),
                ("Up", "Language Select로 이동 1/2", Timing.GeneralKeyDelayMilliseconds),
                ("Up", "Language Select로 이동 2/2", Timing.GeneralKeyDelayMilliseconds),
                ("Enter", "언어 목록 열기", Timing.GeneralKeyDelayMilliseconds)
            };

            for (var move = 1; move <= moveCount; move++)
            {
                steps.Add((
                    directionKey,
                    $"{toLabel}(으)로 이동 {move}/{moveCount}",
                    Timing.RepeatedKeyDelayMilliseconds));
            }

            steps.Add(("Enter", $"{toLabel} 선택", Timing.GeneralKeyDelayMilliseconds));
            steps.Add(("Down", "재시작 확인 Yes 선택", Timing.GeneralKeyDelayMilliseconds));
            steps.Add(("Enter", $"{toLabel} 적용 및 클라이언트 재시작", Timing.GeneralKeyDelayMilliseconds));

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                if (!await SendPracticalKeyAfterDelayAsync(
                        generation,
                        index + 1,
                        steps.Count,
                        step.Key,
                        step.Label,
                        step.DelayMs))
                {
                    return;
                }
            }

            var restartLoading = Timing.RestartLoadingMilliseconds;
            if (!await DelayWithProgressAsync(
                    generation,
                    restartLoading,
                    "클라이언트 재시작 대기 중",
                    $"{fromLabel} → {toLabel} 적용 완료"))
            {
                return;
            }

            if (!await EnsureRestartedGameForegroundAsync(generation))
            {
                return;
            }

            var firstPostRestartDelay = Timing.PostRestartFirstDelayMilliseconds;
            var secondPostRestartDelay = Timing.PostRestartSecondDelayMilliseconds;
            if (!await SendPracticalKeyAfterDelayAsync(
                    generation,
                    1,
                    4,
                    "Enter",
                    "재시작 첫 화면 진행",
                    Timing.GeneralKeyDelayMilliseconds) ||
                !await DelayWithProgressAsync(
                    generation,
                    firstPostRestartDelay,
                    "시작 화면 로딩 중",
                    "재시작 후 1/2 · 두 번째 Enter 입력 대기") ||
                !await SendPracticalKeyAfterDelayAsync(
                    generation,
                    2,
                    4,
                    "Enter",
                    "두 번째 시작 화면 진행",
                    Timing.GeneralKeyDelayMilliseconds) ||
                !await DelayWithProgressAsync(
                    generation,
                    secondPostRestartDelay,
                    "오픈월드 로딩 중",
                    "재시작 후 2/2 · 마지막 Escape 입력 대기") ||
                !await SendPracticalKeyAfterDelayAsync(
                    generation,
                    3,
                    4,
                    "Escape",
                    "오픈월드 입장",
                    Timing.GeneralKeyDelayMilliseconds) ||
                !await SendPracticalKeyAfterDelayAsync(
                    generation,
                    4,
                    4,
                    "M",
                    "오픈월드 안정화 후 다음 회차 전체 지도 열기",
                    Timing.OpenWorldMapDelayMilliseconds))
            {
                return;
            }

            PrepareNextCycle();
            Interlocked.Exchange(ref _restartInProgress, 0);
            AddLog("재시작 후 Enter → 대기 → Enter → 대기 → Escape → M 완료.");
            Publish(
                ValidationSessionState.Running,
                "다음 회차 시작",
                $"{fromLabel} → {toLabel} 변경 완료 · 지도 필터를 다시 적용합니다");
            await RunPracticalCycleAsync(generation);
        }
        catch (OperationCanceledException exception)
        {
            Stop(exception.Message);
        }
        catch (Exception exception)
        {
            AddLog($"언어 변경 재시작 중단: {exception.Message}");
            _stopwatch.Stop();
            Publish(
                ValidationSessionState.NeedsAttention,
                "언어 변경 재시작 중단",
                exception.Message);
        }
    }

    private async Task<bool> EnsureRestartedGameForegroundAsync(int generation)
    {
        const int timeoutMilliseconds = 5_000;
        const int retryMilliseconds = 250;
        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (!IsLiveGenerationActive(generation))
            {
                return false;
            }

            ThrowIfEmergencyStopIsDown();
            Publish(
                ValidationSessionState.Running,
                "재시작 후 FH6 전경 복구 중",
                "시작 화면 창을 찾아 첫 Enter를 준비합니다");

            if (WindowsWindowActivator.TryActivateExactTitle(_safety.RequiredWindowTitle))
            {
                await Task.Delay(retryMilliseconds);
                try
                {
                    using var capture = ForegroundWindowCapture.Capture();
                    ValidateLiveForeground(capture);
                    AddLog($"재시작 후 FH6 전경 복구 완료: {capture.Title}");
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // The game can briefly replace its top-level window while loading.
                }
            }

            await Task.Delay(retryMilliseconds);
        }

        throw new InvalidOperationException(
            $"재시작 후 {_safety.RequiredWindowTitle} 창을 5초 안에 전경으로 복구하지 못했습니다.");
    }

    private static void PlayTargetAlert()
    {
        _ = Task.Run(() =>
        {
            for (var index = 0; index < 3; index++)
            {
                SystemSounds.Exclamation.Play();
                Thread.Sleep(350);
            }
        });
    }

    private void RegisterRecognitionRetry(string reason)
    {
        _recognitionRetries++;
        AddLog(
            $"OCR/캡처 재시도 {_recognitionRetries}/{_safety.MaxRecognitionRetriesPerPoint}: {reason}");

        if (_recognitionRetries >= _safety.MaxRecognitionRetriesPerPoint)
        {
            Publish(
                ValidationSessionState.NeedsAttention,
                "확인 필요 — 자동 정지 아님",
                "커서를 조정한 뒤 재개를 누르세요");
            return;
        }

        Publish(
            ValidationSessionState.Running,
            "판독 재시도 중",
            reason);
    }

    private void ResetCycle()
    {
        _vehicles.Clear();
        _uniqueVehicleNames.Clear();
        _logLines.Clear();
        _lastObservedName = null;
        _lastObservationLogKey = null;
        _recognitionRetries = 0;
        _duplicateObservations = 0;
        _currentLanguage = null;
        Volatile.Write(ref _activePhase, null);
        Interlocked.Exchange(ref _restartInProgress, 0);
    }

    private void PrepareNextCycle()
    {
        _vehicles.Clear();
        _uniqueVehicleNames.Clear();
        _lastObservedName = null;
        _lastObservationLogKey = null;
        _recognitionRetries = 0;
        _duplicateObservations = 0;
        _currentLanguage = null;
    }

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logLines.Add(line);
        if (_logLines.Count > 300)
        {
            _logLines.RemoveAt(0);
        }

        if (_logPath is not null)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    private void LogObservationOnce(string key, string message)
    {
        if (string.Equals(_lastObservationLogKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _lastObservationLogKey = key;
        AddLog(message);
    }

    private void Publish(
        ValidationSessionState state,
        string status,
        string currentStep)
    {
        Snapshot = CreateSnapshot(state, status, currentStep);
        SnapshotChanged?.Invoke(Snapshot);
    }

    private ValidationSessionSnapshot CreateSnapshot(
        ValidationSessionState state,
        string status,
        string currentStep)
        => new(
            state,
            status,
            currentStep,
            _stopwatch.Elapsed,
            _recognitionRetries,
            _duplicateObservations,
            _vehicles.ToArray(),
            _logLines.ToArray(),
            GetRemainingSeconds(),
            _safety.PracticalStartEnabled);

    private int? GetRemainingSeconds()
    {
        var phase = Volatile.Read(ref _activePhase);
        if (phase is null)
        {
            return null;
        }

        return Math.Max(
            0,
            (int)Math.Ceiling((phase.Deadline - DateTimeOffset.UtcNow).TotalSeconds));
    }

    private enum ForegroundObservationKind
    {
        Waiting,
        Attention,
        Error,
        IconCluster,
        Card
    }

    private sealed record ForegroundObservation(
        ForegroundObservationKind Kind,
        string Message,
        AftermarketMapIconCluster? Cluster = null,
        AftermarketMapCardScanResult? CardResult = null,
        int ScreenWidth = 0,
        int ScreenHeight = 0)
    {
        public static ForegroundObservation Waiting(string message)
            => new(ForegroundObservationKind.Waiting, message);

        public static ForegroundObservation Attention(string message)
            => new(ForegroundObservationKind.Attention, message);

        public static ForegroundObservation Error(string message)
            => new(ForegroundObservationKind.Error, message);

        public static ForegroundObservation IconCluster(
            AftermarketMapIconCluster cluster,
            int screenWidth,
            int screenHeight)
            => new(
                ForegroundObservationKind.IconCluster,
                string.Empty,
                cluster,
                ScreenWidth: screenWidth,
                ScreenHeight: screenHeight);

        public static ForegroundObservation Card(AftermarketMapCardScanResult result)
            => new(ForegroundObservationKind.Card, string.Empty, CardResult: result);
    }
}
