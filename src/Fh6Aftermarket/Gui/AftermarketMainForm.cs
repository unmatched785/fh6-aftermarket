using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Gui;

public sealed class AftermarketMainForm : Form
{
    private const int WmHotKey = 0x0312;
    private const int StartHotKeyId = 1;
    private const int StopHotKeyId = 2;
    private const uint F1VirtualKey = 0x70;
    private const uint F2VirtualKey = 0x71;
    private const int PageCount = 3;

    private sealed record ShadowLabel(Label Front, Label Shadow);

    private readonly ValidationSessionController _controller;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _emergencyStopTimer = new() { Interval = 50 };
    private readonly Panel[] _pages = new Panel[PageCount];
    private readonly Label[] _tabs = new Label[PageCount];
    private readonly Label[] _tabShadows = new Label[PageCount];
    private readonly ShadowLabel[] _vehicleLabels = new ShadowLabel[3];
    private readonly TextBox _inputDelay = new();
    private readonly TextBox _transitionDelay = new();
    private readonly TextBox _fastTravelLoading = new();
    private readonly TextBox _forwardDuration = new();
    private readonly ComboBox _steeringKey = new();
    private readonly TextBox _steeringDuration = new();
    private readonly TextBox _restartLoading = new();
    private readonly TextBox _postRestartFirstDelay = new();
    private readonly TextBox _postRestartSecondDelay = new();
    private readonly TextBox _openWorldMapDelay = new();
    private readonly RichTextBox _logBox = new();
    private float _dpiScale = 1F;
    private string _baseFont = "Malgun Gothic";
    private Font _activeTabFont = null!;
    private Font _inactiveTabFont = null!;
    private Label _activeTabIndicator = null!;
    private Label _pinLabel = null!;
    private ShadowLabel _modeHint = null!;
    private ShadowLabel _scanSummary = null!;
    private ShadowLabel _elapsedSummary = null!;
    private ShadowLabel _retrySummary = null!;
    private ShadowLabel _status = null!;
    private ShadowLabel _detail = null!;
    private ShadowLabel _remaining = null!;
    private Label _startAction = null!;
    private Label _pauseAction = null!;
    private Label _stopAction = null!;
    private int _renderedLogCount;
    private string _renderedLastLog = string.Empty;
    private bool _f1WasDown;
    private bool _f2WasDown;
    private bool _globalHotKeysRegistered = true;
    private int _currentPage;

    public AftermarketMainForm(ValidationSessionController controller)
    {
        _controller = controller;
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
        {
            _dpiScale = Math.Max(1F, graphics.DpiX / 96F);
        }

        if (FontFamily.Families.Any(family =>
                family.Name.Equals("NanumGothic", StringComparison.OrdinalIgnoreCase)))
        {
            _baseFont = "NanumGothic";
        }

        InitializeCompactLayout();
        RestoreTimingSettings();
        WireTimingControls();
        _controller.SnapshotChanged += RenderSnapshot;
        _timer.Tick += async (_, _) => await _controller.TickAsync();
        _emergencyStopTimer.Tick += (_, _) => PollSessionHotKeys();
        _timer.Start();
        _emergencyStopTimer.Start();
        RenderSnapshot(_controller.Snapshot);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var startRegistered = RegisterHotKey(Handle, StartHotKeyId, 0, F1VirtualKey);
        var stopRegistered = RegisterHotKey(Handle, StopHotKeyId, 0, F2VirtualKey);
        _globalHotKeysRegistered = startRegistered && stopRegistered;
        RenderModeHint(_controller.Snapshot.PracticalInputEnabled);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        _emergencyStopTimer.Stop();
        _controller.SnapshotChanged -= RenderSnapshot;
        _ = UnregisterHotKey(Handle, StartHotKeyId);
        _ = UnregisterHotKey(Handle, StopHotKeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeTabFont?.Dispose();
            _inactiveTabFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotKey)
        {
            var id = message.WParam.ToInt32();
            if (id == StartHotKeyId)
            {
                StartOrResumeFromUi();
                return;
            }

            if (id == StopHotKeyId)
            {
                _controller.Stop("F2 긴급 중지");
                return;
            }
        }

        base.WndProc(ref message);
    }

    private int Px(int value) => (int)Math.Round(value * _dpiScale);

