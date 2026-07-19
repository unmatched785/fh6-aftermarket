using System.Drawing;
using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Gui;

public sealed class AftermarketMainForm : Form
{
    private const int WmHotKey = 0x0312;
    private const int StartHotKeyId = 1;
    private const int StopHotKeyId = 2;
    private const uint F1VirtualKey = 0x70;
    private const uint F2VirtualKey = 0x71;

    private readonly ValidationSessionController _controller;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 600 };
    private readonly System.Windows.Forms.Timer _emergencyStopTimer = new() { Interval = 50 };
    private readonly Label _statusLabel = new();
    private readonly Label _stepLabel = new();
    private readonly Label _elapsedLabel = new();
    private readonly Label _retryLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _stopButton = new();
    private readonly DataGridView _vehicleGrid = new();
    private readonly RichTextBox _logBox = new();
    private int _renderedLogCount;
    private bool _f1WasDown;
    private bool _f2WasDown;

    public AftermarketMainForm(ValidationSessionController controller)
    {
        _controller = controller;
        Text = "FH6 Aftermarket Validator";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 720);
        Size = new Size(1100, 800);
        BackColor = Color.FromArgb(18, 22, 28);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        BuildLayout();
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
        if (!startRegistered || !stopRegistered)
        {
            _modeLabel.Text = "검증 모드 · F1/F2 직접 감시 · 게임 입력 없음";
            _modeLabel.ForeColor = Color.FromArgb(130, 200, 255);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        _emergencyStopTimer.Stop();
        _ = UnregisterHotKey(Handle, StartHotKeyId);
        _ = UnregisterHotKey(Handle, StopHotKeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotKey)
        {
            var id = message.WParam.ToInt32();
            if (id == StartHotKeyId)
            {
                _controller.StartOrResume();
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

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "FH6 AFTERMARKET",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 4)
        };
        _modeLabel.Text = "실전 1회 · 영어 오픈월드 시작 · F1 실행 / F2 중지";
        _modeLabel.AutoSize = false;
        _modeLabel.Size = new Size(980, 30);
        _modeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _modeLabel.Font = new Font("Malgun Gothic", 9F);
        _modeLabel.UseCompatibleTextRendering = true;
        _modeLabel.ForeColor = Color.FromArgb(130, 200, 255);
        _modeLabel.Location = new Point(3, 56);
        header.Controls.Add(title);
        header.Controls.Add(_modeLabel);
        root.Controls.Add(header, 0, 0);

        var statusPanel = CreateCard();
        _statusLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(18, 12);
        _stepLabel.AutoSize = false;
        _stepLabel.Location = new Point(20, 48);
        _stepLabel.Size = new Size(980, 36);
        _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
        _stepLabel.Font = new Font("Malgun Gothic", 9.5F);
        _stepLabel.UseCompatibleTextRendering = true;
        _stepLabel.ForeColor = Color.FromArgb(190, 199, 210);
        statusPanel.Controls.Add(_statusLabel);
        statusPanel.Controls.Add(_stepLabel);
        root.Controls.Add(statusPanel, 0, 1);

        var controlsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            Padding = new Padding(0, 10, 0, 8)
        };
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ConfigureButton(_startButton, "실전 1회 시작  F1", Color.FromArgb(20, 145, 92));
        ConfigureButton(_pauseButton, "일시정지", Color.FromArgb(190, 125, 25));
        ConfigureButton(_stopButton, "중지  F2", Color.FromArgb(188, 58, 65));
        _startButton.Click += (_, _) => _controller.StartOrResume();
        _pauseButton.Click += (_, _) => _controller.Pause();
        _stopButton.Click += (_, _) => _controller.Stop();
        controlsPanel.Controls.Add(_startButton, 0, 0);
        controlsPanel.Controls.Add(_pauseButton, 1, 0);
        controlsPanel.Controls.Add(_stopButton, 2, 0);
        _elapsedLabel.Dock = DockStyle.Fill;
        _elapsedLabel.TextAlign = ContentAlignment.MiddleRight;
        _retryLabel.Dock = DockStyle.Fill;
        _retryLabel.TextAlign = ContentAlignment.MiddleRight;
        controlsPanel.Controls.Add(_elapsedLabel, 3, 0);
        controlsPanel.Controls.Add(_retryLabel, 4, 0);
        root.Controls.Add(controlsPanel, 0, 2);

        ConfigureVehicleGrid();
        root.Controls.Add(_vehicleGrid, 0, 3);

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.FromArgb(11, 14, 18);
        _logBox.ForeColor = Color.FromArgb(205, 213, 222);
        _logBox.Font = new Font("Cascadia Mono", 9F);
        root.Controls.Add(_logBox, 0, 4);
    }

    private void ConfigureVehicleGrid()
    {
        _vehicleGrid.Dock = DockStyle.Fill;
        _vehicleGrid.BackgroundColor = Color.FromArgb(24, 29, 36);
        _vehicleGrid.BorderStyle = BorderStyle.None;
        _vehicleGrid.ReadOnly = true;
        _vehicleGrid.AllowUserToAddRows = false;
        _vehicleGrid.AllowUserToDeleteRows = false;
        _vehicleGrid.AllowUserToResizeRows = false;
        _vehicleGrid.RowHeadersVisible = false;
        _vehicleGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vehicleGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _vehicleGrid.EnableHeadersVisualStyles = false;
        _vehicleGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 43, 52);
        _vehicleGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _vehicleGrid.DefaultCellStyle.BackColor = Color.FromArgb(24, 29, 36);
        _vehicleGrid.DefaultCellStyle.ForeColor = Color.White;
        _vehicleGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 76, 102);
        _vehicleGrid.Columns.Add("order", "순서");
        _vehicleGrid.Columns.Add("vehicle", "판독 차량");
        _vehicleGrid.Columns.Add("confidence", "OCR 신뢰도");
        _vehicleGrid.Columns.Add("result", "판정");
        _vehicleGrid.Columns[0].FillWeight = 15;
        _vehicleGrid.Columns[1].FillWeight = 50;
        _vehicleGrid.Columns[2].FillWeight = 20;
        _vehicleGrid.Columns[3].FillWeight = 20;
    }

    private void RenderSnapshot(ValidationSessionSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RenderSnapshot(snapshot));
            return;
        }

        _statusLabel.Text = snapshot.Status;
        _statusLabel.ForeColor = StatusColor(snapshot.State);
        _stepLabel.Text = snapshot.CurrentStep;
        _elapsedLabel.Text = $"경과 {snapshot.Elapsed:mm\\:ss}";
        _retryLabel.Text = $"재시도 {snapshot.RecognitionRetries} · 중복 {snapshot.DuplicateObservations}";
        _pauseButton.Enabled = snapshot.State == ValidationSessionState.Running;
        _stopButton.Enabled = snapshot.State != ValidationSessionState.Stopped;
        _startButton.Text = snapshot.State is ValidationSessionState.Paused or ValidationSessionState.NeedsAttention
            ? "재개  F1"
            : snapshot.PracticalInputEnabled
                ? "오픈월드 실전 시작  F1"
                : "시작 / 새 검증  F1";

        _vehicleGrid.Rows.Clear();
        for (var index = 0; index < snapshot.Vehicles.Count; index++)
        {
            var vehicle = snapshot.Vehicles[index];
            _vehicleGrid.Rows.Add(
                index + 1,
                vehicle.Name,
                vehicle.Confidence.ToString("F1"),
                vehicle.IsTarget ? "목표" : "일반");
        }

        if (_renderedLogCount != snapshot.LogLines.Count)
        {
            _logBox.Lines = snapshot.LogLines.ToArray();
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
            _renderedLogCount = snapshot.LogLines.Count;
        }

        _modeLabel.Text = snapshot.PracticalInputEnabled
            ? "실전 1회 · 영어 오픈월드 정차 상태에서 시작 · F1 실행 / F2 중지"
            : "검증 모드 · 게임 입력 없음 · F1 시작 / F2 중지";
    }

    private static Panel CreateCard()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(27, 33, 41),
            Margin = new Padding(0, 0, 0, 8)
        };

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 10, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static Color StatusColor(ValidationSessionState state)
        => state switch
        {
            ValidationSessionState.Running => Color.FromArgb(89, 210, 154),
            ValidationSessionState.Paused => Color.FromArgb(255, 193, 7),
            ValidationSessionState.NeedsAttention => Color.FromArgb(255, 167, 38),
            ValidationSessionState.TargetFound => Color.FromArgb(80, 210, 255),
            ValidationSessionState.CycleComplete => Color.FromArgb(130, 200, 255),
            _ => Color.FromArgb(180, 188, 198)
        };

    private void PollSessionHotKeys()
    {
        var f1IsDown = (GetAsyncKeyState((int)F1VirtualKey) & 0x8000) != 0;
        if (!_f1WasDown && f1IsDown)
        {
            _controller.StartOrResume();
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
