using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.FlightSimulator.SimConnect;

namespace MSFS2024SARLocator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LocatorForm());
        }
    }

    internal sealed class LocatorForm : Form
    {
        private const int WmUserSimConnect = 0x0402;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;
        private const double EarthRadiusMeters = 6371000.0;
        private const double MetersPerNauticalMile = 1852.0;
        private const double AutoHideDelaySeconds = 3.0;
        private const double ReconnectDelaySeconds = 3.0;
        private const double ScanTimeoutSeconds = 6.0;
        private const double ScanQuietPeriodSeconds = 0.75;
        private const int StrongTargetScore = 32;
        private const int CandidateConfirmationRequired = 2;
        private const double StartupZoneRadiusNm = 0.18;
        private const double SessionJumpResetNm = 3.0;
        private const double DefaultSearchAreaEntryRadiusNm = 0.50;
        private const double MinimumDepartureDistanceNm = 0.40;
        private const double MinimumGateObservationNm = 1.00;
        private const double MinimumSearchGateSessionAgeSeconds = 12.0;
        private const int CompactWindowWidth = 392;
        private const int CompactHeaderHeight = 34;
        private const int CompactHeaderTitleWidth = 94;
        private const int CompactHeaderSettingsWidth = 58;
        private const int CompactHeaderTopMostWidth = 54;
        private const int CompactHeaderWindowButtonWidth = 22;
        private const int CompactHudHeight = 216;
        private const int CompactSettingsPanelHeight = 200;
        private const int CompactHudPaddingH = 10;
        private const int CompactHudPaddingTop = 6;
        private const int CompactHudPaddingBottom = 8;
        private const int CompactHudModeRowHeight = 18;
        private const int CompactHudMainRowHeight = 124;
        private const int CompactHudButtonRowHeight = 34;
        private const int CompactHudStatusRowHeight = 26;
        private const int CompactGaugeSize = 112;
        private const int CompactGaugeColumnWidth = 120;
        private const int CompactFormPadding = 2;
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopMost = new IntPtr(-2);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoActivate = 0x0010;
        private const double AutoPauseConfirmSeconds = 1.2;
        private const double HardLandingGForceThreshold = 2.0;
        private const double HardLandingVerticalSpeedFps = -10.0;
        private const double EssentialPartHealthyLevel = 0.20;
        private const double EssentialPartBrokenLevel = 0.05;
        private const uint SimConnectGroupPriorityHighest = 1;
        private const int WmKeydown = 0x0100;
        private const int WmKeyup = 0x0101;
        private const int VkEscape = 0x1B;
        private static readonly string[] SimulatorWindowTitles =
        {
            "Microsoft Flight Simulator 2024",
            "Microsoft Flight Simulator"
        };

        private enum Definitions
        {
            UserState = 1,
            ObjectState = 2,
            NavigationState = 3,
            SafetyState = 4,
            WearState = 5
        }

        private enum Requests
        {
            UserState = 1,
            ObjectScan = 2,
            NavigationState = 3,
            SafetyState = 4,
            WearState = 5
        }

        private enum ClientEvents
        {
            ShowPauseMenu = 100,
            PauseToggle = 101
        }

        private enum SystemEvents
        {
            Crashed = 200,
            CrashReset = 201,
            Paused = 202,
            Unpaused = 203
        }

        private enum NotificationGroups
        {
            Safety = 1
        }

        private enum ScanMode
        {
            Ground = 1,
            All = 2
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct UserStateData
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
            public double Heading;
            public int SimOnGround;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct NavigationStateData
        {
            public int IsActiveFlightPlan;
            public int IsActiveWaypoint;
            public double WaypointDistanceMeters;
            public double TargetDistanceMeters;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string NextWaypointId;

            public double FlightPlanWaypointCount;
            public double FlightPlanWaypointIndex;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct ObjectStateData
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Title;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Category;

            public double Latitude;
            public double Longitude;
            public double Altitude;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct SafetyStateData
        {
            public double GForce;
            public double VerticalSpeedFps;
            public int SimOnGround;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct WearStateData
        {
            public double WearExposedPartsLowestLevel;
        }

        private sealed class ScanResult
        {
            public uint ObjectId;
            public string Title;
            public string Category;
            public double Latitude;
            public double Longitude;
            public double Altitude;
            public double DistanceNm;
            public double BearingDeg;
            public double RelativeBearingDeg;
            public int CandidateScore;
            public bool Excluded;
            public string ExclusionReason;
            public ScanMode Source;
        }

        // ADF-style pointer: the aircraft symbol is fixed nose-up, the needle
        // rotates to the target's relative bearing for instant visual guidance.
        // A short easing animation keeps the needle moving continuously instead
        // of stepping whenever a new sample arrives.
        private sealed class DirectionGauge : Control
        {
            private readonly Timer _animationTimer = new Timer();
            private double _displayAngleDeg;
            private double _targetAngleDeg;
            private bool _hasTarget;
            private Color _needleColor = Color.FromArgb(0, 163, 234);

            private readonly Color _faceColor = Color.FromArgb(24, 28, 34);
            private readonly Color _ringColor = Color.FromArgb(96, 104, 116);
            private readonly Color _ringDimColor = Color.FromArgb(58, 64, 74);
            private readonly Color _tickColor = Color.FromArgb(120, 128, 140);
            private readonly Color _planeColor = Color.FromArgb(236, 239, 244);
            private readonly Color _planeDimColor = Color.FromArgb(110, 118, 130);

            public DirectionGauge()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);

                _animationTimer.Interval = 25;
                _animationTimer.Tick += delegate { StepAnimation(); };
            }

            public void SetTarget(double relativeBearingDeg, Color needleColor)
            {
                if (needleColor != _needleColor)
                {
                    _needleColor = needleColor;
                    Invalidate();
                }

                _targetAngleDeg = relativeBearingDeg;

                if (!_hasTarget)
                {
                    // First lock: snap directly, no sweep from a stale angle.
                    _hasTarget = true;
                    _displayAngleDeg = relativeBearingDeg;
                    Invalidate();
                    return;
                }

                if (!_animationTimer.Enabled &&
                    Math.Abs(SignedDeltaDeg(_targetAngleDeg - _displayAngleDeg)) >= 0.1)
                {
                    _animationTimer.Start();
                }
            }

            public void ClearTarget()
            {
                _animationTimer.Stop();
                if (!_hasTarget) return;
                _hasTarget = false;
                Invalidate();
            }

            private void StepAnimation()
            {
                double delta = SignedDeltaDeg(_targetAngleDeg - _displayAngleDeg);

                if (Math.Abs(delta) <= 0.2)
                {
                    _displayAngleDeg = _targetAngleDeg;
                    _animationTimer.Stop();
                }
                else
                {
                    // 45 percent per 25 ms tick: ~90 percent convergence in 100 ms.
                    _displayAngleDeg += delta * 0.45;
                }

                Invalidate();
            }

            private static double SignedDeltaDeg(double angle)
            {
                angle %= 360.0;
                if (angle > 180.0) angle -= 360.0;
                if (angle < -180.0) angle += 360.0;
                return angle;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _animationTimer.Dispose();
                }

                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                System.Drawing.Drawing2D.SmoothingMode previous = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                float cx = Width / 2f;
                float cy = Height / 2f;
                float radius = Math.Min(Width, Height) / 2f - 4f;
                if (radius < 20f) return;

                using (SolidBrush face = new SolidBrush(_faceColor))
                {
                    e.Graphics.FillEllipse(face, cx - radius, cy - radius, radius * 2f, radius * 2f);
                }

                using (Pen ring = new Pen(_hasTarget ? _ringColor : _ringDimColor, 1.6f))
                {
                    e.Graphics.DrawEllipse(ring, cx - radius, cy - radius, radius * 2f, radius * 2f);
                }

                using (Pen tickPen = new Pen(_tickColor, 1.4f))
                using (Pen cardinalPen = new Pen(_tickColor, 2.2f))
                {
                    for (int angle = 0; angle < 360; angle += 30)
                    {
                        bool cardinal = angle % 90 == 0;
                        float length = cardinal ? 9f : 5f;
                        double rad = angle * Math.PI / 180.0;
                        float sin = (float)Math.Sin(rad);
                        float cos = (float)Math.Cos(rad);
                        e.Graphics.DrawLine(
                            cardinal ? cardinalPen : tickPen,
                            cx + sin * (radius - length),
                            cy - cos * (radius - length),
                            cx + sin * (radius - 1.5f),
                            cy - cos * (radius - 1.5f));
                    }
                }

                // Nose reference marker at 12 o'clock.
                using (SolidBrush marker = new SolidBrush(_hasTarget ? _needleColor : _tickColor))
                {
                    PointF[] nose =
                    {
                        new PointF(cx, cy - radius + 12f),
                        new PointF(cx - 5f, cy - radius + 2f),
                        new PointF(cx + 5f, cy - radius + 2f)
                    };
                    e.Graphics.FillPolygon(marker, nose);
                }

                if (_hasTarget)
                {
                    e.Graphics.TranslateTransform(cx, cy);
                    e.Graphics.RotateTransform((float)_displayAngleDeg);

                    float tip = radius - 14f;
                    PointF[] needle =
                    {
                        new PointF(0f, -tip),
                        new PointF(6.5f, -tip + 16f),
                        new PointF(2.2f, -tip + 16f),
                        new PointF(2.2f, radius * 0.32f),
                        new PointF(-2.2f, radius * 0.32f),
                        new PointF(-2.2f, -tip + 16f),
                        new PointF(-6.5f, -tip + 16f)
                    };

                    using (SolidBrush needleBrush = new SolidBrush(_needleColor))
                    {
                        e.Graphics.FillPolygon(needleBrush, needle);
                    }

                    e.Graphics.ResetTransform();
                }

                DrawAircraftSymbol(e.Graphics, cx, cy, radius, _hasTarget ? _planeColor : _planeDimColor);
                e.Graphics.SmoothingMode = previous;
            }

            private static void DrawAircraftSymbol(Graphics graphics, float cx, float cy, float radius, Color color)
            {
                float fuselage = radius * 0.17f;
                float wing = radius * 0.155f;
                float tail = radius * 0.07f;

                using (Pen pen = new Pen(color, 2.6f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    graphics.DrawLine(pen, cx, cy - fuselage, cx, cy + fuselage * 0.85f);
                    graphics.DrawLine(pen, cx - wing, cy - fuselage * 0.1f, cx + wing, cy - fuselage * 0.1f);
                    graphics.DrawLine(pen, cx - tail, cy + fuselage * 0.68f, cx + tail, cy + fuselage * 0.68f);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // MSFS 2024 style palette: desaturated grays with a single blue accent.
        private readonly Color _windowColor = Color.FromArgb(23, 26, 31);
        private readonly Color _headerColor = Color.FromArgb(18, 21, 26);
        private readonly Color _cardColor = Color.FromArgb(31, 35, 42);
        private readonly Color _cardSecondary = Color.FromArgb(40, 45, 53);
        private readonly Color _textPrimary = Color.FromArgb(236, 239, 244);
        private readonly Color _textMuted = Color.FromArgb(148, 156, 168);
        private readonly Color _accentBlue = Color.FromArgb(0, 163, 234);
        private readonly Color _accentGreen = Color.FromArgb(70, 200, 140);
        private readonly Color _accentAmber = Color.FromArgb(238, 178, 64);
        private readonly Color _accentRed = Color.FromArgb(232, 96, 96);
        private readonly Color _borderColor = Color.FromArgb(62, 68, 78);
        private readonly Color _buttonBase = Color.FromArgb(0, 122, 182);
        private readonly Color _buttonHover = Color.FromArgb(16, 150, 214);
        private readonly Color _buttonDisabled = Color.FromArgb(46, 52, 61);

        private SimConnect _simConnect;
        private bool _connected;
        private bool _scanPending;
        private bool _hasUserState;
        private bool _hasNavigationState;
        private bool _searchAreaArmed;
        private bool _settingsVisible;
        private bool _exitRequested;
        private bool _targetHudActive;
        private bool _positionedForTarget;
        private uint _suppressedTargetObjectId;
        private DateTime _lastConnectAttemptUtc = DateTime.MinValue;
        private DateTime _scanRequestedUtc = DateTime.MinValue;
        private DateTime _lastObjectCallbackUtc = DateTime.MinValue;
        private int _scanSequence;
        private int _currentScanObjectCount;
        private DateTime _lastScanCompletedUtc = DateTime.MinValue;
        private DateTime _lastTargetSeenUtc = DateTime.MinValue;
        private DateTime _standbySinceUtc = DateTime.UtcNow;
        private DateTime _flightSessionStartedUtc = DateTime.MinValue;
        private ScanMode _activeScanMode = ScanMode.Ground;
        private ScanMode _targetSource = ScanMode.Ground;
        private bool _sessionAnchorSet;
        private bool _startupZoneEnabled;
        private bool _searchGateApproachObserved;
        private double _largestObservedNavDistanceNm;
        private double _sessionAnchorLatitude;
        private double _sessionAnchorLongitude;
        private bool _hasPreviousUserPoint;
        private double _previousUserLatitude;
        private double _previousUserLongitude;
        private DateTime _previousUserPointUtc = DateTime.MinValue;
        private uint _pendingCandidateId;
        private int _pendingCandidateConfirmations;
        private bool _preferTopMost;
        private bool _autoPauseEnabled = true;
        private bool _autoPauseOnCrash = true;
        private bool _autoPauseOnHardLanding = true;
        private bool _autoPauseOnEssentialPartBroken = true;
        private bool _safetyMonitoringActive;
        private bool _wearVarAvailable;
        private bool _autoPauseWaitingForSim;
        private bool _autoPauseFallbackSent;
        private string _autoPauseReason = string.Empty;
        private DateTime _autoPauseRequestedUtc = DateTime.MinValue;
        private bool _simPaused;
        private bool _wasOnGround;
        private bool _hardLandingArmed = true;
        private bool _crashArmed = true;
        private bool _essentialPartArmed = true;
        private double _lastWearLowestLevel = 1.0;
        private bool _hasSafetyState;
        private SafetyStateData _safetyState;
        private readonly HashSet<uint> _startupIgnoredObjectIds = new HashSet<uint>();
        private UserStateData _userState;
        private NavigationStateData _navigationState;
        private ScanResult _currentTarget;
        private readonly Dictionary<uint, ScanResult> _scanBuffer = new Dictionary<uint, ScanResult>();
        private readonly Timer _timer = new Timer();
        private readonly Timer _pumpTimer = new Timer();

        private readonly TableLayoutPanel _root = new TableLayoutPanel();
        private readonly Panel _headerPanel = new Panel();
        private readonly Panel _hudPanel = new Panel();
        private readonly Panel _settingsPanel = new Panel();
        private readonly Label _appTitle = new Label();
        private readonly Label _connectionState = new Label();
        private readonly Button _settingsButton = new Button();
        private readonly Button _topMostButton = new Button();
        private readonly Button _minimizeButton = new Button();
        private readonly Button _hideButton = new Button();
        private readonly Label _modeLabel = new Label();
        private readonly Label _directionLabel = new Label();
        private readonly Label _turnLabel = new Label();
        private readonly Label _distanceLabel = new Label();
        private readonly Label _bearingLabel = new Label();
        private readonly Label _closeAlert = new Label();
        private readonly Button _mainScanButton = new Button();
        private readonly Label _statusLabel = new Label();
        private readonly DirectionGauge _gauge = new DirectionGauge();
        private readonly Font _headlineFontLarge = new Font("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _headlineFontSmall = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly NumericUpDown _radiusNm = new NumericUpDown();
        private readonly NumericUpDown _searchGateRadiusNm = new NumericUpDown();
        private readonly CheckBox _autoPauseMaster = new CheckBox();
        private readonly CheckBox _autoPauseCrash = new CheckBox();
        private readonly CheckBox _autoPauseHardLanding = new CheckBox();
        private readonly CheckBox _autoPauseEssentialPart = new CheckBox();
        private readonly Button _scanNowButton = new Button();
        private readonly Button _copyCoordinatesButton = new Button();
        private readonly Button _reconnectButton = new Button();
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();

        public LocatorForm()
        {
            Text = "MSFS 2024 SAR Locator 1.0.0";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = _windowColor;
            ForeColor = _textPrimary;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Opacity = 0.82;
            MinimumSize = new Size(CompactWindowWidth, CompactHeaderHeight + CompactHudHeight + CompactFormPadding);
            ClientSize = new Size(CompactWindowWidth, CompactHeaderHeight + CompactHudHeight + CompactFormPadding);
            Padding = new Padding(1);

            BuildUi();
            _scanNowButton.Enabled = false;
            _mainScanButton.Enabled = false;
            BuildTrayIcon();
            ApplyStandbyState("WAITING FOR MSFS", "The locator will connect automatically.");

            _timer.Interval = 500;
            _timer.Tick += TimerOnTick;
            _timer.Start();

            // High-frequency dispatch pump: WM_USER notifications alone can lag
            // behind SIM_FRAME data, so drain the SimConnect queue every 30 ms.
            _pumpTimer.Interval = 30;
            _pumpTimer.Tick += delegate
            {
                if (_simConnect == null) return;
                try { _simConnect.ReceiveMessage(); }
                catch { }
            };
            _pumpTimer.Start();

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                _standbySinceUtc = DateTime.UtcNow;
                UpdateTopMostButtonStyle();
                TryAutoConnect();
            };

        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private void BuildUi()
        {
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.Margin = new Padding(0);
            _root.Padding = new Padding(0);
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHeaderHeight));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHudHeight));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

            BuildHeader();
            BuildHud();
            BuildSettingsPanel();

            _root.Controls.Add(_headerPanel, 0, 0);
            _root.Controls.Add(_hudPanel, 0, 1);
            _root.Controls.Add(_settingsPanel, 0, 2);
            Controls.Add(_root);
            ApplyCompactSize();

            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(_borderColor))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }

                // MSFS panels carry a thin accent strip along the top edge.
                using (SolidBrush accent = new SolidBrush(_accentBlue))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, Width, 2);
                }
            };
        }

        private void BuildHeader()
        {
            _headerPanel.Dock = DockStyle.Fill;
            _headerPanel.BackColor = _headerColor;
            _headerPanel.Padding = Padding.Empty;
            _headerPanel.MouseDown += HeaderMouseDown;

            TableLayoutPanel headerLayout = new TableLayoutPanel();
            headerLayout.Dock = DockStyle.Fill;
            headerLayout.ColumnCount = 6;
            headerLayout.RowCount = 1;
            headerLayout.Padding = new Padding(8, 3, 4, 0);
            headerLayout.Margin = Padding.Empty;
            headerLayout.BackColor = _headerColor;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactHeaderTitleWidth));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactHeaderSettingsWidth));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactHeaderTopMostWidth));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactHeaderWindowButtonWidth));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactHeaderWindowButtonWidth));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _appTitle.Text = "SAR LOCATOR";
            _appTitle.AutoSize = false;
            _appTitle.Dock = DockStyle.Fill;
            _appTitle.TextAlign = ContentAlignment.MiddleLeft;
            _appTitle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
            _appTitle.ForeColor = _textPrimary;
            _appTitle.Margin = new Padding(0, 0, 6, 0);
            _appTitle.MouseDown += HeaderMouseDown;

            _connectionState.Text = "OFFLINE";
            _connectionState.Dock = DockStyle.Fill;
            _connectionState.TextAlign = ContentAlignment.MiddleLeft;
            _connectionState.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            _connectionState.ForeColor = _accentRed;
            _connectionState.AutoEllipsis = true;
            _connectionState.Margin = new Padding(0, 0, 4, 0);
            _connectionState.MouseDown += HeaderMouseDown;

            ConfigureHeaderButton(_settingsButton, "SETTINGS", 58);
            _settingsButton.Click += delegate { ToggleSettings(); };

            ConfigureHeaderButton(_topMostButton, "ON TOP", 56);
            _topMostButton.Click += delegate { ToggleTopMost(); };

            ConfigureHeaderButton(_minimizeButton, "-", 24);
            _minimizeButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            _minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };

            ConfigureHeaderButton(_hideButton, "X", 24);
            _hideButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            _hideButton.Click += delegate
            {
                _exitRequested = true;
                Close();
            };

            headerLayout.Controls.Add(_appTitle, 0, 0);
            headerLayout.Controls.Add(_connectionState, 1, 0);
            headerLayout.Controls.Add(_settingsButton, 2, 0);
            headerLayout.Controls.Add(_topMostButton, 3, 0);
            headerLayout.Controls.Add(_minimizeButton, 4, 0);
            headerLayout.Controls.Add(_hideButton, 5, 0);
            _headerPanel.Controls.Add(headerLayout);
        }

        private void BuildHud()
        {
            _hudPanel.Dock = DockStyle.Fill;
            _hudPanel.BackColor = _cardColor;
            _hudPanel.Padding = new Padding(CompactHudPaddingH, CompactHudPaddingTop, CompactHudPaddingH, CompactHudPaddingBottom);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHudModeRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHudMainRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHudButtonRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactHudStatusRowHeight));

            _modeLabel.Text = "STANDBY";
            _modeLabel.Dock = DockStyle.Fill;
            _modeLabel.TextAlign = ContentAlignment.MiddleLeft;
            _modeLabel.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            _modeLabel.ForeColor = _textMuted;
            _modeLabel.Margin = new Padding(2, 0, 0, 0);

            // Main instrument row: direction gauge on the left, data column on the right.
            TableLayoutPanel mainRow = new TableLayoutPanel();
            mainRow.Dock = DockStyle.Fill;
            mainRow.ColumnCount = 2;
            mainRow.RowCount = 1;
            mainRow.Margin = new Padding(0);
            mainRow.Padding = new Padding(0);
            mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CompactGaugeColumnWidth));
            mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _gauge.Size = new Size(CompactGaugeSize, CompactGaugeSize);
            _gauge.Anchor = AnchorStyles.None;
            _gauge.Margin = new Padding(0);
            _gauge.BackColor = _cardColor;

            TableLayoutPanel infoColumn = new TableLayoutPanel();
            infoColumn.Dock = DockStyle.Fill;
            infoColumn.ColumnCount = 1;
            infoColumn.RowCount = 4;
            infoColumn.Margin = new Padding(6, 2, 0, 2);
            infoColumn.Padding = new Padding(0);
            infoColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            infoColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            infoColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            infoColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _directionLabel.Text = "READY";
            _directionLabel.Dock = DockStyle.Fill;
            _directionLabel.TextAlign = ContentAlignment.MiddleLeft;
            _directionLabel.Font = _headlineFontLarge;
            _directionLabel.ForeColor = _accentBlue;
            _directionLabel.Margin = new Padding(0);
            _directionLabel.AutoEllipsis = true;

            _distanceLabel.Text = "--";
            _distanceLabel.Dock = DockStyle.Fill;
            _distanceLabel.TextAlign = ContentAlignment.MiddleLeft;
            _distanceLabel.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point);
            _distanceLabel.ForeColor = _textPrimary;
            _distanceLabel.Margin = new Padding(0);

            _turnLabel.Text = "Waiting for a SAR target";
            _turnLabel.Dock = DockStyle.Fill;
            _turnLabel.TextAlign = ContentAlignment.TopLeft;
            _turnLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            _turnLabel.ForeColor = _textMuted;
            _turnLabel.Margin = new Padding(0, 1, 0, 0);
            _turnLabel.AutoEllipsis = true;

            _bearingLabel.Text = "Target bearing unavailable";
            _bearingLabel.Dock = DockStyle.Fill;
            _bearingLabel.TextAlign = ContentAlignment.TopLeft;
            _bearingLabel.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            _bearingLabel.ForeColor = _textMuted;
            _bearingLabel.Margin = new Padding(0, 0, 0, 0);
            _bearingLabel.AutoEllipsis = true;

            infoColumn.Controls.Add(_directionLabel, 0, 0);
            infoColumn.Controls.Add(_distanceLabel, 0, 1);
            infoColumn.Controls.Add(_turnLabel, 0, 2);
            infoColumn.Controls.Add(_bearingLabel, 0, 3);

            mainRow.Controls.Add(_gauge, 0, 0);
            mainRow.Controls.Add(infoColumn, 1, 0);

            _mainScanButton.Text = "SCAN FOR TARGET";
            _mainScanButton.Dock = DockStyle.Fill;
            _mainScanButton.Margin = new Padding(0, 2, 0, 0);
            _mainScanButton.Padding = new Padding(0, 1, 0, 0);
            _mainScanButton.TextAlign = ContentAlignment.MiddleCenter;
            _mainScanButton.FlatStyle = FlatStyle.Flat;
            _mainScanButton.FlatAppearance.BorderSize = 0;
            _mainScanButton.BackColor = _buttonBase;
            _mainScanButton.ForeColor = Color.White;
            _mainScanButton.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            _mainScanButton.Cursor = Cursors.Hand;
            _mainScanButton.Click += delegate { StartManualScan(); };
            _mainScanButton.MouseEnter += delegate
            {
                if (_mainScanButton.Enabled) _mainScanButton.BackColor = _buttonHover;
            };
            _mainScanButton.MouseLeave += delegate
            {
                if (_mainScanButton.Enabled) _mainScanButton.BackColor = _buttonBase;
            };
            _mainScanButton.EnabledChanged += delegate
            {
                _mainScanButton.BackColor = _mainScanButton.Enabled ? _buttonBase : _buttonDisabled;
                _mainScanButton.ForeColor = _mainScanButton.Enabled ? Color.White : _textMuted;
            };

            _statusLabel.Text = "Starting...";
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            _statusLabel.ForeColor = Color.FromArgb(120, 128, 140);
            _statusLabel.AutoEllipsis = true;
            _statusLabel.UseCompatibleTextRendering = true;
            _statusLabel.Margin = Padding.Empty;

            layout.Controls.Add(_modeLabel, 0, 0);
            layout.Controls.Add(mainRow, 0, 1);
            layout.Controls.Add(_mainScanButton, 0, 2);
            layout.Controls.Add(_statusLabel, 0, 3);

            _hudPanel.Controls.Add(layout);
        }

        private void BuildSettingsPanel()
        {
            _settingsPanel.Dock = DockStyle.Fill;
            _settingsPanel.BackColor = _windowColor;
            _settingsPanel.Padding = new Padding(8, 6, 8, 8);
            _settingsPanel.Visible = false;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.BackColor = _cardSecondary;
            layout.Padding = new Padding(8, 8, 8, 8);
            layout.Margin = Padding.Empty;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            TableLayoutPanel numericRow = new TableLayoutPanel();
            numericRow.Dock = DockStyle.Fill;
            numericRow.ColumnCount = 2;
            numericRow.RowCount = 1;
            numericRow.Margin = Padding.Empty;
            numericRow.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            numericRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            numericRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            ConfigureNumeric(_radiusNm, 1, 100, 15);
            ConfigureNumeric(_searchGateRadiusNm, 0, 3, (decimal)DefaultSearchAreaEntryRadiusNm);
            _searchGateRadiusNm.DecimalPlaces = 2;
            _searchGateRadiusNm.Increment = 0.05M;
            _searchGateRadiusNm.Minimum = 0.10M;

            numericRow.Controls.Add(CreateSettingsField("Radius NM", _radiusNm, false), 0, 0);
            numericRow.Controls.Add(CreateSettingsField("Search gate NM", _searchGateRadiusNm, true), 1, 0);

            ConfigureAutoPauseCheckBox(_autoPauseMaster, "Auto-pause on incident", true);
            _autoPauseMaster.Checked = true;
            _autoPauseMaster.CheckedChanged += delegate { ApplyAutoPauseSettings(); };

            ConfigureAutoPauseCheckBox(_autoPauseCrash, "Crash", false);
            _autoPauseCrash.Checked = true;
            _autoPauseCrash.CheckedChanged += delegate
            {
                _autoPauseOnCrash = _autoPauseCrash.Checked;
                if (_autoPauseEnabled) UpdateSafetyMonitoring();
            };

            ConfigureAutoPauseCheckBox(_autoPauseHardLanding, "Hard landing", false);
            _autoPauseHardLanding.Checked = true;
            _autoPauseHardLanding.CheckedChanged += delegate
            {
                _autoPauseOnHardLanding = _autoPauseHardLanding.Checked;
                if (_autoPauseEnabled) UpdateSafetyMonitoring();
            };

            ConfigureAutoPauseCheckBox(_autoPauseEssentialPart, "Essential part broken", false);
            _autoPauseEssentialPart.Checked = true;
            _autoPauseEssentialPart.CheckedChanged += delegate
            {
                _autoPauseOnEssentialPartBroken = _autoPauseEssentialPart.Checked;
                if (_autoPauseEnabled) UpdateSafetyMonitoring();
            };

            ApplyAutoPauseSettings();

            TableLayoutPanel optionRow = new TableLayoutPanel();
            optionRow.Dock = DockStyle.Fill;
            optionRow.ColumnCount = 3;
            optionRow.RowCount = 1;
            optionRow.Margin = Padding.Empty;
            optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            optionRow.Controls.Add(_autoPauseCrash, 0, 0);
            optionRow.Controls.Add(_autoPauseHardLanding, 1, 0);
            optionRow.Controls.Add(_autoPauseEssentialPart, 2, 0);

            ConfigureActionButton(_scanNowButton, "Scan target");
            _scanNowButton.Click += delegate { StartManualScan(); };

            ConfigureActionButton(_copyCoordinatesButton, "Copy coordinates");
            _copyCoordinatesButton.Click += delegate { CopyTargetCoordinates(); };

            ConfigureActionButton(_reconnectButton, "Reconnect");
            _reconnectButton.Click += delegate
            {
                DisconnectInternal(false);
                TryAutoConnect();
            };

            TableLayoutPanel actionRow = new TableLayoutPanel();
            actionRow.Dock = DockStyle.Fill;
            actionRow.ColumnCount = 2;
            actionRow.RowCount = 1;
            actionRow.Margin = new Padding(0, 4, 0, 0);
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionRow.Controls.Add(_scanNowButton, 0, 0);
            actionRow.Controls.Add(_reconnectButton, 1, 0);
            _scanNowButton.Margin = new Padding(0, 0, 4, 0);
            _reconnectButton.Margin = new Padding(4, 0, 0, 0);

            _copyCoordinatesButton.Margin = new Padding(0, 4, 0, 0);

            layout.Controls.Add(numericRow, 0, 0);
            layout.Controls.Add(_autoPauseMaster, 0, 1);
            layout.Controls.Add(optionRow, 0, 2);
            layout.Controls.Add(actionRow, 0, 3);
            layout.Controls.Add(_copyCoordinatesButton, 0, 4);
            _settingsPanel.Controls.Add(layout);
        }

        private void BuildTrayIcon()
        {
            ToolStripMenuItem openItem = new ToolStripMenuItem("Open locator");
            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Open settings");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");

            openItem.Click += delegate { ShowFromTray(false); };
            settingsItem.Click += delegate { ShowFromTray(true); };
            exitItem.Click += delegate
            {
                _exitRequested = true;
                Close();
            };

            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(settingsItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.Text = "MSFS 2024 SAR Locator";
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { ShowFromTray(false); };
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WmUserSimConnect)
            {
                if (_simConnect != null)
                {
                    try
                    {
                        _simConnect.ReceiveMessage();
                    }
                    catch (Exception ex)
                    {
                        SetStatus("SimConnect receive error: " + ex.Message);
                    }
                }
                return;
            }

            base.DefWndProc(ref m);
        }

        private void TimerOnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;

            if (!_connected)
            {
                if ((now - _lastConnectAttemptUtc).TotalSeconds >= ReconnectDelaySeconds)
                {
                    TryAutoConnect();
                }

                return;
            }

            if (_scanPending)
            {
                if (_currentScanObjectCount > 0 &&
                    _lastObjectCallbackUtc != DateTime.MinValue &&
                    (now - _lastObjectCallbackUtc).TotalSeconds >= ScanQuietPeriodSeconds)
                {
                    _scanPending = false;
                    FinishScan(false);
                }
                else if ((now - _scanRequestedUtc).TotalSeconds > ScanTimeoutSeconds)
                {
                    HandleScanTimeout();
                }
            }

            if (_preferTopMost && IsHandleCreated && Visible && WindowState != FormWindowState.Minimized)
            {
                EnforceTopMost();
            }

            if (_hasUserState && _currentTarget != null)
            {
                RefreshTargetMetrics(_currentTarget);
                UpdateTargetHud(_currentTarget);
            }

            EvaluateSearchAreaGate();
            UpdateAutoPauseFallback(now);
        }

        private void TryAutoConnect()
        {
            if (_connected || _simConnect != null) return;

            _lastConnectAttemptUtc = DateTime.UtcNow;
            ApplyStandbyState("WAITING FOR MSFS", "Connecting automatically...");

            try
            {
                _simConnect = new SimConnect("MSFS 2024 SAR Locator", Handle, WmUserSimConnect, null, 0);
                _simConnect.OnRecvOpen += SimConnectOnRecvOpen;
                _simConnect.OnRecvQuit += SimConnectOnRecvQuit;
                _simConnect.OnRecvException += SimConnectOnRecvException;
                _simConnect.OnRecvSimobjectData += SimConnectOnRecvSimobjectData;
                _simConnect.OnRecvSimobjectDataBytype += SimConnectOnRecvSimobjectDataBytype;
                _simConnect.OnRecvEvent += SimConnectOnRecvEvent;

                DefineData();
                SetupSafetyEvents();
                RequestUserState();
                RequestNavigationState();

                _connected = true;

                // Safety monitoring checks _connected, so it must be armed after the
                // connection is marked live.
                ResetSafetyTracking();
                UpdateSafetyMonitoring();

                UpdateConnectionState();
                ApplyStandbyState("READY TO SCAN", "Enter the Search Area, then click the button below.");
                _standbySinceUtc = DateTime.UtcNow;
                UpdateTrayText("Connected - ready to scan");
            }
            catch
            {
                DisconnectInternal(false);
                ApplyStandbyState("WAITING FOR MSFS", "The locator will connect automatically.");
                UpdateTrayText("Waiting for MSFS");
            }
        }

        private void DisconnectInternal(bool resetStandbyTimer)
        {
            _connected = false;
            _scanPending = false;
            _hasUserState = false;
            _hasNavigationState = false;
            _searchAreaArmed = false;
            _scanBuffer.Clear();
            _currentTarget = null;
            _targetHudActive = false;
            _suppressedTargetObjectId = 0;
            ResetSessionTracking();
            _positionedForTarget = false;
            ResetSafetyTracking();
            _safetyMonitoringActive = false;
            _hasSafetyState = false;

            if (_simConnect != null)
            {
                try { _simConnect.Dispose(); }
                catch { }
                _simConnect = null;
            }

            UpdateConnectionState();

            if (resetStandbyTimer)
            {
                _standbySinceUtc = DateTime.UtcNow;
            }
        }

        private void DefineData()
        {
            _simConnect.AddToDataDefinition(Definitions.UserState, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.UserState, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.UserState, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.UserState, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.UserState, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<UserStateData>(Definitions.UserState);

            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS IS ACTIVE FLIGHT PLAN", "Bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS IS ACTIVE WAY POINT", "Bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS WP DISTANCE", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS TARGET DISTANCE", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS WP NEXT ID", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS FLIGHT PLAN WP COUNT", "number", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.NavigationState, "GPS FLIGHT PLAN WP INDEX", "number", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<NavigationStateData>(Definitions.NavigationState);

            _simConnect.AddToDataDefinition(Definitions.ObjectState, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.ObjectState, "CATEGORY", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.ObjectState, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.ObjectState, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.ObjectState, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<ObjectStateData>(Definitions.ObjectState);

            _simConnect.AddToDataDefinition(Definitions.SafetyState, "G FORCE", "GForce", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.SafetyState, "VERTICAL SPEED", "feet per second", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(Definitions.SafetyState, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<SafetyStateData>(Definitions.SafetyState);

            // Wear and tear lives in its own definition: if the SimVar is missing on a
            // given MSFS build, the resulting exception must not disable hard-landing
            // detection as well.
            try
            {
                _simConnect.AddToDataDefinition(Definitions.WearState, "WEAR AND TEAR EXPOSED PARTS LOWEST LEVEL", "Percent over 100", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<WearStateData>(Definitions.WearState);
                _wearVarAvailable = true;
            }
            catch
            {
                _wearVarAvailable = false;
            }
        }

        private void RequestUserState()
        {
            // SIM_FRAME keeps the direction needle responsive while maneuvering.
            _simConnect.RequestDataOnSimObject(
                Requests.UserState,
                Definitions.UserState,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);
        }

        private void RequestNavigationState()
        {
            _simConnect.RequestDataOnSimObject(
                Requests.NavigationState,
                Definitions.NavigationState,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);
        }

        private void SetupSafetyEvents()
        {
            if (_simConnect == null) return;

            _simConnect.MapClientEventToSimEvent(ClientEvents.ShowPauseMenu, "SHOW_PAUSEMENU");
            _simConnect.MapClientEventToSimEvent(ClientEvents.PauseToggle, "PAUSE_TOGGLE");
            _simConnect.AddClientEventToNotificationGroup(NotificationGroups.Safety, ClientEvents.ShowPauseMenu, false);
            _simConnect.AddClientEventToNotificationGroup(NotificationGroups.Safety, ClientEvents.PauseToggle, false);
            _simConnect.SetNotificationGroupPriority(NotificationGroups.Safety, SimConnectGroupPriorityHighest);

            _simConnect.SubscribeToSystemEvent(SystemEvents.Crashed, "Crashed");
            _simConnect.SubscribeToSystemEvent(SystemEvents.CrashReset, "CrashReset");
            _simConnect.SubscribeToSystemEvent(SystemEvents.Paused, "Paused");
            _simConnect.SubscribeToSystemEvent(SystemEvents.Unpaused, "Unpaused");
        }

        private void UpdateSafetyMonitoring()
        {
            bool shouldMonitor = _connected
                && _simConnect != null
                && _autoPauseEnabled
                && (_autoPauseOnHardLanding || _autoPauseOnEssentialPartBroken);

            if (shouldMonitor)
            {
                RequestSafetyState();
                return;
            }

            StopSafetyState();
        }

        private void RequestSafetyState()
        {
            if (_simConnect == null || _safetyMonitoringActive) return;

            try
            {
                _simConnect.RequestDataOnSimObject(
                    Requests.SafetyState,
                    Definitions.SafetyState,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0,
                    0,
                    0);

                if (_wearVarAvailable)
                {
                    _simConnect.RequestDataOnSimObject(
                        Requests.WearState,
                        Definitions.WearState,
                        SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.SECOND,
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0,
                        0,
                        0);
                }

                _safetyMonitoringActive = true;
            }
            catch (Exception ex)
            {
                SetStatus("Safety monitor request failed: " + ex.Message);
            }
        }

        private void StopSafetyState()
        {
            if (_safetyMonitoringActive && _simConnect != null)
            {
                try
                {
                    _simConnect.RequestDataOnSimObject(
                        Requests.SafetyState,
                        Definitions.SafetyState,
                        SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.NEVER,
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0,
                        0,
                        0);

                    if (_wearVarAvailable)
                    {
                        _simConnect.RequestDataOnSimObject(
                            Requests.WearState,
                            Definitions.WearState,
                            SimConnect.SIMCONNECT_OBJECT_ID_USER,
                            SIMCONNECT_PERIOD.NEVER,
                            SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                            0,
                            0,
                            0);
                    }
                }
                catch
                {
                }
            }

            _safetyMonitoringActive = false;
            _hasSafetyState = false;
        }

        private void ResetSafetyTracking()
        {
            _simPaused = false;
            _wasOnGround = false;
            _hardLandingArmed = true;
            _crashArmed = true;
            _essentialPartArmed = true;
            _lastWearLowestLevel = 1.0;
            _autoPauseWaitingForSim = false;
            _autoPauseFallbackSent = false;
            _autoPauseReason = string.Empty;
            _autoPauseRequestedUtc = DateTime.MinValue;
        }

        private void ApplyAutoPauseSettings()
        {
            _autoPauseEnabled = _autoPauseMaster.Checked;
            _autoPauseOnCrash = _autoPauseCrash.Checked;
            _autoPauseOnHardLanding = _autoPauseHardLanding.Checked;
            _autoPauseOnEssentialPartBroken = _autoPauseEssentialPart.Checked;

            bool subEnabled = _autoPauseEnabled;
            _autoPauseCrash.Enabled = subEnabled;
            _autoPauseHardLanding.Enabled = subEnabled;
            _autoPauseEssentialPart.Enabled = subEnabled;

            if (_autoPauseEnabled)
            {
                ResetSafetyTracking();
            }

            UpdateSafetyMonitoring();
        }

        private void TriggerAutoPause(string reason)
        {
            if (!_connected || !_autoPauseEnabled || _simPaused || _autoPauseWaitingForSim) return;

            _autoPauseReason = reason;
            _autoPauseRequestedUtc = DateTime.UtcNow;
            _autoPauseWaitingForSim = true;
            _autoPauseFallbackSent = false;

            // Escape reproduces the manual pause UI, but a posted key message can be
            // ignored by the simulator. The Paused system event is the only reliable
            // confirmation, so the SimConnect fallback runs if it never arrives.
            TrySendEscapeToSimulator();
            SetStatus("Auto-pause requested: " + reason);
        }

        private void UpdateAutoPauseFallback(DateTime now)
        {
            if (!_autoPauseWaitingForSim) return;

            if (_simPaused)
            {
                _autoPauseWaitingForSim = false;
                SetStatus("Auto-paused: " + _autoPauseReason);
                return;
            }

            double elapsed = (now - _autoPauseRequestedUtc).TotalSeconds;
            if (elapsed < AutoPauseConfirmSeconds) return;

            if (!_autoPauseFallbackSent)
            {
                _autoPauseFallbackSent = true;
                _autoPauseRequestedUtc = now;

                if (TrySendPauseMenuEvent())
                {
                    SetStatus("Auto-pause: Escape ignored, using SimConnect pause.");
                }
                else
                {
                    _autoPauseWaitingForSim = false;
                    SetStatus("Auto-pause failed: could not open pause menu.");
                }

                return;
            }

            _autoPauseWaitingForSim = false;
            SetStatus("Auto-pause failed: simulator did not pause (" + _autoPauseReason + ").");
        }

        private bool TrySendPauseMenuEvent()
        {
            if (_simConnect == null) return false;

            try
            {
                _simConnect.TransmitClientEvent(
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    ClientEvents.ShowPauseMenu,
                    0,
                    NotificationGroups.Safety,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                return true;
            }
            catch
            {
                // Fall through to PAUSE_TOGGLE.
            }

            try
            {
                _simConnect.TransmitClientEvent(
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    ClientEvents.PauseToggle,
                    0,
                    NotificationGroups.Safety,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySendEscapeToSimulator()
        {
            for (int i = 0; i < SimulatorWindowTitles.Length; i++)
            {
                IntPtr hwnd = FindWindow(null, SimulatorWindowTitles[i]);
                if (hwnd == IntPtr.Zero) continue;

                PostMessage(hwnd, WmKeydown, (IntPtr)VkEscape, IntPtr.Zero);
                PostMessage(hwnd, WmKeyup, (IntPtr)VkEscape, IntPtr.Zero);
                return true;
            }

            return false;
        }

        private void SimConnectOnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            SystemEvents systemEvent = (SystemEvents)data.uEventID;

            if (systemEvent == SystemEvents.Crashed)
            {
                if (_autoPauseEnabled && _autoPauseOnCrash && _crashArmed)
                {
                    TriggerAutoPause("crash detected");
                    _crashArmed = false;
                }
                return;
            }

            if (systemEvent == SystemEvents.CrashReset)
            {
                _crashArmed = true;
                return;
            }

            if (systemEvent == SystemEvents.Paused)
            {
                _simPaused = data.dwData != 0;
                return;
            }

            if (systemEvent == SystemEvents.Unpaused)
            {
                _simPaused = false;
                _crashArmed = true;
                _hardLandingArmed = true;
                _essentialPartArmed = true;
            }
        }

        private void EvaluateSafetyState(SafetyStateData state)
        {
            bool onGround = state.SimOnGround != 0;

            if (_autoPauseEnabled && _autoPauseOnHardLanding && _hardLandingArmed)
            {
                if (onGround && !_wasOnGround)
                {
                    bool hardLanding = state.GForce >= HardLandingGForceThreshold
                        || state.VerticalSpeedFps <= HardLandingVerticalSpeedFps;

                    if (hardLanding)
                    {
                        TriggerAutoPause("hard landing detected");
                        _hardLandingArmed = false;
                    }
                }

                if (!onGround && _wasOnGround)
                {
                    _hardLandingArmed = true;
                }
            }

            _wasOnGround = onGround;
        }

        private void EvaluateWearState(WearStateData state)
        {
            double wearLevel = state.WearExposedPartsLowestLevel;

            if (_autoPauseEnabled && _autoPauseOnEssentialPartBroken && _essentialPartArmed)
            {
                if (_lastWearLowestLevel >= EssentialPartHealthyLevel && wearLevel <= EssentialPartBrokenLevel)
                {
                    TriggerAutoPause("essential part broken");
                    _essentialPartArmed = false;
                }
            }

            _lastWearLowestLevel = wearLevel;
        }

        private void StartManualScan()
        {
            if (!_connected || _simConnect == null)
            {
                SetStatus("MSFS is not connected yet.");
                return;
            }

            if (!_hasUserState)
            {
                SetStatus("Waiting for an active aircraft session.");
                return;
            }

            if (_scanPending)
            {
                SetStatus("A scan is already in progress.");
                return;
            }

            // In RC9 the player is the search-area gate. Pressing this button
            // explicitly requests exactly one SAR scan cycle.
            _searchAreaArmed = true;
            ResetCandidateConfirmation();
            _mainScanButton.Enabled = false;
            _scanNowButton.Enabled = false;
            _mainScanButton.Enabled = false;

            ApplyStandbyState("MANUAL SCAN", "Searching for the rescue target...");
            _directionLabel.Text = "SCANNING";
            _directionLabel.ForeColor = _accentBlue;
            _gauge.ClearTarget();
            _turnLabel.Text = "Ground vehicles first, then All SimObjects if needed";
            _distanceLabel.Text = "--";
            _bearingLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Search radius {0:0} NM",
                _radiusNm.Value);

            BeginScan(ScanMode.Ground);
        }

        private void BeginScan(ScanMode mode)
        {
            if (_simConnect == null || !_searchAreaArmed) return;

            _scanBuffer.Clear();
            _activeScanMode = mode;
            _scanPending = true;
            _scanRequestedUtc = DateTime.UtcNow;
            _lastObjectCallbackUtc = DateTime.MinValue;
            _currentScanObjectCount = 0;
            _scanSequence++;

            uint radiusMeters = (uint)Math.Min(200000.0, (double)_radiusNm.Value * MetersPerNauticalMile);
            SIMCONNECT_SIMOBJECT_TYPE type = mode == ScanMode.Ground
                ? SIMCONNECT_SIMOBJECT_TYPE.GROUND
                : SIMCONNECT_SIMOBJECT_TYPE.ALL;

            UpdateScanHud();

            try
            {
                _simConnect.RequestDataOnSimObjectType(Requests.ObjectScan, Definitions.ObjectState, radiusMeters, type);
            }
            catch (Exception ex)
            {
                _scanPending = false;
                SetStatus("Scan request failed: " + ex.Message);
                _turnLabel.Text = "SCAN ERROR";
            }
        }

        private void HandleScanTimeout()
        {
            _scanPending = false;
            FinishScan(true);
        }

        private void SimConnectOnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            SetStatus("Connected to MSFS. Enter the Search Area, then press SCAN FOR TARGET.");
        }

        private void SimConnectOnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            DisconnectInternal(true);
            ApplyStandbyState("WAITING FOR MSFS", "MSFS session closed. Reconnecting automatically...");
            UpdateTrayText("Waiting for MSFS");
        }

        private void SimConnectOnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            SetStatus("SimConnect: " + ((SIMCONNECT_EXCEPTION)data.dwException).ToString());
        }

        private void SimConnectOnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwData == null || data.dwData.Length == 0) return;

            if (data.dwRequestID == (uint)Requests.UserState)
            {
                try
                {
                    _userState = (UserStateData)data.dwData[0];
                    _hasUserState = IsValidCoordinate(_userState.Latitude, _userState.Longitude);

                    if (_hasUserState)
                    {
                        if (!_scanPending && !_mainScanButton.Enabled && _connected)
                        {
                            _mainScanButton.Enabled = true;
                            _scanNowButton.Enabled = true;
                        }

                        TrackFlightSession(_userState);

                        // Needle updates run at frame rate; text-only debug info is throttled.
                        if (_currentTarget != null)
                        {
                            RefreshTargetMetrics(_currentTarget);
                            UpdateTargetHud(_currentTarget);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("Aircraft data error: " + ex.Message);
                }
                return;
            }

            if (data.dwRequestID == (uint)Requests.NavigationState)
            {
                try
                {
                    _navigationState = (NavigationStateData)data.dwData[0];
                    _hasNavigationState = true;
                    EvaluateSearchAreaGate();
                }
                catch (Exception ex)
                {
                    _hasNavigationState = false;
                    SetStatus("Navigation data error: " + ex.Message);
                }
                return;
            }

            if (data.dwRequestID == (uint)Requests.SafetyState)
            {
                try
                {
                    _safetyState = (SafetyStateData)data.dwData[0];
                    _hasSafetyState = true;
                    EvaluateSafetyState(_safetyState);
                }
                catch (Exception ex)
                {
                    _hasSafetyState = false;
                    SetStatus("Safety data error: " + ex.Message);
                }
                return;
            }

            if (data.dwRequestID == (uint)Requests.WearState)
            {
                try
                {
                    EvaluateWearState((WearStateData)data.dwData[0]);
                }
                catch (Exception ex)
                {
                    SetStatus("Wear data error: " + ex.Message);
                }
            }
        }

        private void SimConnectOnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            if (!_searchAreaArmed) return;
            if (!_scanPending) return;
            if (data.dwRequestID != (uint)Requests.ObjectScan || data.dwData == null || data.dwData.Length == 0) return;

            try
            {
                _lastObjectCallbackUtc = DateTime.UtcNow;

                ObjectStateData raw = (ObjectStateData)data.dwData[0];
                if (data.dwObjectID != SimConnect.SIMCONNECT_OBJECT_ID_USER && IsValidCoordinate(raw.Latitude, raw.Longitude))
                {
                    ScanResult result = BuildScanResult(data.dwObjectID, raw, _activeScanMode);
                    _scanBuffer[data.dwObjectID] = result;
                    _currentScanObjectCount = _scanBuffer.Count;

                    if (_currentTarget != null && result.ObjectId == _currentTarget.ObjectId)
                    {
                        _lastTargetSeenUtc = DateTime.UtcNow;
                    }

                    // Known/semantic SAR targets are safe to lock immediately because
                    // RC8 does not scan until the Career search phase is active.
                    if (_currentTarget == null && IsImmediateSarTarget(result))
                    {
                        _scanPending = false;
                        LockTarget(result);
                        return;
                    }

                    UpdateScanHud();
                }
            }
            catch (Exception ex)
            {
                _scanPending = false;
                SetStatus("Object data error: " + ex.Message);
                _turnLabel.Text = "SCAN ERROR";
            }
        }

        private ScanResult BuildScanResult(uint objectId, ObjectStateData raw, ScanMode source)
        {
            ScanResult result = new ScanResult();
            result.ObjectId = objectId;
            result.Title = raw.Title ?? string.Empty;
            result.Category = raw.Category ?? string.Empty;
            result.Latitude = raw.Latitude;
            result.Longitude = raw.Longitude;
            result.Altitude = raw.Altitude;
            result.Source = source;
            RefreshTargetMetrics(result);
            result.CandidateScore = ScoreCandidate(result);

            string exclusionReason;
            result.Excluded = ShouldExclude(result, out exclusionReason);
            result.ExclusionReason = exclusionReason;

            if (!result.Excluded && IsStartupZoneObject(result))
            {
                result.Excluded = true;
                result.ExclusionReason = "mission start zone";
                _startupIgnoredObjectIds.Add(result.ObjectId);
            }
            else if (!result.Excluded && _startupIgnoredObjectIds.Contains(result.ObjectId))
            {
                result.Excluded = true;
                result.ExclusionReason = "mission start object";
            }

            return result;
        }

        private void FinishScan(bool timedOut)
        {
            _lastScanCompletedUtc = DateTime.UtcNow;

            List<ScanResult> allResults = _scanBuffer.Values
                .OrderByDescending(x => x.CandidateScore)
                .ThenBy(x => x.DistanceNm)
                .ToList();

            List<ScanResult> usable = allResults
                .Where(x => !x.Excluded)
                .OrderByDescending(x => x.CandidateScore)
                .ThenBy(x => x.DistanceNm)
                .ToList();

            ScanResult currentMatch = null;
            if (_currentTarget != null)
            {
                currentMatch = usable.FirstOrDefault(x => x.ObjectId == _currentTarget.ObjectId);
            }

            if (currentMatch != null)
            {
                _currentTarget = currentMatch;
                _targetSource = currentMatch.Source;
                _lastTargetSeenUtc = DateTime.UtcNow;
                EnsureTargetHudVisible();
                SetStatus("SAR target lock refreshed.");
                EndManualScan();
                return;
            }

            ScanResult immediate = usable.FirstOrDefault(IsImmediateSarTarget);
            if (immediate != null)
            {
                LockTarget(immediate);
                return;
            }

            ScanResult best = usable.FirstOrDefault(IsStrongCandidate);
            if (best != null)
            {
                LockTarget(best);
                return;
            }

            if (_activeScanMode == ScanMode.Ground)
            {
                // The same user click expands once to All SimObjects.
                BeginScan(ScanMode.All);
                return;
            }

            ResetCandidateConfirmation();
            ApplyStandbyState(
                "NO TARGET FOUND",
                "Move farther into the Search Area and press SCAN FOR TARGET again.");
            _directionLabel.Text = "TRY AGAIN";
            _directionLabel.ForeColor = _textPrimary;
            _turnLabel.Text = "No strong SAR candidate in this scan";
            _distanceLabel.Text = "--";
            _bearingLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Last scan: {0} object(s) | Radius {1:0} NM",
                _currentScanObjectCount,
                _radiusNm.Value);

            SetStatus(timedOut
                ? "Manual scan finished without a target."
                : "No SAR target found in the manual scan.");

            EndManualScan();
        }

        private void EndManualScan()
        {
            _scanPending = false;
            _lastScanCompletedUtc = DateTime.UtcNow;
            _mainScanButton.Enabled = _connected && _hasUserState;
            _scanNowButton.Enabled = _connected && _hasUserState;
        }

        private void LockTarget(ScanResult target)
        {
            bool newLock = _currentTarget == null || _currentTarget.ObjectId != target.ObjectId;
            _currentTarget = target;
            _targetSource = target.Source;
            ResetCandidateConfirmation();
            _lastTargetSeenUtc = DateTime.UtcNow;

            if (newLock)
            {
                _positionedForTarget = false;

                // A manual dismissal is remembered for the current Object ID.
                // A genuinely different SAR target is allowed to auto-show once.
                if (_suppressedTargetObjectId != target.ObjectId)
                {
                    _suppressedTargetObjectId = 0;
                }
            }

            EnsureTargetHudVisible();
            UpdateTargetHud(target);

            SetStatus("SAR target locked.");
            UpdateTrayText("SAR target locked");
            EndManualScan();
        }

        private void UnlockTarget()
        {
            _currentTarget = null;
            _targetHudActive = false;
            ResetCandidateConfirmation();
            _positionedForTarget = false;
            ApplyStandbyState(_searchAreaArmed ? "SEARCH AREA ACTIVE" : "WAITING FOR SEARCH AREA", _searchAreaArmed ? "Searching for the next rescue target..." : "Object scanning is paused until the search area is reached.");
            _standbySinceUtc = DateTime.UtcNow;
            UpdateTrayText("Connected - ready to scan");
        }

        private void EnsureTargetHudVisible()
        {
            if (_currentTarget == null) return;

            // RC7 never changes visibility, window state, position, or TopMost
            // automatically. Target tracking continues regardless of how the
            // user manages the window.
            _targetHudActive = true;
        }

        private void UpdateTargetHud(ScanResult target)
        {
            if (target == null) return;

            RefreshTargetMetrics(target);
            _modeLabel.Text = "SAR TARGET LOCKED";
            _modeLabel.ForeColor = _accentGreen;

            string direction = GetDirectionWord(target.RelativeBearingDeg);
            Color directionColor = GetDirectionColor(direction);

            _directionLabel.Font = _headlineFontLarge;
            _directionLabel.Text = direction == "LEFT" ? "TURN LEFT" : direction == "RIGHT" ? "TURN RIGHT" : direction;
            _directionLabel.ForeColor = directionColor;

            if (direction == "AHEAD")
            {
                _turnLabel.Text = "Target nearly straight ahead";
            }
            else if (direction == "TURN AROUND")
            {
                _turnLabel.Text = "Target is behind you";
            }
            else
            {
                _turnLabel.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "Turn {0} {1:F0} deg",
                    direction == "LEFT" ? "left" : "right",
                    Math.Abs(target.RelativeBearingDeg));
            }

            _distanceLabel.Text = target.DistanceNm.ToString("F2", CultureInfo.InvariantCulture) + " NM";
            _distanceLabel.ForeColor = _textPrimary;
            _bearingLabel.Text = string.Format(CultureInfo.InvariantCulture, "TARGET BRG {0:F0} DEG", target.BearingDeg);
            _gauge.SetTarget(target.RelativeBearingDeg, directionColor);
            _copyCoordinatesButton.Enabled = true;
            UpdateConnectionState();
        }

        private void ApplyStandbyState(string headline, string detail)
        {
            if (_currentTarget != null) return;

            _targetHudActive = false;
            _gauge.ClearTarget();
            _modeLabel.Text = _connected ? "STANDBY" : "AUTO CONNECT";
            _modeLabel.ForeColor = _connected ? _accentGreen : _textMuted;
            _directionLabel.Text = headline;
            _directionLabel.Font = headline.Length > 12 ? _headlineFontSmall : _headlineFontLarge;
            _directionLabel.ForeColor = _connected ? _accentBlue : _textMuted;
            _turnLabel.Text = detail;
            _distanceLabel.Text = "--";
            _distanceLabel.ForeColor = _textMuted;
            _bearingLabel.Text = _connected
                ? (_searchAreaArmed ? "Searching for a SAR target" : "Scanning starts at the search area")
                : "MSFS can be opened before or after this app";
            _copyCoordinatesButton.Enabled = false;
            UpdateConnectionState();
        }

        private void RefreshTargetMetrics(ScanResult item)
        {
            if (!_hasUserState || item == null) return;
            item.DistanceNm = HaversineMeters(_userState.Latitude, _userState.Longitude, item.Latitude, item.Longitude) / MetersPerNauticalMile;
            item.BearingDeg = InitialBearingDegrees(_userState.Latitude, _userState.Longitude, item.Latitude, item.Longitude);
            item.RelativeBearingDeg = NormalizeSignedAngle(item.BearingDeg - _userState.Heading);
        }

        private void ToggleSettings()
        {
            _settingsVisible = !_settingsVisible;
            _settingsPanel.Visible = _settingsVisible;
            _settingsButton.Text = _settingsVisible ? "CLOSE" : "SETTINGS";
            ApplyWindowSize(_settingsVisible);

            if (_settingsVisible)
            {
                Show();
            }
        }

        private void ShowFromTray(bool openSettings)
        {
            if (_currentTarget == null) _standbySinceUtc = DateTime.UtcNow;

            // Opening from the tray is an explicit request to show the HUD again.
            if (_currentTarget != null)
            {
                _suppressedTargetObjectId = 0;
            }

            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            if (_preferTopMost)
            {
                EnforceTopMost();
            }

            if (openSettings && !_settingsVisible) ToggleSettings();
            if (!openSettings && _settingsVisible) ToggleSettings();
        }

        private void ApplyTopMostPreference(bool announce)
        {
            TopMost = _preferTopMost;
            EnforceTopMost();
            UpdateTopMostButtonStyle();

            if (announce)
            {
                SetStatus(_preferTopMost ? "Always on top enabled." : "Always on top disabled.");
            }
        }

        private void EnforceTopMost()
        {
            if (!IsHandleCreated) return;

            IntPtr insertAfter = _preferTopMost ? HwndTopMost : HwndNoTopMost;
            SetWindowPos(
                Handle,
                insertAfter,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpShowWindow | SwpNoActivate);
            TopMost = _preferTopMost;
        }

        private void UpdateTopMostButtonStyle()
        {
            if (_preferTopMost)
            {
                _topMostButton.BackColor = Color.FromArgb(14, 74, 106);
                _topMostButton.ForeColor = Color.FromArgb(120, 210, 255);
            }
            else
            {
                _topMostButton.BackColor = _headerColor;
                _topMostButton.ForeColor = _textMuted;
            }

            _topMostButton.Text = "ON TOP";
        }

        private void ToggleTopMost()
        {
            _preferTopMost = !_preferTopMost;
            ApplyTopMostPreference(true);
        }

        private void AutoHideIfIdle(DateTime now)
        {
            // Automatic window management is intentionally disabled in RC7.
        }

        private void CopyTargetCoordinates()
        {
            if (_currentTarget == null)
            {
                SetStatus("No target is locked.");
                return;
            }

            string coordinates = string.Format(
                CultureInfo.InvariantCulture,
                "{0:F6}, {1:F6}",
                _currentTarget.Latitude,
                _currentTarget.Longitude);
            Clipboard.SetText(coordinates);
            SetStatus("Target coordinates copied: " + coordinates);
        }

        private void UpdateConnectionState()
        {
            if (_currentTarget != null)
            {
                _connectionState.Text = "TARGET LOCKED";
                _connectionState.ForeColor = _accentGreen;
            }
            else if (_connected)
            {
                _connectionState.Text = _searchAreaArmed ? "SEARCH ACTIVE" : "CONNECTED";
                _connectionState.ForeColor = _searchAreaArmed ? _accentBlue : _accentGreen;
            }
            else
            {
                _connectionState.Text = "AUTO CONNECT";
                _connectionState.ForeColor = _accentAmber;
            }
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            _statusLabel.Text = text;
        }

        private void UpdateTrayText(string text)
        {
            string value = "MSFS SAR Locator - " + text;
            if (value.Length > 63) value = value.Substring(0, 63);
            _trayIcon.Text = value;
        }

        private void PositionAtTopRight()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 22, area.Top + 22);
        }

        private void CenterOnCurrentScreen()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        private void ApplyCompactSize()
        {
            ApplyWindowSize(false);
        }

        private void ApplyWindowSize(bool settingsOpen)
        {
            _root.RowStyles[0].SizeType = SizeType.Absolute;
            _root.RowStyles[0].Height = CompactHeaderHeight;
            _root.RowStyles[1].SizeType = SizeType.Absolute;
            _root.RowStyles[1].Height = CompactHudHeight;
            _root.RowStyles[2].SizeType = SizeType.Absolute;
            _root.RowStyles[2].Height = settingsOpen ? CompactSettingsPanelHeight : 0F;

            int clientHeight = CompactHeaderHeight + CompactHudHeight + CompactFormPadding;
            if (settingsOpen)
            {
                clientHeight += CompactSettingsPanelHeight;
            }

            MinimumSize = new Size(CompactWindowWidth, clientHeight);
            ClientSize = new Size(CompactWindowWidth, clientHeight);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            _pumpTimer.Stop();
            DisconnectInternal(false);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        private void MinimizeToTaskbar(bool userInitiated)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        }

        private void ConfigureHeaderButton(Button button, string text, int width)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 1, 0);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.UseCompatibleTextRendering = true;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = _headerColor;
            button.ForeColor = _textMuted;
            button.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            button.Cursor = Cursors.Hand;
            button.MouseEnter += delegate
            {
                button.BackColor = Color.FromArgb(44, 50, 59);
                button.ForeColor = _textPrimary;
            };
            button.MouseLeave += delegate
            {
                if (button == _topMostButton)
                {
                    UpdateTopMostButtonStyle();
                }
                else
                {
                    button.BackColor = _headerColor;
                    button.ForeColor = _textMuted;
                }
            };
        }

        private void ConfigureActionButton(Button button, string text)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Height = 24;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = _borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = _cardColor;
            button.ForeColor = _textPrimary;
            button.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 0, 4, 0);
        }

        private void ConfigureNumeric(NumericUpDown numeric, decimal min, decimal max, decimal value)
        {
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.Value = value;
            numeric.AutoSize = false;
            numeric.Dock = DockStyle.None;
            numeric.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            numeric.Size = new Size(148, 23);
            numeric.BackColor = _cardColor;
            numeric.ForeColor = _textPrimary;
            numeric.BorderStyle = BorderStyle.FixedSingle;
            numeric.Margin = Padding.Empty;
            numeric.UpDownAlign = LeftRightAlignment.Right;
        }

        private void ConfigureAutoPauseCheckBox(CheckBox box, string text, bool masterStyle)
        {
            box.Text = text;
            box.AutoSize = false;
            box.Dock = DockStyle.Fill;
            box.TextAlign = ContentAlignment.MiddleLeft;
            box.ForeColor = masterStyle ? _textPrimary : _textMuted;
            box.Font = new Font("Segoe UI", masterStyle ? 8F : 7.5F, FontStyle.Regular, GraphicsUnit.Point);
            box.Margin = Padding.Empty;
            box.Padding = Padding.Empty;
        }

        private Control CreateSettingsField(string caption, NumericUpDown numeric, bool lastColumn)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Margin = lastColumn ? Padding.Empty : new Padding(0, 0, 10, 0);
            field.Padding = Padding.Empty;

            Label label = new Label();
            label.Text = caption;
            label.AutoSize = false;
            label.Location = new Point(0, 0);
            label.Size = new Size(160, 16);
            label.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = _textMuted;
            label.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);

            numeric.Location = new Point(0, 18);

            field.Controls.Add(label);
            field.Controls.Add(numeric);
            field.Resize += delegate
            {
                int width = Math.Max(96, field.ClientSize.Width);
                label.Width = width;
                numeric.SetBounds(0, 18, width, 23);
            };

            return field;
        }

        private Color GetDirectionColor(string direction)
        {
            if (direction == "AHEAD") return _accentGreen;
            if (direction == "TURN AROUND") return _accentAmber;
            return _accentBlue;
        }

        private static bool IsImmediateSarTarget(ScanResult item)
        {
            if (item == null || item.Excluded) return false;

            string value = ((item.Title ?? string.Empty) + " " + (item.Category ?? string.Empty)).ToLowerInvariant();

            // Confirmed from live Career testing.
            if (value.Contains("car bush passengers") || value.Contains("bush passengers")) return true;

            // Explicit SAR semantics are stronger than generic vehicle words.
            if (value.Contains("sar target") || value.Contains("search rescue")) return true;
            if (value.Contains("rescue victim") || value.Contains("sar victim")) return true;
            if (value.Contains("missing person") || value.Contains("missing passenger")) return true;
            if (value.Contains("stranded person") || value.Contains("survivor")) return true;
            if (value.Contains("wreck") || value.Contains("crash victim")) return true;

            return false;
        }

        private void UpdateScanHud()
        {
            if (!_searchAreaArmed || _currentTarget != null) return;

            string source = _activeScanMode == ScanMode.Ground ? "GROUND" : "ALL";
            _modeLabel.Text = "MANUAL SCAN";
            _modeLabel.ForeColor = _accentGreen;
            _directionLabel.Text = "SCANNING";
            _directionLabel.Font = _headlineFontLarge;
            _directionLabel.ForeColor = _accentBlue;
            _gauge.ClearTarget();
            _turnLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} scan #{1} | {2} object(s)",
                source,
                _scanSequence,
                _currentScanObjectCount);
            _distanceLabel.Text = "--";
            _bearingLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Radius {0:0} NM | Manual SAR scan",
                _radiusNm.Value);
            SetStatus(string.Format(
                CultureInfo.InvariantCulture,
                "Manual {0} scan: {1} object(s) received.",
                source,
                _currentScanObjectCount));
        }

        private static bool IsStrongCandidate(ScanResult item)
        {
            if (item == null || item.Excluded) return false;
            string text = ((item.Title ?? string.Empty) + " " + (item.Category ?? string.Empty)).ToLowerInvariant();

            if (text.Contains("car bush passengers") || text.Contains("bush passengers")) return true;
            if (text.Contains("sar") || text.Contains("rescue") || text.Contains("victim") || text.Contains("survivor")) return true;
            if (text.Contains("missing") || text.Contains("stranded") || text.Contains("wreck") || text.Contains("crash")) return true;
            if (text.Contains("hiker") && item.DistanceNm <= 8.0) return true;

            // Generic words such as car, truck, ambulance, or vehicle are intentionally
            // not sufficient on their own.
            return item.CandidateScore >= StrongTargetScore;
        }

        private static int ScoreCandidate(ScanResult item)
        {
            string text = ((item.Title ?? string.Empty) + " " + (item.Category ?? string.Empty)).ToLowerInvariant();
            int score = 0;

            string[] strong =
            {
                "sar", "rescue", "search rescue", "wreck", "crash", "accident", "missing",
                "victim", "stranded", "survivor", "hiker", "bush passengers"
            };

            string[] medium =
            {
                "car", "truck", "vehicle", "pickup", "suv", "jeep", "van",
                "human", "person", "passenger", "civilian"
            };

            foreach (string keyword in strong)
            {
                if (text.Contains(keyword)) score += 30;
            }

            foreach (string keyword in medium)
            {
                if (text.Contains(keyword)) score += 6;
            }

            if (text.Contains("car") && text.Contains("passenger")) score += 16;
            if (text.Contains("groundvehicle")) score += 2;

            // Distance is only a tie-breaker. A nearby generic vehicle must never become
            // a SAR target purely because it is close to the aircraft.
            if (item.DistanceNm <= 1.0) score += 3;
            else if (item.DistanceNm <= 2.0) score += 2;
            else if (item.DistanceNm <= 5.0) score += 1;

            return score;
        }

        private static bool ShouldExclude(ScanResult item, out string reason)
        {
            string text = ((item.Title ?? string.Empty) + " " + (item.Category ?? string.Empty)).ToLowerInvariant();

            string[] protectedTerms =
            {
                "car bush passengers", "bush passengers", "sar", "rescue", "victim", "survivor",
                "missing", "stranded", "wreck", "crash", "hiker"
            };

            foreach (string term in protectedTerms)
            {
                if (text.Contains(term))
                {
                    reason = null;
                    return false;
                }
            }

            string[] excludedTerms =
            {
                "fuel", "baggage", "pushback", "push-back", "tug", "catering", "belt loader", "beltloader",
                "lavatory", "deice", "de-ice", "marshaller", "follow me", "jetway", "airport service",
                "stairs", "stair truck", "ground power", "gpu cart", "tow tractor", "forklift", "service truck",
                "ambulance", "fire truck", "firetruck", "police car", "crew car"
            };

            foreach (string term in excludedTerms)
            {
                if (text.Contains(term))
                {
                    reason = term;
                    return true;
                }
            }

            if (text.Contains("aircraft") || text.Contains("airplane") || text.Contains("helicopter"))
            {
                reason = "aircraft";
                return true;
            }

            reason = null;
            return false;
        }

        private void TrackFlightSession(UserStateData state)
        {
            DateTime now = DateTime.UtcNow;

            if (!_sessionAnchorSet)
            {
                BeginFlightSession(state);
            }
            else if (_hasPreviousUserPoint)
            {
                double elapsed = (now - _previousUserPointUtc).TotalSeconds;
                if (elapsed > 0.0 && elapsed <= 5.0)
                {
                    double jumpNm = HaversineMeters(
                        _previousUserLatitude,
                        _previousUserLongitude,
                        state.Latitude,
                        state.Longitude) / MetersPerNauticalMile;

                    if (jumpNm >= SessionJumpResetNm)
                    {
                        if (_currentTarget != null)
                        {
                            // A large jump with a locked target is a travel skip
                            // (Alt+N) inside the same mission, not a new session.
                            // Keep the lock and continue guidance immediately.
                            ReanchorFlightSession(state);
                        }
                        else
                        {
                            BeginFlightSession(state);
                        }
                    }
                }
            }

            _previousUserLatitude = state.Latitude;
            _previousUserLongitude = state.Longitude;
            _previousUserPointUtc = now;
            _hasPreviousUserPoint = true;
        }

        private void ReanchorFlightSession(UserStateData state)
        {
            _sessionAnchorLatitude = state.Latitude;
            _sessionAnchorLongitude = state.Longitude;
            _startupZoneEnabled = false;
            _flightSessionStartedUtc = DateTime.UtcNow;

            // The mission was already in progress, so scanning stays armed and
            // the scan button stays usable without another gate crossing.
            _searchAreaArmed = true;
            UpdateConnectionState();
            SetStatus("Travel skip detected. SAR target lock kept.");
        }

        private void BeginFlightSession(UserStateData state)
        {
            _sessionAnchorSet = true;
            _sessionAnchorLatitude = state.Latitude;
            _sessionAnchorLongitude = state.Longitude;
            _startupZoneEnabled = state.SimOnGround != 0;
            _searchAreaArmed = false;
            _searchGateApproachObserved = false;
            _largestObservedNavDistanceNm = 0.0;
            _flightSessionStartedUtc = DateTime.UtcNow;
            _lastScanCompletedUtc = DateTime.UtcNow;
            _scanPending = false;
            _scanBuffer.Clear();
            _lastObjectCallbackUtc = DateTime.MinValue;
            _currentScanObjectCount = 0;
            _scanSequence = 0;
            _startupIgnoredObjectIds.Clear();
            _suppressedTargetObjectId = 0;
            _scanNowButton.Enabled = false;
            _mainScanButton.Enabled = false;
            ResetCandidateConfirmation();

            if (_currentTarget != null)
            {
                _currentTarget = null;
                _targetHudActive = false;
                _positionedForTarget = false;
            }

            SetStatus(_startupZoneEnabled
                ? "New mission session detected. Scanning is paused until the search area is reached."
                : "New airborne session detected. Waiting for the search-area navigation gate.");
            _standbySinceUtc = DateTime.UtcNow;
        }

        private void ResetSessionTracking()
        {
            _sessionAnchorSet = false;
            _startupZoneEnabled = false;
            _searchAreaArmed = false;
            _searchGateApproachObserved = false;
            _largestObservedNavDistanceNm = 0.0;
            _hasNavigationState = false;
            _flightSessionStartedUtc = DateTime.MinValue;
            _sessionAnchorLatitude = 0.0;
            _sessionAnchorLongitude = 0.0;
            _hasPreviousUserPoint = false;
            _previousUserLatitude = 0.0;
            _previousUserLongitude = 0.0;
            _previousUserPointUtc = DateTime.MinValue;
            _scanPending = false;
            _scanBuffer.Clear();
            _lastObjectCallbackUtc = DateTime.MinValue;
            _currentScanObjectCount = 0;
            _scanSequence = 0;
            _startupIgnoredObjectIds.Clear();
            _scanNowButton.Enabled = false;
            _mainScanButton.Enabled = false;
            ResetCandidateConfirmation();
        }

        private void EvaluateSearchAreaGate()
        {
            if (_searchAreaArmed || !_connected || !_hasUserState || !_hasNavigationState || !_sessionAnchorSet) return;

            double waypointDistanceNm = GetWaypointDistanceNm();
            double targetDistanceNm = GetGpsTargetDistanceNm();

            double distanceFromStartNm = HaversineMeters(
                _sessionAnchorLatitude,
                _sessionAnchorLongitude,
                _userState.Latitude,
                _userState.Longitude) / MetersPerNauticalMile;

            bool departedStart = distanceFromStartNm >= MinimumDepartureDistanceNm || _userState.SimOnGround == 0;
            if (!departedStart) return;

            double sessionAgeSeconds = _flightSessionStartedUtc == DateTime.MinValue
                ? 0.0
                : (DateTime.UtcNow - _flightSessionStartedUtc).TotalSeconds;
            if (sessionAgeSeconds < MinimumSearchGateSessionAgeSeconds) return;

            string waypointId = (_navigationState.NextWaypointId ?? string.Empty).Trim();
            bool semanticSearchWaypoint = IsSearchAreaWaypointId(waypointId);

            int waypointCount = (int)Math.Round(_navigationState.FlightPlanWaypointCount);
            int waypointIndex = (int)Math.Round(_navigationState.FlightPlanWaypointIndex);
            bool appearsToBeFinalFlightPlanWaypoint = _navigationState.IsActiveFlightPlan != 0 &&
                                                      waypointCount > 0 &&
                                                      waypointIndex >= Math.Max(0, waypointCount - 1);

            double bestDistanceNm = GetBestNavigationDistanceNm(waypointDistanceNm, targetDistanceNm);
            if (double.IsNaN(bestDistanceNm) || bestDistanceNm <= 0.0) return;

            double gateRadiusNm = (double)_searchGateRadiusNm.Value;
            double observationThresholdNm = Math.Max(MinimumGateObservationNm, gateRadiusNm * 2.0);

            if (bestDistanceNm > _largestObservedNavDistanceNm)
            {
                _largestObservedNavDistanceNm = bestDistanceNm;
            }

            if (bestDistanceNm >= observationThresholdNm)
            {
                _searchGateApproachObserved = true;
            }

            bool withinGate = bestDistanceNm <= gateRadiusNm;
            if (!withinGate) return;

            // Highest-confidence cases: the simulator exposes a recognizable
            // search-area waypoint or this is the final flight-plan waypoint.
            if (semanticSearchWaypoint)
            {
                ArmSearchArea("search-area waypoint");
                return;
            }

            if (appearsToBeFinalFlightPlanWaypoint)
            {
                ArmSearchArea("final active GPS waypoint");
                return;
            }

            // Career mission objective markers are not always exposed as a normal
            // active flight-plan waypoint. If we observed the navigation target
            // from farther away and then actually crossed into the configured gate,
            // treat that crossing as the search-area entry.
            if (_searchGateApproachObserved)
            {
                ArmSearchArea("navigation target gate crossing");
            }
        }

        private void ArmSearchArea(string reason)
        {
            if (_searchAreaArmed) return;

            _searchAreaArmed = true;
            UpdateConnectionState();

            if (_currentTarget == null && !_scanPending)
            {
                ApplyStandbyState("READY TO SCAN", "Search Area detected. Click SCAN FOR TARGET below.");
                _mainScanButton.Enabled = _connected && _hasUserState;
                _scanNowButton.Enabled = _connected && _hasUserState;
                SetStatus("Search Area detected. Waiting for a manual scan.");
                UpdateTrayText("Ready for manual scan");
            }
        }

        private double GetWaypointDistanceNm()
        {
            if (!_hasNavigationState) return double.NaN;
            if (double.IsNaN(_navigationState.WaypointDistanceMeters) || _navigationState.WaypointDistanceMeters <= 0.0) return double.NaN;
            return _navigationState.WaypointDistanceMeters / MetersPerNauticalMile;
        }

        private double GetGpsTargetDistanceNm()
        {
            if (!_hasNavigationState) return double.NaN;
            if (double.IsNaN(_navigationState.TargetDistanceMeters) || _navigationState.TargetDistanceMeters <= 0.0) return double.NaN;
            return _navigationState.TargetDistanceMeters / MetersPerNauticalMile;
        }

        private static double GetBestNavigationDistanceNm(double waypointDistanceNm, double targetDistanceNm)
        {
            bool hasWaypoint = !double.IsNaN(waypointDistanceNm) && waypointDistanceNm > 0.0;
            bool hasTarget = !double.IsNaN(targetDistanceNm) && targetDistanceNm > 0.0;

            if (hasWaypoint && hasTarget) return Math.Min(waypointDistanceNm, targetDistanceNm);
            if (hasWaypoint) return waypointDistanceNm;
            if (hasTarget) return targetDistanceNm;
            return double.NaN;
        }

        private static bool IsSearchAreaWaypointId(string waypointId)
        {
            if (string.IsNullOrWhiteSpace(waypointId)) return false;
            string text = waypointId.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            return text.Contains("search") || text.Contains("searcharea") || text.Contains("search area") ||
                   text.Contains("sar") || text.Contains("rescue area") || text.Contains("rescue search");
        }

        private bool IsStartupZoneObject(ScanResult item)
        {
            if (!_sessionAnchorSet || !_startupZoneEnabled || item == null) return false;

            double distanceFromStartNm = HaversineMeters(
                _sessionAnchorLatitude,
                _sessionAnchorLongitude,
                item.Latitude,
                item.Longitude) / MetersPerNauticalMile;

            return distanceFromStartNm <= StartupZoneRadiusNm;
        }

        private bool ConfirmCandidate(ScanResult candidate)
        {
            if (candidate == null)
            {
                ResetCandidateConfirmation();
                return false;
            }

            if (_pendingCandidateId == candidate.ObjectId)
            {
                _pendingCandidateConfirmations++;
            }
            else
            {
                _pendingCandidateId = candidate.ObjectId;
                _pendingCandidateConfirmations = 1;
            }

            if (_pendingCandidateConfirmations >= CandidateConfirmationRequired)
            {
                return true;
            }

            SetStatus(string.Format(
                CultureInfo.InvariantCulture,
                "Confirming SAR target... {0}/{1}",
                _pendingCandidateConfirmations,
                CandidateConfirmationRequired));
            return false;
        }

        private void ResetCandidateConfirmation()
        {
            _pendingCandidateId = 0;
            _pendingCandidateConfirmations = 0;
        }

        private static string GetDirectionWord(double relative)
        {
            double absolute = Math.Abs(relative);
            if (absolute <= 8.0) return "AHEAD";
            if (Math.Abs(absolute - 180.0) <= 12.0) return "TURN AROUND";
            return relative > 0.0 ? "RIGHT" : "LEFT";
        }

        private static string FormatRelative(double relative)
        {
            if (Math.Abs(relative) <= 8.0) return "Ahead";
            if (Math.Abs(Math.Abs(relative) - 180.0) <= 12.0) return "Behind";
            return (relative > 0.0 ? "R " : "L ") + Math.Abs(relative).ToString("F0", CultureInfo.InvariantCulture) + " deg";
        }

        private static bool IsValidCoordinate(double latitude, double longitude)
        {
            return !double.IsNaN(latitude) && !double.IsNaN(longitude) &&
                   latitude >= -90.0 && latitude <= 90.0 &&
                   longitude >= -180.0 && longitude <= 180.0 &&
                   !(Math.Abs(latitude) < 0.000001 && Math.Abs(longitude) < 0.000001);
        }

        private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = DegreesToRadians(lat1);
            double phi2 = DegreesToRadians(lat2);
            double dPhi = DegreesToRadians(lat2 - lat1);
            double dLambda = DegreesToRadians(lon2 - lon1);

            double a = Math.Sin(dPhi / 2.0) * Math.Sin(dPhi / 2.0) +
                       Math.Cos(phi1) * Math.Cos(phi2) *
                       Math.Sin(dLambda / 2.0) * Math.Sin(dLambda / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return EarthRadiusMeters * c;
        }

        private static double InitialBearingDegrees(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = DegreesToRadians(lat1);
            double phi2 = DegreesToRadians(lat2);
            double lambda = DegreesToRadians(lon2 - lon1);

            double y = Math.Sin(lambda) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) -
                       Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(lambda);

            return NormalizeAngle(RadiansToDegrees(Math.Atan2(y, x)));
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= 360.0;
            if (angle < 0.0) angle += 360.0;
            return angle;
        }

        private static double NormalizeSignedAngle(double angle)
        {
            angle = NormalizeAngle(angle);
            if (angle > 180.0) angle -= 360.0;
            return angle;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }
    }
}
