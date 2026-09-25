using System.Drawing.Drawing2D;
using Timer = System.Windows.Forms.Timer;

namespace WindowCloner
{
    public partial class RectangleSelector : Form
    {
        private Bitmap _screenshot;
        private Rectangle _selectionRect;
        private Point _selectionStart;
        private bool _isSelecting = false;
        private bool _hasSelection = false;
        private Rectangle _initialSelection;

        private IntPtr _targetHwnd;
        private string _targetWindowTitle, _processName;
        private bool _onlyWindowName;

        private Label _lblLayoutWarning;

        private Timer _resizeWatchTimer;
        private NativeMethods.RECT _lastKnownWindowRect;
        private string _lastLayoutName;
        private bool _rebuilding;
        private DateTime _lastSizeChangeUtc = DateTime.MinValue;
        private const int ResizeWatchIntervalMs = 750;
        private const int ResizeSettleMs = 400;

        public Rectangle SelectedRect { get; private set; }

        public WindowLayoutObject SelectedLayout { get; private set; }

        private const int MIN_PANEL_CONTENT_WIDTH = 1262;

        private static readonly Font _sizeFont = new Font("Segoe UI", 11, FontStyle.Bold);
        private static readonly Font _warningFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        private static readonly Pen _selectionPen = new Pen(Color.FromArgb(255, 0, 120, 255), 3)
        {
            DashStyle = DashStyle.Dash
        };
        private static readonly Brush _selectionHandleBrush = new SolidBrush(Color.FromArgb(255, 0, 120, 255));
        private static readonly Brush _sizeTextBrush = new SolidBrush(Color.White);
        private static readonly Brush _sizeBgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        private static readonly Brush _shadeBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));

        private int _cachedDx = -1;
        private int _cachedDy = -1;

        private string _lastWarningText;
        private Rectangle _lastWarningArea;

        private readonly List<WindowLayoutObject> _filteredScratch = new List<WindowLayoutObject>(64);

        private Control[] _focusOrder;

        private bool _pictureBoxKeyboardMode;

        private Control _lastFocusedControl;

        public RectangleSelector()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.DoubleBuffered = true;
            this.BackColor = Color.Black;

            this.chkOnlyWindowName.CheckedChanged += ChkOnlyWindowName_CheckedChanged;
            cmbLayout.SelectedIndexChanged += CmbLayout_SelectedIndexChanged;
            btnRemoveLayout.Click += BtnRemoveLayout_Click;

            CreateWarningLabel();
            SetupKeyboardNavigation();
        }

        public RectangleSelector(IntPtr targetHwnd, string windowTitle, string processName, Bitmap screenshot)
        {
            _targetHwnd = targetHwnd;
            _targetWindowTitle = windowTitle;
            _processName = processName;
            _screenshot = screenshot;

            InitializeComponent();

            Text = $"Select Region - {windowTitle}";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            DoubleBuffered = true;
            BackColor = Color.Black;

            panel1.Dock = DockStyle.Bottom;
            pictureBox1.Dock = DockStyle.Fill;

            ApplyMinimumClientSize();

            pictureBox1.Image = _screenshot;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.MouseDown += PictureBox_MouseDown;
            pictureBox1.MouseMove += PictureBox_MouseMove;
            pictureBox1.MouseUp += PictureBox_MouseUp;
            pictureBox1.Paint += PictureBox_Paint;

            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
            btnConfirm.Enabled = false;

            chkOnlyWindowName.CheckedChanged += ChkOnlyWindowName_CheckedChanged;
            cmbLayout.SelectedIndexChanged += CmbLayout_SelectedIndexChanged;
            btnRemoveLayout.Click += BtnRemoveLayout_Click;

            CreateWarningLabel();

            if (screenshot != null)
            {
                ApplyScreenshotBasedSize(screenshot);
            }
            else
            {
                Size = ComputeWindowSize(null);
            }

            if (_screenshot != null)
            {
                _selectionRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                _initialSelection = _selectionRect;
                _hasSelection = true;
                btnConfirm.Enabled = true;
                UpdateInfoText();
            }

            InitializeHistory();
            LoadLayouts();

            SnapshotTargetWindowRect();

            StartResizeWatcher();

            SetupKeyboardNavigation();
        }

        private void SetupKeyboardNavigation()
        {
            _focusOrder = new Control[]
            {
                pictureBox1,
                cmbLayout,
                chkOnlyWindowName,
                btnRemoveLayout,
                btnConfirm,
                btnCancel
            };

            pictureBox1.TabStop = true;
            pictureBox1.KeyDown += PictureBox_KeyDown;
            pictureBox1.GotFocus += PictureBox_GotFocus;
            pictureBox1.LostFocus += PictureBox_LostFocus;

            cmbLayout.KeyDown += ComboOrCheckbox_KeyDown;
            chkOnlyWindowName.KeyDown += ComboOrCheckbox_KeyDown;
            btnRemoveLayout.KeyDown += Button_KeyDown;
            btnConfirm.KeyDown += Button_KeyDown;
            btnCancel.KeyDown += Button_KeyDown;

            this.AcceptButton = null;

            foreach (Control c in _focusOrder)
            {
                if (c == null)
                {
                    continue;
                }

                c.GotFocus += (s, e) =>
                {
                    if (c != pictureBox1)
                    {
                        _lastFocusedControl = c;
                    }
                };
            }
        }

        private void PictureBox_GotFocus(object sender, EventArgs e)
        {
            _pictureBoxKeyboardMode = true;
            pictureBox1.Invalidate();
        }

        private void PictureBox_LostFocus(object sender, EventArgs e)
        {
            _pictureBoxKeyboardMode = false;
            pictureBox1.Invalidate();
        }

        private void PictureBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (_screenshot == null)
            {
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (btnConfirm.Enabled)
                {
                    BtnConfirm_Click(null, null);
                }
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BtnCancel_Click(null, null);
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                _selectionRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                _initialSelection = _selectionRect;
                _hasSelection = true;
                btnConfirm.Enabled = true;
                UpdateInfoText();
                pictureBox1.Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Tab)
            {
                _pictureBoxKeyboardMode = false;
                return;
            }

            bool isArrow =
                e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                e.KeyCode == Keys.Up || e.KeyCode == Keys.Down;

            if (!isArrow)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;

            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;

            int step = ctrl ? 10 : 1;

            if (!_hasSelection || _selectionRect.IsEmpty)
            {
                _selectionRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                _initialSelection = _selectionRect;
                _hasSelection = true;
            }

            int left = _selectionRect.Left;
            int top = _selectionRect.Top;
            int right = _selectionRect.Right;
            int bottom = _selectionRect.Bottom;

            if (shift)
            {
                switch (e.KeyCode)
                {
                    case Keys.Left:
                        right = Math.Max(left + MIN_SELECTION_SIZE, right - step);
                        break;
                    case Keys.Right:
                        right = Math.Min(_screenshot.Width, right + step);
                        break;
                    case Keys.Up:
                        bottom = Math.Max(top + MIN_SELECTION_SIZE, bottom - step);
                        break;
                    case Keys.Down:
                        bottom = Math.Min(_screenshot.Height, bottom + step);
                        break;
                }
            }
            else
            {
                int dx = 0, dy = 0;
                switch (e.KeyCode)
                {
                    case Keys.Left:
                        dx = -step;
                        break;
                    case Keys.Right:
                        dx = step;
                        break;
                    case Keys.Up:
                        dy = -step;
                        break;
                    case Keys.Down:
                        dy = step;
                        break;
                }

                int newLeft = left + dx;
                int newTop = top + dy;

                if (newLeft < 0)
                {
                    newLeft = 0;
                }
                if (newTop < 0)
                {
                    newTop = 0;
                }
                if (newLeft + (right - left) > _screenshot.Width)
                {
                    newLeft = _screenshot.Width - (right - left);
                }
                if (newTop + (bottom - top) > _screenshot.Height)
                {
                    newTop = _screenshot.Height - (bottom - top);
                }

                left = newLeft;
                top = newTop;
                right = left + (right - left);
                bottom = top + (bottom - top);
            }

            _selectionRect = Rectangle.FromLTRB(left, top, right, bottom);
            _hasSelection = true;
            btnConfirm.Enabled = _selectionRect.Width > 5 && _selectionRect.Height > 5;
            UpdateInfoText();
            pictureBox1.Invalidate();
        }

        private void ComboOrCheckbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
            {
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                MoveFocus(-1);
            }
            else if (e.KeyCode == Keys.Right)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                MoveFocus(+1);
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (sender == chkOnlyWindowName)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    chkOnlyWindowName.Checked = !chkOnlyWindowName.Checked;
                }
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (sender == chkOnlyWindowName)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    chkOnlyWindowName.Checked = !chkOnlyWindowName.Checked;
                }
            }
        }

        private void Button_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
            {
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                MoveFocus(-1);
            }
            else if (e.KeyCode == Keys.Right)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                MoveFocus(+1);
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                MoveFocus(e.KeyCode == Keys.Up ? -1 : +1);
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                if (sender is Button btn && btn.Enabled)
                {
                    btn.PerformClick();
                }
            }
        }

        private void MoveFocus(int delta)
        {
            if (_focusOrder == null || _focusOrder.Length == 0)
            {
                return;
            }

            int currentIndex = -1;
            Control current = this.ActiveControl;

            for (int i = 0; i < _focusOrder.Length; i++)
            {
                if (_focusOrder[i] == current)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            int n = _focusOrder.Length;
            int idx = currentIndex;

            for (int tries = 0; tries < n; tries++)
            {
                idx = (idx + delta) % n;
                if (idx < 0)
                {
                    idx += n;
                }

                Control candidate = _focusOrder[idx];
                if (candidate == null)
                {
                    continue;
                }
                if (!candidate.Visible)
                {
                    continue;
                }
                if (!candidate.Enabled)
                {
                    continue;
                }

                candidate.Focus();
                _lastFocusedControl = candidate;
                return;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateCachedBorders();
        }

        private void SnapshotTargetWindowRect()
        {
            if (_targetHwnd != IntPtr.Zero &&
                NativeMethods.GetWindowRect(_targetHwnd, out NativeMethods.RECT r))
            {
                _lastKnownWindowRect = r;
            }
        }

        private void StartResizeWatcher()
        {
            if (_targetHwnd == IntPtr.Zero)
            {
                return;
            }

            _resizeWatchTimer = new Timer { Interval = ResizeWatchIntervalMs };
            _resizeWatchTimer.Tick += ResizeWatchTimer_Tick;
            _resizeWatchTimer.Start();
        }

        private void StopResizeWatcher()
        {
            if (_resizeWatchTimer != null)
            {
                _resizeWatchTimer.Stop();
                _resizeWatchTimer.Tick -= ResizeWatchTimer_Tick;
                _resizeWatchTimer.Dispose();
                _resizeWatchTimer = null;
            }
        }

        private void ResizeWatchTimer_Tick(object sender, EventArgs e)
        {
            if (_rebuilding || _targetHwnd == IntPtr.Zero)
            {
                return;
            }

            if (!NativeMethods.GetWindowRect(_targetHwnd, out NativeMethods.RECT current))
            {
                return;
            }

            if (current.Width != _lastKnownWindowRect.Width ||
                current.Height != _lastKnownWindowRect.Height)
            {
                _lastKnownWindowRect = current;
                _lastSizeChangeUtc = DateTime.UtcNow;
                return;
            }

            if (_lastSizeChangeUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastSizeChangeUtc).TotalMilliseconds >= ResizeSettleMs)
            {
                _lastSizeChangeUtc = DateTime.MinValue;
                RebuildFromTargetWindow();
            }
        }

        private void RebuildFromTargetWindow()
        {
            if (_targetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_targetHwnd))
            {
                return;
            }

            _rebuilding = true;
            try
            {
                _lastLayoutName = (cmbLayout.SelectedIndex > 0)
                    ? cmbLayout.SelectedItem as string
                    : null;

                Bitmap newShot = WindowCapture32.CaptureWindow(_targetHwnd);
                if (newShot == null)
                {
                    return;
                }

                var oldImage = pictureBox1.Image;
                pictureBox1.Image = null;
                oldImage?.Dispose();
                oldImage = null;

                _screenshot = newShot;
                pictureBox1.Image = _screenshot;

                ApplyScreenshotBasedSize(_screenshot);

                _selectionRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                _initialSelection = _selectionRect;
                _hasSelection = true;
                btnConfirm.Enabled = true;
                UpdateInfoText();
                pictureBox1.Invalidate();

                InitializeHistory();

                LoadLayouts();
                if (!string.IsNullOrEmpty(_lastLayoutName))
                {
                    int idx = cmbLayout.Items.IndexOf(_lastLayoutName);
                    if (idx > 0)
                    {
                        cmbLayout.SelectedIndex = idx;
                    }
                }

                _lastWarningText = null;
                CenterWarningLabel();
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private void CreateWarningLabel()
        {
            _lblLayoutWarning = new Label();
            _lblLayoutWarning.AutoSize = false;
            _lblLayoutWarning.BackColor = Color.Black;
            _lblLayoutWarning.ForeColor = Color.Red;
            _lblLayoutWarning.Font = _warningFont;
            _lblLayoutWarning.TextAlign = ContentAlignment.MiddleCenter;
            _lblLayoutWarning.Padding = new Padding(6, 4, 6, 4);
            _lblLayoutWarning.Text = "";
            _lblLayoutWarning.Visible = false;

            _lblLayoutWarning.Enabled = true;
            _lblLayoutWarning.TabStop = false;

            this.Controls.Add(_lblLayoutWarning);
            _lblLayoutWarning.BringToFront();

            CenterWarningLabel();
        }

        private void CenterWarningLabel()
        {
            if (_lblLayoutWarning == null)
            {
                return;
            }

            string text = _lblLayoutWarning.Text ?? string.Empty;
            if (text.Length == 0)
            {
                return;
            }

            if (_lastWarningText == text && _lastWarningArea == ClientRectangle)
            {
                return;
            }
            _lastWarningText = text;
            _lastWarningArea = ClientRectangle;

            const int MAX_WARNING_WIDTH = 300;

            Size measured = TextRenderer.MeasureText(
                text,
                _lblLayoutWarning.Font,
                new Size(MAX_WARNING_WIDTH, int.MaxValue),
                TextFormatFlags.WordBreak |
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);

            int w = Math.Min(
                MAX_WARNING_WIDTH,
                measured.Width + _lblLayoutWarning.Padding.Horizontal);

            int h =
                measured.Height +
                _lblLayoutWarning.Padding.Vertical;

            w = Math.Max(w, 180);
            h = Math.Max(h, 24);

            _lblLayoutWarning.Size = new Size(w, h);

            int panelH = (panel1 != null && panel1.Visible) ? panel1.Height : 0;
            int areaW = this.ClientSize.Width;
            int areaH = Math.Max(1, this.ClientSize.Height - panelH);

            _lblLayoutWarning.Left = Math.Max(0, (areaW - w) / 2);
            _lblLayoutWarning.Top = Math.Max(0, (areaH - h) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CenterWarningLabel();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterWarningLabel();

            if (pictureBox1 != null && pictureBox1.Visible && pictureBox1.Enabled)
            {
                pictureBox1.Focus();
                _pictureBoxKeyboardMode = true;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopResizeWatcher();

            if (_lblLayoutWarning != null)
            {
                _lblLayoutWarning.Dispose();
                _lblLayoutWarning = null;
            }

            base.OnFormClosed(e);
        }

        private void ShowLayoutWarning(string text)
        {
            if (_lblLayoutWarning == null)
            {
                return;
            }

            _lblLayoutWarning.Text = text ?? "";
            _lblLayoutWarning.Visible = !string.IsNullOrEmpty(text);

            _lastWarningText = null;
            CenterWarningLabel();

            _lblLayoutWarning.BringToFront();
            _lblLayoutWarning.Invalidate();
        }

        private void HideLayoutWarning()
        {
            if (_lblLayoutWarning == null)
            {
                return;
            }

            _lblLayoutWarning.Visible = false;
        }

        private void CmbLayout_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateRemoveLayoutButtonVisibility();

            if (cmbLayout.SelectedIndex <= 0)
            {
                HideLayoutWarning();

                if (_screenshot != null)
                {
                    _selectionRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                    _initialSelection = _selectionRect;
                    _hasSelection = true;
                    btnConfirm.Enabled = true;
                    UpdateInfoText();
                    pictureBox1.Invalidate();
                }
                return;
            }

            string selectedName = cmbLayout.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName))
            {
                HideLayoutWarning();
                return;
            }

            if (!Program.HasLayoutOfName(selectedName, out WindowLayoutObject wlo) || wlo == null)
            {
                HideLayoutWarning();
                return;
            }

            if (_screenshot == null)
            {
                HideLayoutWarning();
                return;
            }

            int sw = _screenshot.Width;
            int sh = _screenshot.Height;

            int screenLeft = 0;
            int screenTop = 0;

            int localX = wlo.rectX - screenLeft;
            int localY = wlo.rectY - screenTop;

            int x = Math.Max(0, Math.Min(localX, sw - 1));
            int y = Math.Max(0, Math.Min(localY, sh - 1));

            int maxWidth = Math.Max(1, sw - x);
            int maxHeight = Math.Max(1, sh - y);

            int cw = wlo.rectW > 0 ? wlo.rectW : wlo.width;
            int ch = wlo.rectH > 0 ? wlo.rectH : wlo.height;

            int width = Math.Max(1, Math.Min(cw, maxWidth));
            int height = Math.Max(1, Math.Min(ch, maxHeight));

            if (width <= 1 && height <= 1)
            {
                MessageBox.Show(
                    "Computed selection is 1x1. This almost always means wlo.x/y " +
                    "are in screen coordinates, not screenshot-local coordinates, " +
                    "or a DPI mismatch exists.",
                    "Layout Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _selectionRect = new Rectangle(x, y, width, height);
            _initialSelection = _selectionRect;
            _hasSelection = true;
            btnConfirm.Enabled = true;

            UpdateLayoutMatchWarning(wlo);

            UpdateInfoText();
            pictureBox1.Invalidate();
        }

        private void UpdateLayoutMatchWarning(WindowLayoutObject wlo)
        {
            if (wlo == null || _targetHwnd == IntPtr.Zero)
            {
                HideLayoutWarning();
                return;
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(_targetHwnd, out rect))
            {
                HideLayoutWarning();
                return;
            }

            int actualWidth = rect.Width;
            int actualHeight = rect.Height;

            int suitedWidth = wlo.bestSuitedForWidth;
            int suitedHeight = wlo.bestSuitedForHeight;

            if (suitedWidth <= 0 || suitedHeight <= 0)
            {
                HideLayoutWarning();
                return;
            }

            if (actualWidth != suitedWidth || actualHeight != suitedHeight)
            {
                string msg =
                    $"WINDOW SIZE MISMATCH" + Environment.NewLine +
                    $"Window: {actualWidth} × {actualHeight}" + Environment.NewLine +
                    $"Layout expects: {suitedWidth} × {suitedHeight}" + Environment.NewLine +
                    Environment.NewLine +
                    "Please, resize the source window";
                ShowLayoutWarning(msg);
            }
            else
            {
                HideLayoutWarning();
            }
        }

        private void LoadLayouts()
        {
            cmbLayout.Items.Clear();
            cmbLayout.Items.Add("(none)");

            var layouts = GetFilteredLayouts();
            for (int i = 0; i < layouts.Count; i++)
            {
                cmbLayout.Items.Add(layouts[i].name);
            }
            cmbLayout.SelectedIndex = 0;

            UpdateRemoveLayoutButtonVisibility();
        }

        private List<WindowLayoutObject> GetFilteredLayouts()
        {
            _filteredScratch.Clear();
            var src = Program.windowLayouts;
            if (src == null)
            {
                return _filteredScratch;
            }

            bool onlyName = _onlyWindowName;
            bool hasTitle = !string.IsNullOrEmpty(_targetWindowTitle);
            bool hasProc = !string.IsNullOrEmpty(_processName);

            if (!onlyName && !hasTitle)
            {
                _filteredScratch.AddRange(src);
                return _filteredScratch;
            }

            if (onlyName && hasTitle)
            {
                for (int i = 0; i < src.Count; i++)
                {
                    var l = src[i];
                    if (!string.IsNullOrEmpty(l.windowName) &&
                        string.Equals(l.windowName, _targetWindowTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        _filteredScratch.Add(l);
                    }
                }
                return _filteredScratch;
            }

            if (!onlyName && hasTitle && hasProc)
            {
                for (int i = 0; i < src.Count; i++)
                {
                    var l = src[i];
                    bool nameMatch = !string.IsNullOrEmpty(l.windowName) &&
                                     string.Equals(l.windowName, _targetWindowTitle, StringComparison.OrdinalIgnoreCase);
                    bool procMatch = !string.IsNullOrEmpty(l.processName) &&
                                     string.Equals(l.processName, _processName, StringComparison.OrdinalIgnoreCase);
                    if (nameMatch || procMatch)
                    {
                        _filteredScratch.Add(l);
                    }
                }
                return _filteredScratch;
            }

            return _filteredScratch;
        }

        private void ChkOnlyWindowName_CheckedChanged(object sender, EventArgs e)
        {
            _onlyWindowName = chkOnlyWindowName.Checked;
            LoadLayouts();
        }

        private void UpdateRemoveLayoutButtonVisibility()
        {
            if (btnRemoveLayout == null)
            {
                return;
            }

            bool hasRealLayout =
                cmbLayout != null &&
                cmbLayout.SelectedIndex > 0 &&
                !string.IsNullOrEmpty(cmbLayout.SelectedItem as string);

            btnRemoveLayout.Visible = hasRealLayout;
        }

        private void BtnRemoveLayout_Click(object sender, EventArgs e)
        {
            string layoutName = cmbLayout.SelectedItem as string;
            if (string.IsNullOrEmpty(layoutName) || cmbLayout.SelectedIndex <= 0)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                $"Are you sure to remove {layoutName} layout? This action is irreversible!",
                "Hold on",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                return;
            }

            RemoveLayout(layoutName);

            LoadLayouts();
        }

        private void RemoveLayout(string layoutName)
        {
            Program.windowLayouts.Remove(Program.windowLayouts.Find(l => l.name == layoutName));
            Program.SaveLayoutFile();
        }

        private void ApplyMinimumClientSize()
        {
            int contentW = 0;
            if (panel1 != null)
            {
                foreach (Control c in panel1.Controls)
                {
                    if (!c.Visible && c != btnRemoveLayout)
                    {
                        continue;
                    }

                    contentW = Math.Max(contentW, c.Right + 12);
                }
            }
            contentW = Math.Max(contentW, MIN_PANEL_CONTENT_WIDTH);

            int panelH = (panel1 != null && panel1.Height > 0)
                ? panel1.Height
                : 65;

            Size minClient = new Size(contentW, panelH + 150);
            this.MinimumSize = ClientToSize(minClient);
        }

        private const int BASE_WIDTH = 800;
        private const int BASE_HEIGHT = 600;
        private const double SCALE_STRENGTH = 0.5;
        private const double REF_AREA = 1280.0 * 720.0;

        private void ApplyScreenshotBasedSize(Bitmap screenshot)
        {
            this.ClientSize = ComputeDesiredClientSize(screenshot);
        }

        private Size ComputeWindowSize(Bitmap screenshot)
        {
            return ClientToSize(ComputeDesiredClientSize(screenshot));
        }

        private Size ComputeDesiredClientSize(Bitmap screenshot)
        {
            double ratio = 1.0;
            if (screenshot != null && screenshot.Width > 0 && screenshot.Height > 0)
            {
                double area = (double)screenshot.Width * screenshot.Height;
                ratio = area / REF_AREA;
                if (ratio < 1.0)
                {
                    ratio = 1.0;
                }
            }
            double scale = Math.Pow(ratio, SCALE_STRENGTH);

            int imageW = (int)Math.Round(BASE_WIDTH * scale);
            int imageH = (int)Math.Round(BASE_HEIGHT * scale);

            int panelH = (panel1 != null && panel1.Height > 0)
                ? panel1.Height
                : 65;

            Size minClient = SizeToClientSize(this.MinimumSize);
            int minW = Math.Max(minClient.Width, MIN_PANEL_CONTENT_WIDTH);
            int minH = Math.Max(minClient.Height, panelH + 150);

            Size desiredClient = new Size(
                Math.Max(imageW, minW),
                Math.Max(imageH + panelH, minH));

            Rectangle wa;
            try
            {
                wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            }
            catch
            {
                wa = Screen.PrimaryScreen.WorkingArea;
            }

            int maxOuterW = Math.Max(BASE_WIDTH, wa.Width - 60);
            int maxOuterH = Math.Max(BASE_HEIGHT, wa.Height - 60);
            Size maxClient = SizeToClientSize(new Size(maxOuterW, maxOuterH));

            desiredClient.Width = Math.Min(desiredClient.Width, maxClient.Width);
            desiredClient.Height = Math.Min(desiredClient.Height, maxClient.Height);

            desiredClient.Width = Math.Max(desiredClient.Width, minW);
            desiredClient.Height = Math.Max(desiredClient.Height, minH);

            return desiredClient;
        }

        private void UpdateCachedBorders()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Size curOuter = this.Size;
            Size curClient = this.ClientSize;
            _cachedDx = curOuter.Width - curClient.Width;
            _cachedDy = curOuter.Height - curClient.Height;
        }

        private Size ClientToSize(Size client)
        {
            if (_cachedDx >= 0)
            {
                return new Size(client.Width + _cachedDx, client.Height + _cachedDy);
            }

            if (this.IsHandleCreated)
            {
                UpdateCachedBorders();
                return new Size(client.Width + _cachedDx, client.Height + _cachedDy);
            }

            using (var probe = new Form())
            {
                probe.FormBorderStyle = this.FormBorderStyle;
                probe.ShowInTaskbar = false;
                probe.StartPosition = FormStartPosition.Manual;
                probe.Location = new Point(-32000, -32000);
                probe.ClientSize = client;
                probe.Show();
                Size s = probe.Size;
                probe.Hide();
                return s;
            }
        }

        private Size SizeToClientSize(Size outer)
        {
            if (_cachedDx >= 0)
            {
                return new Size(
                    Math.Max(1, outer.Width - _cachedDx),
                    Math.Max(1, outer.Height - _cachedDy));
            }

            if (this.IsHandleCreated)
            {
                UpdateCachedBorders();
                return new Size(
                    Math.Max(1, outer.Width - _cachedDx),
                    Math.Max(1, outer.Height - _cachedDy));
            }

            using (var probe = new Form())
            {
                probe.FormBorderStyle = this.FormBorderStyle;
                probe.ShowInTaskbar = false;
                probe.StartPosition = FormStartPosition.Manual;
                probe.Location = new Point(-32000, -32000);
                probe.Size = outer;
                probe.Show();
                Size s = probe.ClientSize;
                probe.Hide();
                return s;
            }
        }

        private enum ResizeHandle
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private ResizeHandle _activeResizeHandle = ResizeHandle.None;
        private Point _mouseDownImagePoint;
        private Rectangle _mouseDownSelectionRect;
        private bool _movingSelection;
        private bool _creatingSelection;

        private const int HANDLE_HIT_SIZE = 24;
        private const int MIN_SELECTION_SIZE = 6;

        private sealed class SelectionHistoryState
        {
            public Rectangle SelectionRect;
            public Rectangle InitialSelection;
            public bool HasSelection;
        }

        private readonly List<SelectionHistoryState> _undoHistory =
            new List<SelectionHistoryState>();

        private readonly List<SelectionHistoryState> _redoHistory =
            new List<SelectionHistoryState>();

        private SelectionHistoryState _historyActionStart;
        private bool _historyRestoring;
        private const int MAX_HISTORY_ENTRIES = 100;

        private SelectionHistoryState CreateHistoryState()
        {
            return new SelectionHistoryState
            {
                SelectionRect = _selectionRect,
                InitialSelection = _initialSelection,
                HasSelection = _hasSelection
            };
        }

        private bool HistoryStatesEqual(
            SelectionHistoryState a,
            SelectionHistoryState b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            return a.SelectionRect == b.SelectionRect &&
                   a.InitialSelection == b.InitialSelection &&
                   a.HasSelection == b.HasSelection;
        }

        private void InitializeHistory()
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
            _historyActionStart = null;

            if (_screenshot != null)
            {
                _undoHistory.Add(CreateHistoryState());
            }
        }

        private void BeginHistoryAction()
        {
            if (_historyRestoring)
            {
                return;
            }

            _historyActionStart = CreateHistoryState();
        }

        private void CommitHistoryAction()
        {
            if (_historyRestoring || _historyActionStart == null)
            {
                _historyActionStart = null;
                return;
            }

            SelectionHistoryState current = CreateHistoryState();

            if (HistoryStatesEqual(_historyActionStart, current))
            {
                _historyActionStart = null;
                return;
            }

            _undoHistory.Add(current);

            if (_undoHistory.Count > MAX_HISTORY_ENTRIES)
            {
                _undoHistory.RemoveAt(0);
            }

            _redoHistory.Clear();
            _historyActionStart = null;
        }

        private void UndoSelection()
        {
            if (_isSelecting || _undoHistory.Count <= 1)
            {
                return;
            }

            SelectionHistoryState current = _undoHistory[_undoHistory.Count - 1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            _redoHistory.Add(current);

            SelectionHistoryState previous =
                _undoHistory[_undoHistory.Count - 1];

            RestoreHistoryState(previous);
        }

        private void RedoSelection()
        {
            if (_isSelecting || _redoHistory.Count == 0)
            {
                return;
            }

            SelectionHistoryState next =
                _redoHistory[_redoHistory.Count - 1];

            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            _undoHistory.Add(next);

            RestoreHistoryState(next);
        }

        private void RestoreHistoryState(SelectionHistoryState state)
        {
            if (state == null)
            {
                return;
            }

            _historyRestoring = true;
            try
            {
                _selectionRect = state.SelectionRect;
                _initialSelection = state.InitialSelection;
                _hasSelection = state.HasSelection;

                btnConfirm.Enabled =
                    _hasSelection &&
                    _selectionRect.Width > 5 &&
                    _selectionRect.Height > 5;

                UpdateInfoText();
                pictureBox1.Invalidate();
            }
            finally
            {
                _historyRestoring = false;
            }
        }

        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _screenshot == null)
            {
                return;
            }

            Rectangle imageRect = GetImageRectangle();
            if (imageRect.IsEmpty || !imageRect.Contains(e.Location))
            {
                return;
            }

            if (!pictureBox1.Focused)
            {
                pictureBox1.Focus();
            }

            Point imagePoint = PicturePointToImagePoint(e.Location);

            if (_hasSelection && !_selectionRect.IsEmpty)
            {
                ResizeHandle handle = GetResizeHandle(e.Location);

                if (handle != ResizeHandle.None)
                {
                    BeginHistoryAction();
                    _isSelecting = true;
                    _activeResizeHandle = handle;
                    _movingSelection = false;
                    _creatingSelection = false;
                    _mouseDownImagePoint = imagePoint;
                    _mouseDownSelectionRect = _selectionRect;
                    btnConfirm.Enabled = false;
                    pictureBox1.Invalidate();
                    return;
                }

                bool ctrlShift =
                    (Control.ModifierKeys & Keys.Control) == Keys.Control &&
                    (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

                if (_selectionRect.Contains(imagePoint))
                {
                    if (ctrlShift)
                    {
                        BeginHistoryAction();
                        _isSelecting = true;
                        _activeResizeHandle = ResizeHandle.None;
                        _movingSelection = true;
                        _creatingSelection = false;
                        _mouseDownImagePoint = imagePoint;
                        _mouseDownSelectionRect = _selectionRect;
                        btnConfirm.Enabled = false;
                        pictureBox1.Invalidate();
                        return;
                    }

                    BeginHistoryAction();
                    _isSelecting = true;
                    _activeResizeHandle = ResizeHandle.None;
                    _movingSelection = false;
                    _creatingSelection = true;
                    _mouseDownImagePoint = imagePoint;
                    _mouseDownSelectionRect = Rectangle.Empty;
                    _selectionStart = imagePoint;
                    _selectionRect = new Rectangle(imagePoint, Size.Empty);
                    _hasSelection = false;
                    btnConfirm.Enabled = false;
                    pictureBox1.Invalidate();
                    return;
                }
            }

            BeginHistoryAction();
            _isSelecting = true;
            _activeResizeHandle = ResizeHandle.None;
            _movingSelection = false;
            _creatingSelection = true;
            _mouseDownImagePoint = imagePoint;
            _mouseDownSelectionRect = Rectangle.Empty;
            _selectionStart = imagePoint;
            _selectionRect = new Rectangle(imagePoint, Size.Empty);
            _hasSelection = false;
            btnConfirm.Enabled = false;

            pictureBox1.Invalidate();
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (_screenshot == null)
            {
                return;
            }

            if (!_isSelecting)
            {
                return;
            }

            Point imagePoint = PicturePointToImagePoint(e.Location);

            if (_creatingSelection)
            {
                UpdateNewSelection(imagePoint);
            }
            else if (_movingSelection)
            {
                UpdateMovedSelection(imagePoint);
            }
            else if (_activeResizeHandle != ResizeHandle.None)
            {
                UpdateResizedSelection(imagePoint);
            }

            if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
            {
                lblInfo.Text =
                    $"Selection: {_selectionRect.Width}×{_selectionRect.Height} pixels";
            }

            pictureBox1.Invalidate();
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_isSelecting || _screenshot == null)
            {
                return;
            }

            _isSelecting = false;

            bool validSelection =
                _selectionRect.Width > 10 &&
                _selectionRect.Height > 10;

            if (validSelection)
            {
                _hasSelection = true;
                btnConfirm.Enabled = true;
                UpdateInfoText();
                CommitHistoryAction();
            }
            else
            {
                _selectionRect = _initialSelection;
                _hasSelection = true;
                btnConfirm.Enabled = true;
                UpdateInfoText();
                _historyActionStart = null;
            }

            _activeResizeHandle = ResizeHandle.None;
            _movingSelection = false;
            _creatingSelection = false;

            pictureBox1.Invalidate();
        }

        private Point PicturePointToImagePoint(Point picturePoint)
        {
            Rectangle imageRect = GetImageRectangle();

            if (imageRect.IsEmpty || _screenshot == null)
            {
                return Point.Empty;
            }

            float scaleX =
                (float)_screenshot.Width / imageRect.Width;

            float scaleY =
                (float)_screenshot.Height / imageRect.Height;

            int x = (int)Math.Round(
                (picturePoint.X - imageRect.X) * scaleX);

            int y = (int)Math.Round(
                (picturePoint.Y - imageRect.Y) * scaleY);

            x = Math.Max(0, Math.Min(x, _screenshot.Width));
            y = Math.Max(0, Math.Min(y, _screenshot.Height));

            return new Point(x, y);
        }

        private Point ImagePointToPicturePoint(Point imagePoint)
        {
            Rectangle imageRect = GetImageRectangle();

            if (imageRect.IsEmpty || _screenshot == null)
            {
                return Point.Empty;
            }

            float scaleX =
                (float)imageRect.Width / _screenshot.Width;

            float scaleY =
                (float)imageRect.Height / _screenshot.Height;

            int x =
                imageRect.X +
                (int)Math.Round(imagePoint.X * scaleX);

            int y =
                imageRect.Y +
                (int)Math.Round(imagePoint.Y * scaleY);

            return new Point(x, y);
        }

        private ResizeHandle GetResizeHandle(Point picturePoint)
        {
            if (!_hasSelection || _selectionRect.IsEmpty)
            {
                return ResizeHandle.None;
            }

            Point topLeft = ImagePointToPicturePoint(
                new Point(_selectionRect.Left, _selectionRect.Top));

            Point topRight = ImagePointToPicturePoint(
                new Point(_selectionRect.Right, _selectionRect.Top));

            Point bottomLeft = ImagePointToPicturePoint(
                new Point(_selectionRect.Left, _selectionRect.Bottom));

            Point bottomRight = ImagePointToPicturePoint(
                new Point(_selectionRect.Right, _selectionRect.Bottom));

            int half = HANDLE_HIT_SIZE / 2;

            Rectangle topLeftHandle = new Rectangle(
                topLeft.X - half,
                topLeft.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle topRightHandle = new Rectangle(
                topRight.X - half,
                topRight.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle bottomLeftHandle = new Rectangle(
                bottomLeft.X - half,
                bottomLeft.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle bottomRightHandle = new Rectangle(
                bottomRight.X - half,
                bottomRight.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            if (topLeftHandle.Contains(picturePoint))
            {
                return ResizeHandle.TopLeft;
            }

            if (topRightHandle.Contains(picturePoint))
            {
                return ResizeHandle.TopRight;
            }

            if (bottomLeftHandle.Contains(picturePoint))
            {
                return ResizeHandle.BottomLeft;
            }

            if (bottomRightHandle.Contains(picturePoint))
            {
                return ResizeHandle.BottomRight;
            }

            Point topCenter = ImagePointToPicturePoint(
                new Point(
                    _selectionRect.Left + _selectionRect.Width / 2,
                    _selectionRect.Top));

            Point bottomCenter = ImagePointToPicturePoint(
                new Point(
                    _selectionRect.Left + _selectionRect.Width / 2,
                    _selectionRect.Bottom));

            Point leftCenter = ImagePointToPicturePoint(
                new Point(
                    _selectionRect.Left,
                    _selectionRect.Top + _selectionRect.Height / 2));

            Point rightCenter = ImagePointToPicturePoint(
                new Point(
                    _selectionRect.Right,
                    _selectionRect.Top + _selectionRect.Height / 2));

            Rectangle topHandle = new Rectangle(
                topCenter.X - half,
                topCenter.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle bottomHandle = new Rectangle(
                bottomCenter.X - half,
                bottomCenter.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle leftHandle = new Rectangle(
                leftCenter.X - half,
                leftCenter.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            Rectangle rightHandle = new Rectangle(
                rightCenter.X - half,
                rightCenter.Y - half,
                HANDLE_HIT_SIZE,
                HANDLE_HIT_SIZE);

            if (topHandle.Contains(picturePoint))
            {
                return ResizeHandle.Top;
            }

            if (bottomHandle.Contains(picturePoint))
            {
                return ResizeHandle.Bottom;
            }

            if (leftHandle.Contains(picturePoint))
            {
                return ResizeHandle.Left;
            }

            if (rightHandle.Contains(picturePoint))
            {
                return ResizeHandle.Right;
            }

            return ResizeHandle.None;
        }

        private void UpdateNewSelection(Point current)
        {
            Point start = _selectionStart;

            int left = Math.Min(start.X, current.X);
            int top = Math.Min(start.Y, current.Y);
            int right = Math.Max(start.X, current.X);
            int bottom = Math.Max(start.Y, current.Y);

            left = Math.Max(0, Math.Min(left, _screenshot.Width));
            top = Math.Max(0, Math.Min(top, _screenshot.Height));
            right = Math.Max(0, Math.Min(right, _screenshot.Width));
            bottom = Math.Max(0, Math.Min(bottom, _screenshot.Height));

            _selectionRect = Rectangle.FromLTRB(
                left,
                top,
                right,
                bottom);
        }

        private void UpdateMovedSelection(Point current)
        {
            int dx = current.X - _mouseDownImagePoint.X;
            int dy = current.Y - _mouseDownImagePoint.Y;

            Rectangle original = _mouseDownSelectionRect;

            int x = original.X + dx;
            int y = original.Y + dy;

            x = Math.Max(
                0,
                Math.Min(
                    x,
                    _screenshot.Width - original.Width));

            y = Math.Max(
                0,
                Math.Min(
                    y,
                    _screenshot.Height - original.Height));

            _selectionRect = new Rectangle(
                x,
                y,
                original.Width,
                original.Height);
        }

        private void UpdateResizedSelection(Point current)
        {
            Rectangle original = _mouseDownSelectionRect;

            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool alt = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;

            int left = original.Left;
            int top = original.Top;
            int right = original.Right;
            int bottom = original.Bottom;

            int mouseX = current.X;
            int mouseY = current.Y;

            if (alt &&
                (_activeResizeHandle == ResizeHandle.TopLeft ||
                 _activeResizeHandle == ResizeHandle.TopRight ||
                 _activeResizeHandle == ResizeHandle.BottomLeft ||
                 _activeResizeHandle == ResizeHandle.BottomRight))
            {
                int deltaX = Math.Abs(mouseX - _mouseDownImagePoint.X);
                int deltaY = Math.Abs(mouseY - _mouseDownImagePoint.Y);

                if (deltaX >= deltaY)
                {
                    if (_activeResizeHandle == ResizeHandle.TopLeft ||
                        _activeResizeHandle == ResizeHandle.BottomLeft)
                    {
                        left = mouseX;
                    }
                    else
                    {
                        right = mouseX;
                    }
                }
                else
                {
                    if (_activeResizeHandle == ResizeHandle.TopLeft ||
                        _activeResizeHandle == ResizeHandle.TopRight)
                    {
                        top = mouseY;
                    }
                    else
                    {
                        bottom = mouseY;
                    }
                }

                left = Math.Max(0, Math.Min(left, _screenshot.Width));
                right = Math.Max(0, Math.Min(right, _screenshot.Width));
                top = Math.Max(0, Math.Min(top, _screenshot.Height));
                bottom = Math.Max(0, Math.Min(bottom, _screenshot.Height));

                if (right - left < MIN_SELECTION_SIZE)
                {
                    if (_activeResizeHandle == ResizeHandle.TopLeft ||
                        _activeResizeHandle == ResizeHandle.BottomLeft)
                    {
                        left = right - MIN_SELECTION_SIZE;
                    }
                    else
                    {
                        right = left + MIN_SELECTION_SIZE;
                    }
                }

                if (bottom - top < MIN_SELECTION_SIZE)
                {
                    if (_activeResizeHandle == ResizeHandle.TopLeft ||
                        _activeResizeHandle == ResizeHandle.TopRight)
                    {
                        top = bottom - MIN_SELECTION_SIZE;
                    }
                    else
                    {
                        bottom = top + MIN_SELECTION_SIZE;
                    }
                }

                left = Math.Max(0, left);
                top = Math.Max(0, top);
                right = Math.Min(_screenshot.Width, right);
                bottom = Math.Min(_screenshot.Height, bottom);

                _selectionRect = Rectangle.FromLTRB(
                    left,
                    top,
                    right,
                    bottom);

                return;
            }

            switch (_activeResizeHandle)
            {
                case ResizeHandle.Left:
                    left = mouseX;
                    break;

                case ResizeHandle.Right:
                    right = mouseX;
                    break;

                case ResizeHandle.Top:
                    top = mouseY;
                    break;

                case ResizeHandle.Bottom:
                    bottom = mouseY;
                    break;

                case ResizeHandle.TopLeft:
                    left = mouseX;
                    top = mouseY;
                    break;

                case ResizeHandle.TopRight:
                    right = mouseX;
                    top = mouseY;
                    break;

                case ResizeHandle.BottomLeft:
                    left = mouseX;
                    bottom = mouseY;
                    break;

                case ResizeHandle.BottomRight:
                    right = mouseX;
                    bottom = mouseY;
                    break;
            }

            if (shift &&
                (_activeResizeHandle == ResizeHandle.TopLeft ||
                 _activeResizeHandle == ResizeHandle.TopRight ||
                 _activeResizeHandle == ResizeHandle.BottomLeft ||
                 _activeResizeHandle == ResizeHandle.BottomRight))
            {
                ApplyAspectRatio(
                    original,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom);
            }

            if (ctrl)
            {
                ApplyCenteredResize(
                    original,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom,
                    shift);
            }

            left = Math.Max(0, Math.Min(left, _screenshot.Width));
            right = Math.Max(0, Math.Min(right, _screenshot.Width));
            top = Math.Max(0, Math.Min(top, _screenshot.Height));
            bottom = Math.Max(0, Math.Min(bottom, _screenshot.Height));

            if (right < left)
            {
                int temp = left;
                left = right;
                right = temp;
            }

            if (bottom < top)
            {
                int temp = top;
                top = bottom;
                bottom = temp;
            }

            if (right - left < MIN_SELECTION_SIZE)
            {
                if (_activeResizeHandle == ResizeHandle.Left ||
                    _activeResizeHandle == ResizeHandle.TopLeft ||
                    _activeResizeHandle == ResizeHandle.BottomLeft)
                {
                    left = Math.Max(0, right - MIN_SELECTION_SIZE);
                }
                else
                {
                    right = Math.Min(
                        _screenshot.Width,
                        left + MIN_SELECTION_SIZE);
                }
            }

            if (bottom - top < MIN_SELECTION_SIZE)
            {
                if (_activeResizeHandle == ResizeHandle.Top ||
                    _activeResizeHandle == ResizeHandle.TopLeft ||
                    _activeResizeHandle == ResizeHandle.TopRight)
                {
                    top = Math.Max(0, bottom - MIN_SELECTION_SIZE);
                }
                else
                {
                    bottom = Math.Min(
                        _screenshot.Height,
                        top + MIN_SELECTION_SIZE);
                }
            }

            _selectionRect = Rectangle.FromLTRB(
                left,
                top,
                right,
                bottom);
        }

        private void ApplyAspectRatio(
            Rectangle original,
            ref int left,
            ref int top,
            ref int right,
            ref int bottom)
        {
            double aspect =
                (double)original.Width / original.Height;

            int width = Math.Abs(right - left);
            int height = Math.Abs(bottom - top);

            if (width < 1)
            {
                width = 1;
            }

            if (height < 1)
            {
                height = 1;
            }

            int correctedWidth;
            int correctedHeight;

            double widthFromHeight = height * aspect;
            double heightFromWidth = width / aspect;

            if (widthFromHeight >= width)
            {
                correctedWidth = (int)Math.Round(widthFromHeight);
                correctedHeight = height;
            }
            else
            {
                correctedWidth = width;
                correctedHeight = (int)Math.Round(heightFromWidth);
            }

            switch (_activeResizeHandle)
            {
                case ResizeHandle.TopLeft:
                    left = right - correctedWidth;
                    top = bottom - correctedHeight;
                    break;

                case ResizeHandle.TopRight:
                    right = left + correctedWidth;
                    top = bottom - correctedHeight;
                    break;

                case ResizeHandle.BottomLeft:
                    left = right - correctedWidth;
                    bottom = top + correctedHeight;
                    break;

                case ResizeHandle.BottomRight:
                    right = left + correctedWidth;
                    bottom = top + correctedHeight;
                    break;
            }
        }

        private void ApplyCenteredResize(
            Rectangle original,
            ref int left,
            ref int top,
            ref int right,
            ref int bottom,
            bool preserveAspect)
        {
            int centerX =
                original.Left + original.Width / 2;

            int centerY =
                original.Top + original.Height / 2;

            int width = Math.Abs(right - left);
            int height = Math.Abs(bottom - top);

            if (_activeResizeHandle == ResizeHandle.Left ||
                _activeResizeHandle == ResizeHandle.Right)
            {
                width = Math.Abs(
                    (left + right) / 2 - centerX) * 2;
            }
            else if (_activeResizeHandle == ResizeHandle.Top ||
                     _activeResizeHandle == ResizeHandle.Bottom)
            {
                height = Math.Abs(
                    (top + bottom) / 2 - centerY) * 2;
            }

            if (width < MIN_SELECTION_SIZE)
            {
                width = MIN_SELECTION_SIZE;
            }

            if (height < MIN_SELECTION_SIZE)
            {
                height = MIN_SELECTION_SIZE;
            }

            if (preserveAspect)
            {
                double aspect =
                    (double)original.Width / original.Height;

                if (_activeResizeHandle == ResizeHandle.Left ||
                    _activeResizeHandle == ResizeHandle.Right)
                {
                    height = Math.Max(
                        MIN_SELECTION_SIZE,
                        (int)Math.Round(width / aspect));
                }
                else if (_activeResizeHandle == ResizeHandle.Top ||
                         _activeResizeHandle == ResizeHandle.Bottom)
                {
                    width = Math.Max(
                        MIN_SELECTION_SIZE,
                        (int)Math.Round(height * aspect));
                }
                else
                {
                    double widthFromHeight =
                        height * aspect;

                    double heightFromWidth =
                        width / aspect;

                    if (widthFromHeight >= width)
                    {
                        width = Math.Max(
                            MIN_SELECTION_SIZE,
                            (int)Math.Round(widthFromHeight));
                    }
                    else
                    {
                        height = Math.Max(
                            MIN_SELECTION_SIZE,
                            (int)Math.Round(heightFromWidth));
                    }
                }
            }

            left = centerX - width / 2;
            right = centerX + (width + 1) / 2;
            top = centerY - height / 2;
            bottom = centerY + (height + 1) / 2;

            if (left < 0)
            {
                right -= left;
                left = 0;
            }

            if (top < 0)
            {
                bottom -= top;
                top = 0;
            }

            if (right > _screenshot.Width)
            {
                int overflow = right - _screenshot.Width;
                left -= overflow;
                right = _screenshot.Width;
            }

            if (bottom > _screenshot.Height)
            {
                int overflow = bottom - _screenshot.Height;
                top -= overflow;
                bottom = _screenshot.Height;
            }

            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Min(_screenshot.Width, right);
            bottom = Math.Min(_screenshot.Height, bottom);
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (_screenshot == null || _selectionRect.IsEmpty)
            {
                return;
            }

            Rectangle imageRect = GetImageRectangle();

            if (imageRect.IsEmpty)
            {
                return;
            }

            float scaleX =
                (float)_screenshot.Width / imageRect.Width;

            float scaleY =
                (float)_screenshot.Height / imageRect.Height;

            int x =
                (int)Math.Round(
                    _selectionRect.X / scaleX + imageRect.X);

            int y =
                (int)Math.Round(
                    _selectionRect.Y / scaleY + imageRect.Y);

            int width =
                (int)Math.Round(
                    _selectionRect.Width / scaleX);

            int height =
                (int)Math.Round(
                    _selectionRect.Height / scaleY);

            width = Math.Max(1, width);
            height = Math.Max(1, height);

            int topHeight = y - imageRect.Y;

            if (topHeight > 0)
            {
                e.Graphics.FillRectangle(
                    _shadeBrush,
                    imageRect.X,
                    imageRect.Y,
                    imageRect.Width,
                    topHeight);
            }

            int bottomY = y + height;

            if (bottomY < imageRect.Bottom)
            {
                e.Graphics.FillRectangle(
                    _shadeBrush,
                    imageRect.X,
                    bottomY,
                    imageRect.Width,
                    imageRect.Bottom - bottomY);
            }

            int leftWidth = x - imageRect.X;

            if (leftWidth > 0)
            {
                e.Graphics.FillRectangle(
                    _shadeBrush,
                    imageRect.X,
                    y,
                    leftWidth,
                    height);
            }

            int rightX = x + width;

            if (rightX < imageRect.Right)
            {
                e.Graphics.FillRectangle(
                    _shadeBrush,
                    rightX,
                    y,
                    imageRect.Right - rightX,
                    height);
            }

            e.Graphics.DrawRectangle(
                _selectionPen,
                x,
                y,
                width,
                height);

            int handleSize = 10;

            DrawHandle(e.Graphics, _selectionHandleBrush, x, y, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x + width, y, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x, y + height, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x + width, y + height, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x + width / 2, y, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x + width / 2, y + height, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x, y + height / 2, handleSize);
            DrawHandle(e.Graphics, _selectionHandleBrush, x + width, y + height / 2, handleSize);

            string sizeText =
                $"{_selectionRect.Width} × {_selectionRect.Height}";

            SizeF textSize =
                e.Graphics.MeasureString(
                    sizeText,
                    _sizeFont);

            float textX =
                x + (width - textSize.Width) / 2;

            float textY =
                y - textSize.Height - 8;

            if (textY < imageRect.Y + 10)
            {
                textY =
                    y + height + 8;
            }

            textX =
                Math.Max(
                    imageRect.X + 10,
                    Math.Min(
                        textX,
                        imageRect.X +
                        imageRect.Width -
                        textSize.Width -
                        10));

            e.Graphics.FillRectangle(
                _sizeBgBrush,
                textX - 8,
                textY - 4,
                textSize.Width + 16,
                textSize.Height + 8);

            e.Graphics.DrawString(
                sizeText,
                _sizeFont,
                _sizeTextBrush,
                textX,
                textY);
        }

        private void DrawHandle(
            Graphics graphics,
            Brush brush,
            int centerX,
            int centerY,
            int size)
        {
            graphics.FillRectangle(
                brush,
                centerX - size / 2,
                centerY - size / 2,
                size,
                size);
        }

        private Rectangle GetImageRectangle()
        {
            if (pictureBox1.Image == null)
            {
                return Rectangle.Empty;
            }

            float scaleX = (float)pictureBox1.ClientSize.Width / _screenshot.Width;
            float scaleY = (float)pictureBox1.ClientSize.Height / _screenshot.Height;
            float scale = Math.Min(scaleX, scaleY);

            int x = (int)((pictureBox1.ClientSize.Width - _screenshot.Width * scale) / 2);
            int y = (int)((pictureBox1.ClientSize.Height - _screenshot.Height * scale) / 2);
            int width = (int)(_screenshot.Width * scale);
            int height = (int)(_screenshot.Height * scale);

            return new Rectangle(x, y, width, height);
        }

        private void UpdateInfoText()
        {
            lblInfo.Text = $"Selection: {_selectionRect.Width}×{_selectionRect.Height} px";
        }

        private void BtnConfirm_Click(object sender, EventArgs e)
        {
            if (_hasSelection && _selectionRect.Width > 5 && _selectionRect.Height > 5)
            {
                SelectedRect = _selectionRect;

                SelectedLayout = null;
                if (cmbLayout.SelectedIndex > 0)
                {
                    string chosenName = cmbLayout.SelectedItem as string;
                    SelectedLayout = Program.windowLayouts
                        .Find(l => l.name == chosenName);
                }

                StopResizeWatcher();
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            StopResizeWatcher();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                UndoSelection();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Y))
            {
                RedoSelection();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                BtnCancel_Click(null, null);
                return true;
            }

            if (keyData == Keys.Enter && this.ActiveControl == null)
            {
                if (btnConfirm.Enabled)
                {
                    BtnConfirm_Click(null, null);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}