    private void InitializeCompactLayout()
    {
        Text = "FH6 애프터마켓";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Px(500), Px(240));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(10, 14, 19);
        BackgroundImageLayout = ImageLayout.Stretch;
        DoubleBuffered = true;
        Font = new Font(_baseFont, 9F);

        ApplyBackground();
        _activeTabFont = new Font(_baseFont, 11.5F, FontStyle.Bold);
        _inactiveTabFont = new Font(_baseFont, 9.5F, FontStyle.Bold);

        for (var index = 0; index < PageCount; index++)
        {
            _pages[index] = new Panel
            {
                Size = ClientSize,
                Location = Point.Empty,
                BackColor = Color.Transparent,
                Visible = false
            };
            Controls.Add(_pages[index]);
        }

        BuildRunPage(_pages[0]);
        BuildTimingPage(_pages[1]);
        BuildLogPage(_pages[2]);
        BuildPersistentMonitor();
        BuildTabsAndPin();
        SwitchTab(0);
    }

    private void ApplyBackground()
    {
        var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Fh6Aftermarket.Gui.aftermarket.bg.png");
        if (stream is not null)
        {
            using var image = Image.FromStream(stream);
            graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
        }
        else
        {
            using var gradient = new LinearGradientBrush(
                new Rectangle(Point.Empty, bitmap.Size),
                Color.FromArgb(22, 43, 60),
                Color.FromArgb(8, 12, 18),
                20F);
            graphics.FillRectangle(gradient, new Rectangle(Point.Empty, bitmap.Size));
        }

        using var overlay = new SolidBrush(Color.FromArgb(115, 0, 0, 0));
        graphics.FillRectangle(
            overlay,
            new Rectangle(Px(10), Px(10), bitmap.Width - Px(20), bitmap.Height - Px(20)));
        BackgroundImage = bitmap;
    }

    private void BuildRunPage(Control page)
    {
        _modeHint = CreateShadowLabel(
            page,
            "시작: KOR/ENG 오픈월드에서 F1 · 값 수정은 상단 타이밍 설정",
            20,
            55,
            8F,
            FontStyle.Bold,
            Color.FromArgb(235, 215, 140),
            460);
        _scanSummary = CreateShadowLabel(
            page,
            "판독 0/3",
            20,
            79,
            10F,
            FontStyle.Bold,
            Color.White,
            105);
        _elapsedSummary = CreateShadowLabel(
            page,
            "경과 00:00",
            145,
            79,
            10F,
            FontStyle.Bold,
            Color.White,
            105);
        _retrySummary = CreateShadowLabel(
            page,
            "재시도 0 · 중복 0",
            270,
            79,
            9F,
            FontStyle.Bold,
            Color.White,
            210);

        for (var index = 0; index < _vehicleLabels.Length; index++)
        {
            _vehicleLabels[index] = CreateShadowLabel(
                page,
                $"차량 {index + 1}: —",
                20,
                102 + (index * 18),
                8F,
                FontStyle.Bold,
                Color.FromArgb(220, 226, 233),
                460);
        }
    }

    private void BuildTimingPage(Control page)
    {
        CreateShadowLabel(
            page,
            "PC 환경에 맞춰 조정 · 재시작 후 1/2와 2/2는 별도 적용",
            20,
            53,
            8F,
            FontStyle.Bold,
            Color.FromArgb(235, 215, 140),
            460);

        BuildTimingField(page, "입력 간격", _inputDelay, "ms", 10, 75);
        BuildTimingField(page, "화면 전환", _transitionDelay, "ms", 108, 75);
        BuildTimingField(page, "빠른 이동", _fastTravelLoading, "초", 206, 75);
        BuildTimingField(page, "전진 시간", _forwardDuration, "초", 304, 75);
        BuildSteeringKeyField(page, 402, 75);
        BuildTimingField(page, "조향 시간", _steeringDuration, "ms", 10, 116);
        BuildTimingField(page, "재시작 로딩", _restartLoading, "초", 108, 116);
        BuildTimingField(page, "재시작 후 1/2", _postRestartFirstDelay, "초", 206, 116);
        BuildTimingField(page, "재시작 후 2/2", _postRestartSecondDelay, "초", 304, 116);
        BuildTimingField(page, "오픈월드→M", _openWorldMapDelay, "초", 402, 116);
    }

    private void BuildTimingField(
        Control page,
        string label,
        TextBox textBox,
        string unit,
        int x,
        int y)
    {
        CreateShadowLabel(
            page,
            label,
            x,
            y,
            8.5F,
            FontStyle.Bold,
            Color.White,
            96);
        ConfigureTimingTextBox(textBox);
        textBox.Location = new Point(Px(x), Px(y + 19));
        page.Controls.Add(textBox);
        CreateShadowLabel(
            page,
            unit,
            x + 62,
            y + 22,
            8F,
            FontStyle.Regular,
            Color.White,
            40);
    }

    private void BuildSteeringKeyField(Control page, int x, int y)
    {
        CreateShadowLabel(
            page,
            "조향 키",
            x,
            y,
            8.5F,
            FontStyle.Bold,
            Color.White,
            88);
        _steeringKey.DropDownStyle = ComboBoxStyle.DropDownList;
        _steeringKey.Items.AddRange(["D", "A", "사용 안 함"]);
        _steeringKey.Size = new Size(Px(78), Px(22));
        _steeringKey.Location = new Point(Px(x), Px(y + 19));
        _steeringKey.Font = new Font(_baseFont, 8F, FontStyle.Regular);
        page.Controls.Add(_steeringKey);
    }

    private void BuildLogPage(Control page)
    {
        CreateShadowLabel(
            page,
            "최근 로그 · 전체 기록은 실행 폴더의 logs에 저장",
            20,
            53,
            8F,
            FontStyle.Bold,
            Color.FromArgb(235, 215, 140),
            460);

        _logBox.Location = new Point(Px(20), Px(73));
        _logBox.Size = new Size(Px(460), Px(80));
        _logBox.ReadOnly = true;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.FromArgb(22, 28, 35);
        _logBox.ForeColor = Color.FromArgb(220, 226, 233);
        _logBox.Font = new Font("Cascadia Mono", 7.5F);
        _logBox.WordWrap = false;
        _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        page.Controls.Add(_logBox);
    }

    private void BuildPersistentMonitor()
    {
        CreateShadowLabel(
            this,
            "실시간 모니터링 상태창",
            20,
            160,
            10F,
            FontStyle.Bold,
            Color.White,
            230);
        CreateShadowLabel(
            this,
            "문제시 F2 긴급정지",
            300,
            162,
            8F,
            FontStyle.Bold,
            Color.FromArgb(235, 215, 140),
            180);
        _status = CreateShadowLabel(
            this,
            "상태: 대기 중",
            20,
            185,
            10F,
            FontStyle.Bold,
            Color.White,
            330);
        _remaining = CreateShadowLabel(
            this,
            "남은 시간: —",
            360,
            185,
            9F,
            FontStyle.Bold,
            Color.FromArgb(235, 215, 140),
            120);
        _detail = CreateShadowLabel(
            this,
            "오픈월드에서 [F1] 키를 눌러주세요.",
            20,
            210,
            9F,
            FontStyle.Regular,
            Color.White,
            280);

        _startAction = CreateActionLabel("[F1]시작", 305, Color.White, StartOrResumeFromUi);
        _pauseAction = CreateActionLabel("일시정지", 366, Color.FromArgb(255, 200, 100), _controller.Pause);
        _stopAction = CreateActionLabel("[F2]정지", 421, Color.White, () => _controller.Stop());
    }

    private Label CreateActionLabel(string text, int x, Color color, Action action)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(Px(x), Px(210)),
            AutoSize = true,
            Font = new Font(_baseFont, 8.5F, FontStyle.Bold),
            ForeColor = color,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        label.Click += (_, _) => action();
        Controls.Add(label);
        label.BringToFront();
        return label;
    }

    private void BuildTabsAndPin()
    {
        var separator = new Label
        {
            Height = Math.Max(1, Px(1)),
            Width = ClientSize.Width - Px(40),
            BackColor = Color.FromArgb(120, 255, 255, 255),
            Location = new Point(Px(20), Px(40))
        };
        Controls.Add(separator);

        _activeTabIndicator = new Label
        {
            Height = Math.Max(2, Px(2)),
            BackColor = Color.White
        };
        Controls.Add(_activeTabIndicator);

        var names = new[] { "실행", "타이밍 설정", "판독·로그" };
        var positions = new[] { 12, 64, 158 };
        var widths = new[] { 52, 94, 96 };
        for (var index = 0; index < PageCount; index++)
        {
            _tabShadows[index] = new Label
            {
                Text = names[index],
                Location = new Point(Px(positions[index] + 1), Px(9)),
                Size = new Size(Px(widths[index]), Px(30)),
                Font = _inactiveTabFont,
                ForeColor = Color.Black,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            Controls.Add(_tabShadows[index]);

            _tabs[index] = new Label
            {
                Text = names[index],
                Location = new Point(Px(positions[index]), Px(8)),
                Size = new Size(Px(widths[index]), Px(30)),
                Font = _inactiveTabFont,
                ForeColor = Color.FromArgb(205, 205, 205),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            WireTabInteraction(_tabs[index], index);
            WireTabInteraction(_tabShadows[index], index);
            Controls.Add(_tabs[index]);
        }

        _pinLabel = new Label
        {
            Text = "📌 항상 위",
            Location = new Point(Px(410), Px(15)),
            Font = new Font(_baseFont, 9F, FontStyle.Bold),
            ForeColor = Color.Gray,
            BackColor = Color.Transparent,
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        _pinLabel.Click += (_, _) =>
        {
            TopMost = !TopMost;
            _pinLabel.ForeColor = TopMost ? Color.FromArgb(235, 80, 80) : Color.Gray;
        };
        Controls.Add(_pinLabel);

        separator.BringToFront();
        _activeTabIndicator.BringToFront();
        _pinLabel.BringToFront();
    }

    private ShadowLabel CreateShadowLabel(
        Control parent,
        string text,
        int x,
        int y,
        float fontSize,
        FontStyle style,
        Color color,
        int width)
    {
        var font = new Font(_baseFont, fontSize, style);
        var shadow = new Label
        {
            Text = text,
            Location = new Point(Px(x) + Math.Max(1, Px(1)), Px(y) + Math.Max(1, Px(1))),
            Size = new Size(Px(width), Px(22)),
            Font = font,
            ForeColor = Color.Black,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var front = new Label
        {
            Text = text,
            Location = new Point(Px(x), Px(y)),
            Size = new Size(Px(width), Px(22)),
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        parent.Controls.Add(shadow);
        parent.Controls.Add(front);
        front.BringToFront();
        return new ShadowLabel(front, shadow);
    }

    private void SwitchTab(int index)
    {
        _currentPage = index;
        for (var pageIndex = 0; pageIndex < PageCount; pageIndex++)
        {
            var active = pageIndex == index;
            _pages[pageIndex].Visible = active;
            _tabs[pageIndex].Font = active ? _activeTabFont : _inactiveTabFont;
            _tabs[pageIndex].ForeColor = active ? Color.White : Color.FromArgb(205, 205, 205);
            _tabShadows[pageIndex].Font = active ? _activeTabFont : _inactiveTabFont;
            _tabShadows[pageIndex].BringToFront();
            _tabs[pageIndex].BringToFront();

            if (active)
            {
                _activeTabIndicator.Width = TextRenderer.MeasureText(
                    _tabs[pageIndex].Text,
                    _tabs[pageIndex].Font).Width;
                _activeTabIndicator.Location = new Point(_tabs[pageIndex].Location.X, Px(38));
            }
        }

        _activeTabIndicator.BringToFront();
        _pinLabel.BringToFront();
    }

    private void WireTabInteraction(Control control, int index)
    {
        control.Click += (_, _) => SwitchTab(index);
        control.MouseEnter += (_, _) => _tabs[index].ForeColor = Color.White;
        control.MouseDown += (_, _) => _tabs[index].ForeColor = Color.FromArgb(255, 215, 80);
        control.MouseUp += (_, _) => _tabs[index].ForeColor = Color.White;
        control.MouseLeave += (_, _) =>
        {
            _tabs[index].ForeColor = index == _currentPage
                ? Color.White
                : Color.FromArgb(205, 205, 205);
        };
    }

    private void ConfigureTimingTextBox(TextBox textBox)
    {
        textBox.Size = new Size(Px(55), Px(22));
        textBox.Font = new Font(_baseFont, 9F, FontStyle.Regular);
        textBox.TextAlign = HorizontalAlignment.Center;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.White;
        textBox.ForeColor = Color.Black;
        textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        };
    }

    private void WireTimingControls()
    {
        foreach (var textBox in TimingTextBoxes())
        {
            textBox.Leave += (_, _) =>
            {
                if (!TryApplyTimingSettings(showError: false))
                {
                    RestoreTimingSettings();
                }
            };
        }

        _steeringKey.SelectionChangeCommitted += (_, _) =>
        {
            _ = TryApplyTimingSettings(showError: false);
        };
    }

    private IEnumerable<TextBox> TimingTextBoxes()
    {
        yield return _inputDelay;
        yield return _transitionDelay;
        yield return _fastTravelLoading;
        yield return _forwardDuration;
        yield return _steeringDuration;
        yield return _restartLoading;
        yield return _postRestartFirstDelay;
        yield return _postRestartSecondDelay;
        yield return _openWorldMapDelay;
    }

    private void RestoreTimingSettings()
    {
        var timing = _controller.TimingSettings;
        _inputDelay.Text = timing.InputDelayMilliseconds.ToString();
        _transitionDelay.Text = timing.TransitionDelayMilliseconds.ToString();
        _fastTravelLoading.Text = (timing.FastTravelLoadingMilliseconds / 1_000).ToString();
        _forwardDuration.Text = (timing.ForwardDurationMilliseconds / 1_000).ToString();
        _steeringKey.SelectedItem = timing.SteeringKey == "None" ? "사용 안 함" : timing.SteeringKey;
        _steeringDuration.Text = timing.SteeringDurationMilliseconds.ToString();
        _restartLoading.Text = (timing.RestartLoadingMilliseconds / 1_000).ToString();
        _postRestartFirstDelay.Text = (timing.PostRestartFirstDelayMilliseconds / 1_000).ToString();
        _postRestartSecondDelay.Text = (timing.PostRestartSecondDelayMilliseconds / 1_000).ToString();
        _openWorldMapDelay.Text = (timing.OpenWorldMapDelayMilliseconds / 1_000).ToString();
        foreach (var textBox in TimingTextBoxes())
        {
            textBox.BackColor = Color.White;
        }
    }

    private bool TryApplyTimingSettings(bool showError)
    {
        var valid =
            TryRead(_inputDelay, 50, 5_000, out var inputDelay) &
            TryRead(_transitionDelay, 250, 15_000, out var transitionDelay) &
            TryRead(_fastTravelLoading, 0, 300, out var fastTravelLoading) &
            TryRead(_forwardDuration, 1, 60, out var forwardDuration) &
            TryRead(_steeringDuration, 0, 5_000, out var steeringDuration) &
            TryRead(_restartLoading, 0, 600, out var restartLoading) &
            TryRead(_postRestartFirstDelay, 0, 120, out var firstPostRestartDelay) &
            TryRead(_postRestartSecondDelay, 0, 120, out var secondPostRestartDelay) &
            TryRead(_openWorldMapDelay, 0, 120, out var openWorldMapDelay);

        if (!valid)
        {
            if (showError)
            {
                SwitchTab(1);
                SetShadowText(_status, "상태: 타이밍 값 확인 필요");
                SetShadowText(_detail, "붉게 표시된 입력값의 범위를 확인하세요.");
            }

            return false;
        }

        _controller.UpdateTimingSettings(new AutomationTimingSettings
        {
            InputDelayMilliseconds = inputDelay,
            TransitionDelayMilliseconds = transitionDelay,
            FastTravelLoadingMilliseconds = fastTravelLoading * 1_000,
            ForwardDurationMilliseconds = forwardDuration * 1_000,
            SteeringKey = _steeringKey.SelectedItem?.ToString() == "사용 안 함" ? "None" :
                _steeringKey.SelectedItem?.ToString() ?? "D",
            SteeringDurationMilliseconds = steeringDuration,
            RestartLoadingMilliseconds = restartLoading * 1_000,
            PostRestartFirstDelayMilliseconds = firstPostRestartDelay * 1_000,
            PostRestartSecondDelayMilliseconds = secondPostRestartDelay * 1_000,
            OpenWorldMapDelayMilliseconds = openWorldMapDelay * 1_000
        });
        return true;
    }

    private static bool TryRead(TextBox textBox, int minimum, int maximum, out int value)
    {
        var valid = int.TryParse(textBox.Text, out value) && value >= minimum && value <= maximum;
        textBox.BackColor = valid ? Color.White : Color.FromArgb(255, 190, 190);
        return valid;
    }

    private void StartOrResumeFromUi()
    {
        if (!TryApplyTimingSettings(showError: true))
        {
            return;
        }

        _controller.StartOrResume();
    }

    private void RenderSnapshot(ValidationSessionSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RenderSnapshot(snapshot));
            return;
        }

        SetShadowText(_status, $"상태: {snapshot.Status}");
        _status.Front.ForeColor = StatusColor(snapshot.State);
        SetShadowText(_detail, snapshot.CurrentStep);
        SetShadowText(
            _remaining,
            snapshot.RemainingSeconds is int seconds
                ? $"남은 시간: {seconds}초"
                : "남은 시간: —");
        _remaining.Front.ForeColor = snapshot.RemainingSeconds is null
            ? Color.FromArgb(205, 205, 205)
            : Color.FromArgb(255, 215, 80);

        SetShadowText(_scanSummary, $"판독 {snapshot.Vehicles.Count}/3");
        SetShadowText(_elapsedSummary, $"경과 {snapshot.Elapsed:mm\\:ss}");
        SetShadowText(
            _retrySummary,
            $"재시도 {snapshot.RecognitionRetries} · 중복 {snapshot.DuplicateObservations}");

        for (var index = 0; index < _vehicleLabels.Length; index++)
        {
            if (index < snapshot.Vehicles.Count)
            {
                var vehicle = snapshot.Vehicles[index];
                SetShadowText(
                    _vehicleLabels[index],
                    $"차량 {index + 1}: {vehicle.Name} · {(vehicle.IsTarget ? "목표" : "일반")}");
                _vehicleLabels[index].Front.ForeColor = vehicle.IsTarget
                    ? Color.FromArgb(80, 220, 255)
                    : Color.FromArgb(220, 226, 233);
            }
            else
            {
                SetShadowText(_vehicleLabels[index], $"차량 {index + 1}: —");
                _vehicleLabels[index].Front.ForeColor = Color.FromArgb(220, 226, 233);
            }
        }

        var lastLog = snapshot.LogLines.Count > 0 ? snapshot.LogLines[^1] : string.Empty;
        if (_renderedLogCount != snapshot.LogLines.Count ||
            !string.Equals(_renderedLastLog, lastLog, StringComparison.Ordinal))
        {
            _logBox.Lines = snapshot.LogLines.ToArray();
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
            _renderedLogCount = snapshot.LogLines.Count;
            _renderedLastLog = lastLog;
        }

        _startAction.ForeColor = snapshot.State == ValidationSessionState.Running
            ? Color.Gray
            : Color.White;
        _pauseAction.ForeColor = snapshot.State == ValidationSessionState.Running
            ? Color.FromArgb(255, 200, 100)
            : Color.Gray;
        _stopAction.ForeColor = snapshot.State == ValidationSessionState.Stopped
            ? Color.Gray
            : Color.White;
        RenderModeHint(snapshot.PracticalInputEnabled);
    }

    private void RenderModeHint(bool practicalInputEnabled)
    {
        if (_modeHint is null)
        {
            return;
        }

        var inputMode = practicalInputEnabled
            ? "KOR/ENG 오픈월드 정차 상태"
            : "검증 모드 · 게임 입력 없음";
        var hotKeyMode = _globalHotKeysRegistered ? string.Empty : " · 직접 키 감시";
        SetShadowText(
            _modeHint,
            $"시작: {inputMode}에서 F1{hotKeyMode} · 설정은 상단 타이밍");
    }

    private static void SetShadowText(ShadowLabel label, string text)
    {
        label.Front.Text = text;
        label.Shadow.Text = text;
    }

    private static Color StatusColor(ValidationSessionState state)
        => state switch
        {
            ValidationSessionState.Running => Color.FromArgb(105, 235, 175),
            ValidationSessionState.Paused => Color.FromArgb(255, 215, 80),
            ValidationSessionState.NeedsAttention => Color.FromArgb(255, 175, 70),
            ValidationSessionState.TargetFound => Color.FromArgb(80, 220, 255),
            ValidationSessionState.CycleComplete => Color.FromArgb(130, 210, 255),
            _ => Color.White
        };

    private void PollSessionHotKeys()
    {
        var f1IsDown = (GetAsyncKeyState((int)F1VirtualKey) & 0x8000) != 0;
        if (!_f1WasDown && f1IsDown)
        {
            StartOrResumeFromUi();
        }

        _f1WasDown = f1IsDown;

        var f2IsDown = (GetAsyncKeyState((int)F2VirtualKey) & 0x8000) != 0;
        if (!_f2WasDown && f2IsDown)
        {
            _controller.Stop("F2 긴급 중지");
        }

        _f2WasDown = f2IsDown;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
