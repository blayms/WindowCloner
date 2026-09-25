using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WindowCloner;

public partial class Form1 : Form
{
    public readonly IntPtr _targetHwnd;
    private readonly string _windowTitle;
    private readonly string _processName;

    private IntPtr _thumbnailHandle = IntPtr.Zero;
    private Rectangle _captureRect;

    private NativeMethods.WINDOWPLACEMENT _originalPlacement;
    private bool _hasOriginalPlacement;
    private bool _isWindowHidden;
    private bool _isTargetWindowMinimized;

    private System.Windows.Forms.Timer? _watchdogTimer;
    private bool _closing;

    private bool _inSizeMove;
    private Size _baseWindowSize = Size.Empty;
    private Point _baseWindowLocation = Point.Empty;
    private Rectangle _baseCaptureRect = Rectangle.Empty;
    private int _lastDeltaW;
    private int _lastDeltaH;
    private int _lastDeltaX;
    private int _lastDeltaY;

    private double _scaleX = 1.0;
    private double _scaleY = 1.0;
    private double _baseScaleX = 1.0;
    private double _baseScaleY = 1.0;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private Icon? _trayIcon;
    private MemoryStream? _trayIconStream;

    private IntPtr _currentRegion = IntPtr.Zero;
    private Size _lastRegionSize = Size.Empty;

    private NativeMethods.DWM_THUMBNAIL_PROPERTIES _lastThumbProps;
    private bool _hasThumbProps;

    private static readonly Pen BorderPen = new Pen(Color.FromArgb(80, 80, 80), 1);
    private GraphicsPath? _cachedBorderPath;
    private Size _cachedBorderPathSize = Size.Empty;

    internal WindowLayoutObject? _wlo;

    public Rectangle CaptureRect => _captureRect;
    public double CaptureScaleX => _scaleX;
    public double CaptureScaleY => _scaleY;
    public int CaptureWidth => Math.Max(1, _captureRect.Width);
    public int CaptureHeight => Math.Max(1, _captureRect.Height);

    public WindowLayoutObject? WindowLayoutObject => _wlo;

