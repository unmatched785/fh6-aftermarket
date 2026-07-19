using System.Diagnostics;
using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Domain;
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
    bool AutomationEnabled);

public sealed class ValidationSessionController
{
    private readonly SafetySettings _safety;
    private readonly AftermarketMapCardAnalyzer _cardAnalyzer;
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

    public ValidationSessionController(
        SafetySettings safety,
        AftermarketMapCardAnalyzer cardAnalyzer,
        string logDirectory)
    {
        _safety = safety;
        _cardAnalyzer = cardAnalyzer;
        _logDirectory = logDirectory;
        Snapshot = CreateSnapshot(
            ValidationSessionState.Stopped,
            "준비됨 — 검증 모드",
            "F1 또는 시작 버튼을 누르세요");
    }

    public event Action<ValidationSessionSnapshot>? SnapshotChanged;

    public ValidationSessionSnapshot Snapshot { get; private set; }

    public void StartOrResume()
    {
        if (Snapshot.State == ValidationSessionState.Running)
        {
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

    public void Pause()
    {
        if (Snapshot.State != ValidationSessionState.Running)
        {
            return;
        }

        Interlocked.Increment(ref _observationGeneration);
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
        AddLog($"검증 세션 중지: {reason}");
        _stopwatch.Stop();
        Publish(
            ValidationSessionState.Stopped,
            "중지됨",
            reason);
    }

    public async Task TickAsync()
    {
        if (Snapshot.State != ValidationSessionState.Running ||
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
            Publish(
                ValidationSessionState.TargetFound,
                "목표 차량 발견",
                result.TargetMatches[0].Target.DisplayName);
            return;
        }

        if (_vehicles.Count >= 3)
        {
            _stopwatch.Stop();
            Publish(
                ValidationSessionState.CycleComplete,
                "3대 검증 완료 — 목표 없음",
                "검증 모드에서는 재시작하지 않습니다");
            return;
        }

        Publish(
            ValidationSessionState.Running,
            $"차량 {_vehicles.Count}/3 판독 완료",
            "다음 초록 차량 아이콘을 선택하세요");
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
            _safety.AutomationEnabled);

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