    public Form1(WindowLayoutObject? wlo, Rectangle rect, IntPtr targetHwnd, string windowTitle,
                 string processName, Rectangle captureRect)
    {
        _wlo = wlo;
        _targetHwnd = targetHwnd;
        _windowTitle = windowTitle;
        _processName = processName;
        _captureRect = captureRect;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = false;
        BackColor = Color.Black;
        TopMost = true;
        Text = "Window Cloner";
        ShowInTaskbar = false;
        KeyPreview = true;
        _captureRect = rect;
        _baseCaptureRect = rect;
        Size = new Size(Math.Max(1, captureRect.Width), Math.Max(1, captureRect.Height));
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Constants.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public void ApplyLayout(WindowLayoutObject wlo)
    {
        Location = new Point(wlo.locationX, wlo.locationY);
        Size = new Size(wlo.width, wlo.height);

        int cw = wlo.rectW > 0 ? wlo.rectW : wlo.width;
        int ch = wlo.rectH > 0 ? wlo.rectH : wlo.height;

        _captureRect = new Rectangle(new Point(wlo.rectX, wlo.rectY), new Size(cw, ch));
        _scaleX = wlo.scaleX > 0 ? wlo.scaleX : 1.0;
        _scaleY = wlo.scaleY > 0 ? wlo.scaleY : 1.0;
        _baseScaleX = _scaleX;
        _baseScaleY = _scaleY;

        UpdateMirrorPosition();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedRegion();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_targetHwnd == IntPtr.Zero)
        {
            MessageBox.Show("No window to capture.");
            Close();
            return;
        }

        if (!IsDwmEnabled())
        {
            MessageBox.Show("DWM composition is not enabled.", "DWM Not Available",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        if (!IsWindowValid(_targetHwnd))
        {
            MessageBox.Show($"Target window '{_windowTitle}' is no longer valid.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        InitializeSystemTray();
        SaveOriginalPlacement();

        _isTargetWindowMinimized = NativeMethods.IsIconic(_targetHwnd);
        if (_isTargetWindowMinimized)
        {
            NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_RESTORE);
            Thread.Sleep(100);
        }

        HighlightTargetWindow(_targetHwnd);

        if (CreateThumbnail())
        {
            HideTargetWindow();
            _watchdogTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _watchdogTimer.Tick += WatchdogTimer_Tick;
            _watchdogTimer.Start();
        }
        else
        {
            MessageBox.Show(
                "Failed to create DWM thumbnail.\n\n" +
                "Possible reasons:\n" +
                "1. The window is minimized or hidden\n" +
                "2. The window belongs to a different desktop\n" +
                "3. The window is protected (like some browsers)\n\n" +
                "Try restoring the window manually and try again.",
                "Thumbnail Creation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (_wlo is not null)
        {
            ApplyLayout(_wlo);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var t = new System.Windows.Forms.Timer { Interval = 150 };
        t.Tick += (s, _) =>
        {
            t.Stop();
            t.Dispose();
            ToggleWindowVisibility();
        };
        t.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closing)
        {
            base.OnFormClosing(e);
            return;
        }
        _closing = true;

        if (_watchdogTimer is not null)
        {
            _watchdogTimer.Stop();
            _watchdogTimer.Tick -= WatchdogTimer_Tick;
            _watchdogTimer.Dispose();
            _watchdogTimer = null;
        }

        if (_thumbnailHandle != IntPtr.Zero)
        {
            _ = NativeMethods.DwmUnregisterThumbnail(_thumbnailHandle);
            _thumbnailHandle = IntPtr.Zero;
        }

        try
        {
            CleanupTargetWindow();
        }
        catch
        {
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _contextMenu?.Dispose();
        _contextMenu = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        _trayIconStream?.Dispose();
        _trayIconStream = null;

        _currentRegion = IntPtr.Zero;

        _cachedBorderPath?.Dispose();
        _cachedBorderPath = null;

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            CleanupTargetWindow();
        }
        catch
        {
        }
        base.OnFormClosed(e);
    }

    private void InitializeSystemTray()
    {
        _contextMenu = new ContextMenuStrip();
        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) =>
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
            }
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(Close);
            }
            else
            {
                Close();
            }
        };
        _contextMenu.Items.Add(quitItem);

        _trayIconStream = new MemoryStream(Properties.Resources.AppIco);
        _trayIcon = new Icon(_trayIconStream);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Window Cloner",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    private void ToggleVisibility()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
            Focus();
            NativeMethods.BringWindowToTop(Handle);
            NativeMethods.SetForegroundWindow(Handle);
        }
    }

    private static bool IsDwmEnabled()
    {
        try
        {
            int hr = NativeMethods.DwmIsCompositionEnabled(out bool enabled);
            return hr == 0 && enabled;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWindowValid(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }
        try
        {
            Span<char> buf = stackalloc char[128];
            return NativeMethods.GetWindowText(hwnd, buf, buf.Length) > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool CreateThumbnail()
    {
        try
        {
            int hr = NativeMethods.DwmRegisterThumbnail(Handle, _targetHwnd, out _thumbnailHandle);
            if (hr == 0 && _thumbnailHandle != IntPtr.Zero)
            {
                _hasThumbProps = false;
                UpdateMirrorPosition();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void RecreateThumbnail()
    {
        if (_thumbnailHandle != IntPtr.Zero)
        {
            _ = NativeMethods.DwmUnregisterThumbnail(_thumbnailHandle);
            _thumbnailHandle = IntPtr.Zero;
            _hasThumbProps = false;
        }
        Thread.Sleep(200);
        CreateThumbnail();
    }

    private void UpdateMirrorPosition()
    {
        if (_thumbnailHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int cw = ClientRectangle.Width;
            int ch = ClientRectangle.Height;

            var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Win32Constants.DWM_TNP_RECTDESTINATION
                        | Win32Constants.DWM_TNP_RECTSOURCE
                        | Win32Constants.DWM_TNP_VISIBLE
                        | Win32Constants.DWM_TNP_SOURCECLIENTAREAONLY,
                fVisible = 1,
                fSourceClientAreaOnly = 1,
                rcDestination = new NativeMethods.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = cw,
                    Bottom = ch
                },
                rcSource = new NativeMethods.RECT
                {
                    Left = _captureRect.X,
                    Top = _captureRect.Y,
                    Right = _captureRect.X + _captureRect.Width,
                    Bottom = _captureRect.Y + _captureRect.Height
                }
            };

            if (_hasThumbProps && ThumbPropsEqual(in _lastThumbProps, in props))
            {
                return;
            }

            _ = NativeMethods.DwmUpdateThumbnailProperties(_thumbnailHandle, ref props);
            _lastThumbProps = props;
            _hasThumbProps = true;
        }
        catch
        {
        }
    }

    private static bool ThumbPropsEqual(in NativeMethods.DWM_THUMBNAIL_PROPERTIES a,
                                        in NativeMethods.DWM_THUMBNAIL_PROPERTIES b)
    {
        return a.dwFlags == b.dwFlags
            && a.fVisible == b.fVisible
            && a.fSourceClientAreaOnly == b.fSourceClientAreaOnly
            && a.rcDestination.Left == b.rcDestination.Left
            && a.rcDestination.Top == b.rcDestination.Top
            && a.rcDestination.Right == b.rcDestination.Right
            && a.rcDestination.Bottom == b.rcDestination.Bottom
            && a.rcSource.Left == b.rcSource.Left
            && a.rcSource.Top == b.rcSource.Top
            && a.rcSource.Right == b.rcSource.Right
            && a.rcSource.Bottom == b.rcSource.Bottom;
    }

    private void SaveOriginalPlacement()
    {
        try
        {
            _originalPlacement = new NativeMethods.WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
            };
            _hasOriginalPlacement = NativeMethods.GetWindowPlacement(_targetHwnd, ref _originalPlacement);
        }
        catch
        {
            _hasOriginalPlacement = false;
        }
    }

    private void HideTargetWindow()
    {
        if (_targetHwnd == IntPtr.Zero || _isWindowHidden || !IsWindowValid(_targetHwnd))
        {
            return;
        }

        try
        {
            NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero,
                Win32Constants.OFFSCREEN_X, Win32Constants.OFFSCREEN_Y, 0, 0,
                Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOZORDER);
            _isWindowHidden = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error hiding window: {ex.Message}");
        }
    }

    private void ShowTargetWindow()
    {
        if (_targetHwnd == IntPtr.Zero || !_isWindowHidden || !IsWindowValid(_targetHwnd))
        {
            _isWindowHidden = false;
            return;
        }

        try
        {
            if (_hasOriginalPlacement)
            {
                NativeMethods.SetWindowPlacement(_targetHwnd, in _originalPlacement);
            }
            else
            {
                NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero, 100, 100, 0, 0,
                    Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOZORDER);
            }

            NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_SHOW);
            NativeMethods.BringWindowToTop(_targetHwnd);
            NativeMethods.SetForegroundWindow(_targetHwnd);
            _isWindowHidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing window: {ex.Message}");
            try
            {
                NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_RESTORE);
                NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero, 100, 100, 800, 600,
                    Win32Constants.SWP_SHOWWINDOW);
                _isWindowHidden = false;
            }
            catch
            {
            }
        }
    }

    private void ToggleWindowVisibility()
    {
        if (!IsWindowValid(_targetHwnd))
        {
            MessageBox.Show("Target window is no longer valid.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_isWindowHidden)
        {
            ShowTargetWindow();
            RecreateThumbnail();
        }
        else
        {
            SaveOriginalPlacement();
            HideTargetWindow();
        }

        Activate();
        Focus();
        NativeMethods.BringWindowToTop(Handle);
        NativeMethods.SetForegroundWindow(Handle);
    }

    private void CleanupTargetWindow()
    {
        if (_targetHwnd == IntPtr.Zero)
        {
            return;
        }

        bool stillExists;
        try
        {
            stillExists = NativeMethods.IsWindow(_targetHwnd);
        }
        catch
        {
            stillExists = false;
        }

        if (!stillExists)
        {
            _isWindowHidden = false;
            return;
        }

        try
        {
            if (_isWindowHidden)
            {
                if (_hasOriginalPlacement)
                {
                    NativeMethods.SetWindowPlacement(_targetHwnd, in _originalPlacement);
                }
                else if (NativeMethods.GetWindowRect(_targetHwnd, out var cur))
                {
                    int w = Math.Max(200, cur.Width);
                    int h = Math.Max(150, cur.Height);
                    NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero, 100, 100, w, h,
                        Win32Constants.SWP_NOZORDER | Win32Constants.SWP_SHOWWINDOW);
                }
                else
                {
                    NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero, 100, 100, 0, 0,
                        Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOZORDER | Win32Constants.SWP_SHOWWINDOW);
                }
                _isWindowHidden = false;
            }

            if (_isTargetWindowMinimized)
            {
                NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_RESTORE);
                _isTargetWindowMinimized = false;
            }

            NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_SHOW);
            NativeMethods.BringWindowToTop(_targetHwnd);
            NativeMethods.SetForegroundWindow(_targetHwnd);

            HighlightTargetWindow(_targetHwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CleanupTargetWindow failed: {ex.Message}");
            try
            {
                NativeMethods.SetWindowPos(_targetHwnd, IntPtr.Zero, 100, 100, 0, 0,
                    Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOZORDER | Win32Constants.SWP_SHOWWINDOW);
                NativeMethods.ShowWindow(_targetHwnd, Win32Constants.SW_SHOW);
                _isWindowHidden = false;
            }
            catch
            {
            }
        }
    }

    private static void HighlightTargetWindow(IntPtr hwnd)
    {
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            {
                return;
            }

            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return;
            }
            try
            {
                _ = NativeMethods.DrawFocusRect(dc, ref rect);
            }
            finally
            {
                _ = NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }
        }
        catch
        {
        }
    }

    private void WatchdogTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsWindowValid(_targetHwnd))
        {
            _watchdogTimer?.Stop();
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(Close);
                }
                else
                {
                    Close();
                }
            }
            catch
            {
            }
            return;
        }

        if (_thumbnailHandle != IntPtr.Zero && _isWindowHidden)
        {
            try
            {
                UpdateMirrorPosition();
            }
            catch
            {
                RecreateThumbnail();
            }
        }
    }

    private void ApplyRoundedRegion()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int w = Math.Max(1, Width);
        int h = Math.Max(1, Height);

        if (_currentRegion != IntPtr.Zero && _lastRegionSize.Width == w && _lastRegionSize.Height == h)
        {
            return;
        }

        IntPtr rgn = NativeMethods.CreateRoundRectRgn(0, 0, w, h, 10, 10);
        if (rgn == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.SetWindowRgn(Handle, rgn, true) == 0)
        {
            NativeMethods.DeleteObject(rgn);
            return;
        }
        _currentRegion = rgn;
        _lastRegionSize = new Size(w, h);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedRegion();
        UpdateMirrorPosition();
        InvalidateBorderCache();
    }

    private void InvalidateBorderCache()
    {
        _cachedBorderPath?.Dispose();
        _cachedBorderPath = null;
        _cachedBorderPathSize = Size.Empty;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Alt && e.KeyCode == Keys.D1)
        {
            ToggleWindowVisibility();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.D2)
        {
            ApplyLayout(_wlo);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Oemtilde)
        {
            using var dlg = new SaveLayoutDialog(_windowTitle, _processName, this);
            dlg.ShowDialog(this);
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private static bool IsShiftDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Constants.VK_SHIFT) & 0x8000) != 0;
    }

    private static bool IsCtrlDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Constants.VK_CONTROL) & 0x8000) != 0;
    }

    private static bool IsAltDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Constants.VK_MENU) & 0x8000) != 0;
    }

    private void BeginSizeMove()
    {
        _inSizeMove = true;
        _baseWindowSize = Size;
        _baseWindowLocation = Location;
        _baseCaptureRect = _captureRect;
        _baseScaleX = _scaleX;
        _baseScaleY = _scaleY;
        _lastDeltaW = 0;
        _lastDeltaH = 0;
        _lastDeltaX = 0;
        _lastDeltaY = 0;
    }

    private void EndSizeMove()
    {
        _inSizeMove = false;
        _baseWindowSize = Size.Empty;
        _baseWindowLocation = Point.Empty;
        _baseCaptureRect = Rectangle.Empty;
        _lastDeltaW = 0;
        _lastDeltaH = 0;
        _lastDeltaX = 0;
        _lastDeltaY = 0;
        UpdateMirrorPosition();
    }

    private void ApplyModifiersToSizing(int edge, ref NativeMethods.RECT r)
    {
        if (!_inSizeMove || _baseWindowSize.IsEmpty)
        {
            return;
        }

        bool shift = IsShiftDown();
        bool ctrl = IsCtrlDown();

        int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

        if (shift && _baseWindowSize.Width > 0 && _baseWindowSize.Height > 0)
        {
            double aspect = (double)_baseWindowSize.Width / _baseWindowSize.Height;
            bool horizontal = edge is Win32Constants.WMSZ_LEFT or Win32Constants.WMSZ_RIGHT;
            bool vertical = edge is Win32Constants.WMSZ_TOP or Win32Constants.WMSZ_BOTTOM;

            int newW = right - left;
            int newH = bottom - top;

            if (horizontal)
            {
                newH = (int)Math.Round(newW / aspect);
                bottom = top + newH;
            }
            else if (vertical)
            {
                newW = (int)Math.Round(newH * aspect);
                right = left + newW;
            }
            else
            {
                double scaleW = (double)newW / _baseWindowSize.Width;
                double scaleH = (double)newH / _baseWindowSize.Height;
                double scale = Math.Max(scaleW, scaleH);
                newW = (int)Math.Round(_baseWindowSize.Width * scale);
                newH = (int)Math.Round(_baseWindowSize.Height * scale);

                switch (edge)
                {
                    case Win32Constants.WMSZ_BOTTOMRIGHT:
                        right = left + newW;
                        bottom = top + newH;
                        break;
                    case Win32Constants.WMSZ_BOTTOMLEFT:
                        left = right - newW;
                        bottom = top + newH;
                        break;
                    case Win32Constants.WMSZ_TOPRIGHT:
                        right = left + newW;
                        top = bottom - newH;
                        break;
                    case Win32Constants.WMSZ_TOPLEFT:
                        left = right - newW;
                        top = bottom - newH;
                        break;
                }
            }
        }

        if (ctrl)
        {
            int cx = (left + right) / 2;
            int cy = (top + bottom) / 2;
            int hw = (right - left) / 2;
            int hh = (bottom - top) / 2;
            left = cx - hw;
            right = cx + hw;
            top = cy - hh;
            bottom = cy + hh;
        }

        r.Left = left;
        r.Top = top;
        r.Right = right;
        r.Bottom = bottom;
    }

    private void UpdateCaptureRectFromWindow()
    {
        if (!_inSizeMove || _baseWindowSize.IsEmpty)
        {
            return;
        }

        bool alt = IsAltDown();
        bool shift = IsShiftDown();

        if (!alt)
        {
            _lastDeltaW = 0;
            _lastDeltaH = 0;
            _lastDeltaX = 0;
            _lastDeltaY = 0;
            _scaleX = _baseScaleX;
            _scaleY = _baseScaleY;
            return;
        }

        int deltaW = Width - _baseWindowSize.Width;
        int deltaH = Height - _baseWindowSize.Height;
        int deltaX = Location.X - _baseWindowLocation.X;
        int deltaY = Location.Y - _baseWindowLocation.Y;

        if (deltaW == _lastDeltaW && deltaH == _lastDeltaH &&
            deltaX == _lastDeltaX && deltaY == _lastDeltaY)
        {
            return;
        }

        _lastDeltaW = deltaW;
        _lastDeltaH = deltaH;
        _lastDeltaX = deltaX;
        _lastDeltaY = deltaY;

        if (shift)
        {
            double sx = _baseWindowSize.Width > 0
                ? (double)Width / _baseWindowSize.Width
                : 1.0;
            double sy = _baseWindowSize.Height > 0
                ? (double)Height / _baseWindowSize.Height
                : 1.0;

            _scaleX = _baseScaleX * sx;
            _scaleY = _baseScaleY * sy;

            int newW = Math.Max(1, (int)Math.Round(_baseCaptureRect.Width * sx));
            int newH = Math.Max(1, (int)Math.Round(_baseCaptureRect.Height * sy));

            int newX = _baseCaptureRect.X + deltaX;
            int newY = _baseCaptureRect.Y + deltaY;

            _captureRect = new Rectangle(newX, newY, newW, newH);
        }
        else
        {
            int newW = Math.Max(1, _baseCaptureRect.Width + deltaW);
            int newH = Math.Max(1, _baseCaptureRect.Height + deltaH);

            _scaleX = _baseCaptureRect.Width > 0
                ? _baseScaleX * ((double)newW / _baseCaptureRect.Width)
                : _baseScaleX;
            _scaleY = _baseCaptureRect.Height > 0
                ? _baseScaleY * ((double)newH / _baseCaptureRect.Height)
                : _baseScaleY;

            int newX = _baseCaptureRect.X + deltaX;
            int newY = _baseCaptureRect.Y + deltaY;

            _captureRect = new Rectangle(newX, newY, newW, newH);
        }
    }

    private void RestoreCaptureRectIfNeeded()
    {
        if (!_inSizeMove || _baseCaptureRect.IsEmpty)
        {
            return;
        }
        if (!IsAltDown())
        {
            _captureRect = _baseCaptureRect;
            _scaleX = _baseScaleX;
            _scaleY = _baseScaleY;
            _lastDeltaW = 0;
            _lastDeltaH = 0;
            _lastDeltaX = 0;
            _lastDeltaY = 0;
        }
    }

    private int GetResizeEdge(Point p)
    {
        const int tolerance = 15;
        bool left = p.X <= tolerance;
        bool right = p.X >= Width - tolerance;
        bool top = p.Y <= tolerance;
        bool bottom = p.Y >= Height - tolerance;

        if (left && top)
        {
            return 4;
        }
        if (right && top)
        {
            return 5;
        }
        if (left && bottom)
        {
            return 8;
        }
        if (right && bottom)
        {
            return 7;
        }
        if (left)
        {
            return 1;
        }
        if (right)
        {
            return 2;
        }
        if (top)
        {
            return 3;
        }
        if (bottom)
        {
            return 6;
        }
        return 0;
    }

    private static int GetHitTestFromEdge(int edge)
    {
        switch (edge)
        {
            case 1:
                return Win32Constants.HTLEFT;
            case 2:
                return Win32Constants.HTRIGHT;
            case 3:
                return Win32Constants.HTTOP;
            case 6:
                return Win32Constants.HTBOTTOM;
            case 4:
                return Win32Constants.HTTOPLEFT;
            case 5:
                return Win32Constants.HTTOPRIGHT;
            case 8:
                return Win32Constants.HTBOTTOMLEFT;
            case 7:
                return Win32Constants.HTBOTTOMRIGHT;
            default:
                return 0;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Win32Constants.WM_NCHITTEST:
                {
                    int x = unchecked((short)(long)m.LParam);
                    int y = unchecked((short)((long)m.LParam >> 16));
                    var p = PointToClient(new Point(x, y));

                    int hit = GetHitTestFromEdge(GetResizeEdge(p));
                    m.Result = hit != 0 ? hit : Win32Constants.HT_CAPTION;
                    return;
                }

            case Win32Constants.WM_ENTERSIZEMOVE:
                BeginSizeMove();
                break;

            case Win32Constants.WM_EXITSIZEMOVE:
                EndSizeMove();
                break;

            case Win32Constants.WM_SIZING:
                {
                    int edge = (int)m.WParam;
                    var r = Marshal.PtrToStructure<NativeMethods.RECT>(m.LParam);

                    ApplyModifiersToSizing(edge, ref r);
                    Marshal.StructureToPtr(r, m.LParam, false);

                    Size = new Size(r.Right - r.Left, r.Bottom - r.Top);
                    UpdateCaptureRectFromWindow();
                    RestoreCaptureRectIfNeeded();
                    UpdateMirrorPosition();

                    m.Result = IntPtr.Zero;
                    return;
                }

            case Win32Constants.WM_MOVING:
                {
                    UpdateCaptureRectFromWindow();
                    break;
                }

            case Win32Constants.WM_CLOSE:
            case Win32Constants.WM_QUIT:
            case Win32Constants.WM_ENDSESSION:
                try
                {
                    CleanupTargetWindow();
                }
                catch
                {
                }
                break;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_cachedBorderPath is null || _cachedBorderPathSize.Width != Width || _cachedBorderPathSize.Height != Height)
        {
            _cachedBorderPath?.Dispose();
            _cachedBorderPath = GetRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            _cachedBorderPathSize = new Size(Width, Height);
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(BorderPen, _cachedBorderPath);
    }

    private static GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}