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

    private bool _clickThrough;
    private bool _hotkeyRegistered;

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
    private int _opacity = 255;

    public Rectangle CaptureRect => _captureRect;
    public double CaptureScaleX => _scaleX;
    public double CaptureScaleY => _scaleY;
    public int CaptureWidth => Math.Max(1, _captureRect.Width);
    public int CaptureHeight => Math.Max(1, _captureRect.Height);

    public WindowLayoutObject? WindowLayoutObject => _wlo;
    public int Opacity => _opacity;
    public bool ClickThrough => _clickThrough;

    public Form1(
        WindowLayoutObject? wlo,
        Rectangle rect,
        IntPtr targetHwnd,
        string windowTitle,
        string processName,
        Rectangle captureRect)
    {
        _wlo = wlo;
        _targetHwnd = targetHwnd;
        _windowTitle = windowTitle;
        _processName = processName;

        _captureRect = rect;
        _baseCaptureRect = rect;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = false;
        BackColor = Color.Black;
        TopMost = true;
        Text = "Window Cloner";
        ShowInTaskbar = false;
        KeyPreview = true;

        Size = new Size(
            Math.Max(1, captureRect.Width),
            Math.Max(1, captureRect.Height));
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Constants.WS_EX_TOOLWINDOW;
            cp.ExStyle |= Win32Constants.WS_EX_LAYERED;
            return cp;
        }
    }

    public void ApplyLayout(WindowLayoutObject wlo)
    {
        Location = new Point(wlo.locationX, wlo.locationY);

        Size = new Size(
            Math.Max(1, wlo.width),
            Math.Max(1, wlo.height));

        int cw = wlo.rectW > 0 ? wlo.rectW : wlo.width;
        int ch = wlo.rectH > 0 ? wlo.rectH : wlo.height;

        _captureRect = new Rectangle(
            new Point(wlo.rectX, wlo.rectY),
            new Size(
                Math.Max(1, cw),
                Math.Max(1, ch)));

        _scaleX = wlo.scaleX > 0 ? wlo.scaleX : 1.0;
        _scaleY = wlo.scaleY > 0 ? wlo.scaleY : 1.0;

        _baseScaleX = _scaleX;
        _baseScaleY = _scaleY;

        _opacity = wlo.opacity > 0 ? wlo.opacity : 255;

        UpdateMirrorPosition();
        RefreshOpacity();
        ApplyRoundedRegion();
        InvalidateBorderCache();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedRegion();
        RefreshOpacity();

        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            Win32Constants.HOTKEY_ID_TOGGLE_CLICKTHROUGH,
            Win32Constants.MOD_ALT | Win32Constants.MOD_NOREPEAT,
            Win32Constants.VK_3);

        if (!_hotkeyRegistered)
        {
            Debug.WriteLine("RegisterHotKey(Alt+3) failed: " +
                Marshal.GetLastWin32Error());
        }
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
            MessageBox.Show(
                "DWM composition is not enabled.",
                "DWM Not Available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            Close();
            return;
        }

        if (!IsWindowValid(_targetHwnd))
        {
            MessageBox.Show(
                $"Target window '{_windowTitle}' is no longer valid.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
            return;
        }

        InitializeSystemTray();
        SaveOriginalPlacement();

        _isTargetWindowMinimized = NativeMethods.IsIconic(_targetHwnd);

        if (_isTargetWindowMinimized)
        {
            NativeMethods.ShowWindow(
                _targetHwnd,
                Win32Constants.SW_RESTORE);

            Thread.Sleep(100);
        }

        HighlightTargetWindow(_targetHwnd);

        if (CreateThumbnail())
        {
            HideTargetWindow();

            _watchdogTimer = new System.Windows.Forms.Timer
            {
                Interval = 3000
            };

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
                "Thumbnail Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (_wlo is not null)
        {
            ApplyLayout(_wlo);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var timer = new System.Windows.Forms.Timer
        {
            Interval = 150
        };

        timer.Tick += (s, _) =>
        {
            timer.Stop();
            timer.Dispose();
            ToggleWindowVisibility();
        };

        timer.Start();
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

        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(
                Handle,
                Win32Constants.HOTKEY_ID_TOGGLE_CLICKTHROUGH);
            _hotkeyRegistered = false;
        }

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
            Span<char> buffer = stackalloc char[128];

            return NativeMethods.GetWindowText(
                hwnd,
                buffer,
                buffer.Length) > 0;
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
            int hr = NativeMethods.DwmRegisterThumbnail(
                Handle,
                _targetHwnd,
                out _thumbnailHandle);

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
            int cw = Math.Max(1, ClientRectangle.Width);
            int ch = Math.Max(1, ClientRectangle.Height);

            int captureWidth = Math.Max(1, _captureRect.Width);
            int captureHeight = Math.Max(1, _captureRect.Height);

            var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags =
                    Win32Constants.DWM_TNP_RECTDESTINATION |
                    Win32Constants.DWM_TNP_RECTSOURCE |
                    Win32Constants.DWM_TNP_VISIBLE |
                    Win32Constants.DWM_TNP_SOURCECLIENTAREAONLY,

                fVisible = 1,
                fSourceClientAreaOnly = 1,
                opacity = (byte)_opacity,

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
                    Right = _captureRect.X + captureWidth,
                    Bottom = _captureRect.Y + captureHeight
                }
            };

            if (_hasThumbProps &&
                ThumbPropsEqual(in _lastThumbProps, in props))
            {
                return;
            }

            _ = NativeMethods.DwmUpdateThumbnailProperties(
                _thumbnailHandle,
                ref props);

            _lastThumbProps = props;
            _hasThumbProps = true;
        }
        catch
        {
        }
    }

    private static bool ThumbPropsEqual(
        in NativeMethods.DWM_THUMBNAIL_PROPERTIES a,
        in NativeMethods.DWM_THUMBNAIL_PROPERTIES b)
    {
        return a.dwFlags == b.dwFlags
            && a.fVisible == b.fVisible
            && a.fSourceClientAreaOnly == b.fSourceClientAreaOnly
            && a.opacity == b.opacity
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

            _hasOriginalPlacement =
                NativeMethods.GetWindowPlacement(
                    _targetHwnd,
                    ref _originalPlacement);
        }
        catch
        {
            _hasOriginalPlacement = false;
        }
    }

    private void HideTargetWindow()
    {
        if (_targetHwnd == IntPtr.Zero ||
            _isWindowHidden ||
            !IsWindowValid(_targetHwnd))
        {
            return;
        }

        try
        {
            NativeMethods.SetWindowPos(
                _targetHwnd,
                IntPtr.Zero,
                Win32Constants.OFFSCREEN_X,
                Win32Constants.OFFSCREEN_Y,
                0,
                0,
                Win32Constants.SWP_NOSIZE |
                Win32Constants.SWP_NOZORDER);

            _isWindowHidden = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error hiding window: {ex.Message}");
        }
    }

    private void ShowTargetWindow()
    {
        if (_targetHwnd == IntPtr.Zero ||
            !_isWindowHidden ||
            !IsWindowValid(_targetHwnd))
        {
            _isWindowHidden = false;
            return;
        }

        try
        {
            if (_hasOriginalPlacement)
            {
                NativeMethods.SetWindowPlacement(
                    _targetHwnd,
                    in _originalPlacement);
            }
            else
            {
                NativeMethods.SetWindowPos(
                    _targetHwnd,
                    IntPtr.Zero,
                    100,
                    100,
                    0,
                    0,
                    Win32Constants.SWP_NOSIZE |
                    Win32Constants.SWP_NOZORDER);
            }

            NativeMethods.ShowWindow(
                _targetHwnd,
                Win32Constants.SW_SHOW);

            NativeMethods.BringWindowToTop(_targetHwnd);
            NativeMethods.SetForegroundWindow(_targetHwnd);

            _isWindowHidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing window: {ex.Message}");

            try
            {
                NativeMethods.ShowWindow(
                    _targetHwnd,
                    Win32Constants.SW_RESTORE);

                NativeMethods.SetWindowPos(
                    _targetHwnd,
                    IntPtr.Zero,
                    100,
                    100,
                    800,
                    600,
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
            MessageBox.Show(
                "Target window is no longer valid.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

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
                    NativeMethods.SetWindowPlacement(
                        _targetHwnd,
                        in _originalPlacement);
                }
                else if (NativeMethods.GetWindowRect(
                    _targetHwnd,
                    out var currentRect))
                {
                    int width = Math.Max(200, currentRect.Width);
                    int height = Math.Max(150, currentRect.Height);

                    NativeMethods.SetWindowPos(
                        _targetHwnd,
                        IntPtr.Zero,
                        100,
                        100,
                        width,
                        height,
                        Win32Constants.SWP_NOZORDER |
                        Win32Constants.SWP_SHOWWINDOW);
                }
                else
                {
                    NativeMethods.SetWindowPos(
                        _targetHwnd,
                        IntPtr.Zero,
                        100,
                        100,
                        0,
                        0,
                        Win32Constants.SWP_NOSIZE |
                        Win32Constants.SWP_NOZORDER |
                        Win32Constants.SWP_SHOWWINDOW);
                }

                _isWindowHidden = false;
            }

            if (_isTargetWindowMinimized)
            {
                NativeMethods.ShowWindow(
                    _targetHwnd,
                    Win32Constants.SW_RESTORE);

                _isTargetWindowMinimized = false;
            }

            NativeMethods.ShowWindow(
                _targetHwnd,
                Win32Constants.SW_SHOW);

            NativeMethods.BringWindowToTop(_targetHwnd);
            NativeMethods.SetForegroundWindow(_targetHwnd);

            HighlightTargetWindow(_targetHwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"CleanupTargetWindow failed: {ex.Message}");

            try
            {
                NativeMethods.SetWindowPos(
                    _targetHwnd,
                    IntPtr.Zero,
                    100,
                    100,
                    0,
                    0,
                    Win32Constants.SWP_NOSIZE |
                    Win32Constants.SWP_NOZORDER |
                    Win32Constants.SWP_SHOWWINDOW);

                NativeMethods.ShowWindow(
                    _targetHwnd,
                    Win32Constants.SW_SHOW);

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
            if (!NativeMethods.GetWindowRect(
                hwnd,
                out NativeMethods.RECT rect))
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
                _ = NativeMethods.DrawFocusRect(
                    dc,
                    ref rect);
            }
            finally
            {
                _ = NativeMethods.ReleaseDC(
                    IntPtr.Zero,
                    dc);
            }
        }
        catch
        {
        }
    }

    private void WatchdogTimer_Tick(
        object? sender,
        EventArgs e)
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

        if (_thumbnailHandle != IntPtr.Zero &&
            _isWindowHidden)
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

        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);

        if (_currentRegion != IntPtr.Zero &&
            _lastRegionSize.Width == width &&
            _lastRegionSize.Height == height)
        {
            return;
        }

        IntPtr region = NativeMethods.CreateRoundRectRgn(
            0,
            0,
            width,
            height,
            10,
            10);

        if (region == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.SetWindowRgn(
            Handle,
            region,
            true) == 0)
        {
            NativeMethods.DeleteObject(region);
            return;
        }

        _currentRegion = region;
        _lastRegionSize = new Size(width, height);
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
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.D2)
        {
            if (_wlo is not null)
            {
                ApplyLayout(_wlo);
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Oemtilde)
        {
            using var dialog =
                new SaveLayoutDialog(
                    _windowTitle,
                    _processName,
                    this);

            dialog.ShowDialog(this);

            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Alt &&
                 (e.KeyCode == Keys.Oemplus ||
                  e.KeyCode == Keys.Add))
        {
            AdjustOpacity(10);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Alt &&
                 (e.KeyCode == Keys.OemMinus ||
                  e.KeyCode == Keys.Subtract))
        {
            AdjustOpacity(-10);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void AdjustOpacity(int delta)
    {
        _opacity = Math.Max(
            10,
            Math.Min(
                255,
                _opacity + delta));

        if (_wlo is not null)
        {
            _wlo.opacity = _opacity;
        }

        RefreshOpacity();
        UpdateMirrorPosition();
    }

    private void RefreshOpacity()
    {
        if (IsHandleCreated)
        {
            NativeMethods.SetLayeredWindowAttributes(
                Handle,
                0,
                (byte)_opacity,
                Win32Constants.LWA_ALPHA);
        }
    }

    private static bool IsShiftDown()
    {
        return (NativeMethods.GetAsyncKeyState(
            Win32Constants.VK_SHIFT) & 0x8000) != 0;
    }

    private static bool IsCtrlDown()
    {
        return (NativeMethods.GetAsyncKeyState(
            Win32Constants.VK_CONTROL) & 0x8000) != 0;
    }

    private static bool IsAltDown()
    {
        return (NativeMethods.GetAsyncKeyState(
            Win32Constants.VK_MENU) & 0x8000) != 0;
    }

    private void BeginSizeMove()
    {
        if (!NativeMethods.GetWindowRect(
            Handle,
            out NativeMethods.RECT rect))
        {
            return;
        }

        _inSizeMove = true;

        _baseWindowLocation = new Point(
            rect.Left,
            rect.Top);

        _baseWindowSize = new Size(
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));

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

    private void ApplyModifiersToSizing(
        int edge,
        ref NativeMethods.RECT rect)
    {
        if (!_inSizeMove ||
            _baseWindowSize.IsEmpty)
        {
            return;
        }

        bool shift = IsShiftDown();
        bool ctrl = IsCtrlDown();

        int left = rect.Left;
        int top = rect.Top;
        int right = rect.Right;
        int bottom = rect.Bottom;

        if (shift &&
            _baseWindowSize.Width > 0 &&
            _baseWindowSize.Height > 0)
        {
            double aspect =
                (double)_baseWindowSize.Width /
                _baseWindowSize.Height;

            bool horizontal =
                edge == Win32Constants.WMSZ_LEFT ||
                edge == Win32Constants.WMSZ_RIGHT;

            bool vertical =
                edge == Win32Constants.WMSZ_TOP ||
                edge == Win32Constants.WMSZ_BOTTOM;

            int newWidth = right - left;
            int newHeight = bottom - top;

            if (horizontal)
            {
                newWidth = Math.Max(1, newWidth);
                newHeight = Math.Max(
                    1,
                    (int)Math.Round(newWidth / aspect));

                if (edge == Win32Constants.WMSZ_LEFT)
                {
                    left = right - newWidth;
                }
                else
                {
                    right = left + newWidth;
                }

                bottom = top + newHeight;
            }
            else if (vertical)
            {
                newHeight = Math.Max(1, newHeight);
                newWidth = Math.Max(
                    1,
                    (int)Math.Round(newHeight * aspect));

                if (edge == Win32Constants.WMSZ_TOP)
                {
                    top = bottom - newHeight;
                }
                else
                {
                    bottom = top + newHeight;
                }

                right = left + newWidth;
            }
            else
            {
                double scaleW =
                    (double)Math.Max(1, newWidth) /
                    _baseWindowSize.Width;

                double scaleH =
                    (double)Math.Max(1, newHeight) /
                    _baseWindowSize.Height;

                double scale = Math.Max(
                    scaleW,
                    scaleH);

                newWidth = Math.Max(
                    1,
                    (int)Math.Round(
                        _baseWindowSize.Width * scale));

                newHeight = Math.Max(
                    1,
                    (int)Math.Round(
                        _baseWindowSize.Height * scale));

                switch (edge)
                {
                    case Win32Constants.WMSZ_BOTTOMRIGHT:
                        right = left + newWidth;
                        bottom = top + newHeight;
                        break;

                    case Win32Constants.WMSZ_BOTTOMLEFT:
                        left = right - newWidth;
                        bottom = top + newHeight;
                        break;

                    case Win32Constants.WMSZ_TOPRIGHT:
                        right = left + newWidth;
                        top = bottom - newHeight;
                        break;

                    case Win32Constants.WMSZ_TOPLEFT:
                        left = right - newWidth;
                        top = bottom - newHeight;
                        break;
                }
            }
        }

        if (ctrl)
        {
            int centerX = (left + right) / 2;
            int centerY = (top + bottom) / 2;

            int halfWidth = Math.Max(
                1,
                (right - left) / 2);

            int halfHeight = Math.Max(
                1,
                (bottom - top) / 2);

            left = centerX - halfWidth;
            right = centerX + halfWidth;

            top = centerY - halfHeight;
            bottom = centerY + halfHeight;
        }

        if (right <= left)
        {
            right = left + 1;
        }

        if (bottom <= top)
        {
            bottom = top + 1;
        }

        rect.Left = left;
        rect.Top = top;
        rect.Right = right;
        rect.Bottom = bottom;
    }

    private void UpdateCaptureRectFromBounds(
        int x,
        int y,
        int width,
        int height)
    {
        if (!_inSizeMove ||
            _baseWindowSize.IsEmpty)
        {
            return;
        }

        bool alt = IsAltDown();
        bool shift = IsShiftDown();

        int deltaW = width - _baseWindowSize.Width;
        int deltaH = height - _baseWindowSize.Height;

        int deltaX = x - _baseWindowLocation.X;
        int deltaY = y - _baseWindowLocation.Y;

        if (deltaW == _lastDeltaW &&
            deltaH == _lastDeltaH &&
            deltaX == _lastDeltaX &&
            deltaY == _lastDeltaY)
        {
            return;
        }

        _lastDeltaW = deltaW;
        _lastDeltaH = deltaH;
        _lastDeltaX = deltaX;
        _lastDeltaY = deltaY;

        if (!alt)
        {
            _captureRect = _baseCaptureRect;
            _scaleX = _baseScaleX;
            _scaleY = _baseScaleY;
            return;
        }

        if (shift)
        {
            double sx =
                _baseWindowSize.Width > 0
                    ? (double)width / _baseWindowSize.Width
                    : 1.0;

            double sy =
                _baseWindowSize.Height > 0
                    ? (double)height / _baseWindowSize.Height
                    : 1.0;

            _scaleX = _baseScaleX * sx;
            _scaleY = _baseScaleY * sy;

            int newWidth = Math.Max(
                1,
                (int)Math.Round(
                    _baseCaptureRect.Width * sx));

            int newHeight = Math.Max(
                1,
                (int)Math.Round(
                    _baseCaptureRect.Height * sy));

            int newX =
                _baseCaptureRect.X + deltaX;

            int newY =
                _baseCaptureRect.Y + deltaY;

            _captureRect = new Rectangle(
                newX,
                newY,
                newWidth,
                newHeight);
        }
        else
        {
            int newWidth = Math.Max(
                1,
                _baseCaptureRect.Width + deltaW);

            int newHeight = Math.Max(
                1,
                _baseCaptureRect.Height + deltaH);

            _scaleX =
                _baseCaptureRect.Width > 0
                    ? _baseScaleX *
                      ((double)newWidth /
                       _baseCaptureRect.Width)
                    : _baseScaleX;

            _scaleY =
                _baseCaptureRect.Height > 0
                    ? _baseScaleY *
                      ((double)newHeight /
                       _baseCaptureRect.Height)
                    : _baseScaleY;

            int newX =
                _baseCaptureRect.X + deltaX;

            int newY =
                _baseCaptureRect.Y + deltaY;

            _captureRect = new Rectangle(
                newX,
                newY,
                newWidth,
                newHeight);
        }

        if (_wlo is not null)
        {
            _wlo.rectX = _captureRect.X;
            _wlo.rectY = _captureRect.Y;
            _wlo.rectW = _captureRect.Width;
            _wlo.rectH = _captureRect.Height;
            _wlo.scaleX = _scaleX;
            _wlo.scaleY = _scaleY;
        }

        UpdateMirrorPosition();
    }

    private void RestoreCaptureRectIfNeeded()
    {
        if (!_inSizeMove ||
            _baseCaptureRect.IsEmpty)
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

            UpdateMirrorPosition();
        }
    }

    private int GetResizeEdge(Point point)
    {
        const int tolerance = 15;

        int width = ClientSize.Width;
        int height = ClientSize.Height;

        bool left = point.X <= tolerance;
        bool right = point.X >= width - tolerance;
        bool top = point.Y <= tolerance;
        bool bottom = point.Y >= height - tolerance;

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
                    int screenX = unchecked(
                        (short)(long)m.LParam);

                    int screenY = unchecked(
                        (short)((long)m.LParam >> 16));

                    Point clientPoint =
                        PointToClient(
                            new Point(
                                screenX,
                                screenY));

                    int edge =
                        GetResizeEdge(clientPoint);

                    int hit =
                        GetHitTestFromEdge(edge);

                    m.Result =
                        hit != 0
                            ? hit
                            : Win32Constants.HT_CAPTION;

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

                    var rect =
                        Marshal.PtrToStructure<
                            NativeMethods.RECT>(
                            m.LParam);

                    ApplyModifiersToSizing(
                        edge,
                        ref rect);

                    Marshal.StructureToPtr(
                        rect,
                        m.LParam,
                        false);

                    int width =
                        Math.Max(
                            1,
                            rect.Right - rect.Left);

                    int height =
                        Math.Max(
                            1,
                            rect.Bottom - rect.Top);

                    UpdateCaptureRectFromBounds(
                        rect.Left,
                        rect.Top,
                        width,
                        height);

                    m.Result = IntPtr.Zero;
                    return;
                }

            case Win32Constants.WM_MOVING:
                {
                    var rect =
                        Marshal.PtrToStructure<
                            NativeMethods.RECT>(
                            m.LParam);

                    int width =
                        Math.Max(
                            1,
                            rect.Right - rect.Left);

                    int height =
                        Math.Max(
                            1,
                            rect.Bottom - rect.Top);

                    UpdateCaptureRectFromBounds(
                        rect.Left,
                        rect.Top,
                        width,
                        height);

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
            case Win32Constants.WM_HOTKEY:
                {
                    int id = m.WParam.ToInt32();
                    if (id == Win32Constants.HOTKEY_ID_TOGGLE_CLICKTHROUGH)
                    {
                        ToggleClickThrough();
                        return;
                    }
                    break;
                }
        }

        base.WndProc(ref m);
    }

    private void ToggleClickThrough()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        _clickThrough = !_clickThrough;

        IntPtr exStyle = NativeMethods.GetWindowLongPtr(
            Handle,
            Win32Constants.GWL_EXSTYLE);

        long style = exStyle.ToInt64();

        if (_clickThrough)
        {
            style |= Win32Constants.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~(long)Win32Constants.WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLongPtr(
            Handle,
            Win32Constants.GWL_EXSTYLE,
            new IntPtr(style));

        NativeMethods.SetWindowPos(
            Handle,
            IntPtr.Zero,
            0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE |
            Win32Constants.SWP_NOSIZE |
            Win32Constants.SWP_NOZORDER |
            Win32Constants.SWP_NOACTIVATE |
            Win32Constants.SWP_FRAMECHANGED);

        if (_notifyIcon is not null)
        {
            _notifyIcon.Text = _clickThrough
                ? "Window Cloner (click-through)"
                : "Window Cloner";
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_cachedBorderPath is null ||
            _cachedBorderPathSize.Width != Width ||
            _cachedBorderPathSize.Height != Height)
        {
            _cachedBorderPath?.Dispose();

            _cachedBorderPath =
                GetRoundedRectangle(
                    new Rectangle(
                        0,
                        0,
                        Math.Max(1, Width - 1),
                        Math.Max(1, Height - 1)),
                    10);

            _cachedBorderPathSize =
                new Size(
                    Width,
                    Height);
        }

        e.Graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        if (_cachedBorderPath is not null)
        {
            e.Graphics.DrawPath(
                BorderPen,
                _cachedBorderPath);
        }
    }

    private static GraphicsPath GetRoundedRectangle(
        Rectangle rect,
        int radius)
    {
        var path = new GraphicsPath();

        int d = radius * 2;

        int width = Math.Max(
            d,
            rect.Width);

        int height = Math.Max(
            d,
            rect.Height);

        path.AddArc(
            rect.X,
            rect.Y,
            d,
            d,
            180,
            90);

        path.AddArc(
            rect.X + width - d,
            rect.Y,
            d,
            d,
            270,
            90);

        path.AddArc(
            rect.X + width - d,
            rect.Y + height - d,
            d,
            d,
            0,
            90);

        path.AddArc(
            rect.X,
            rect.Y + height - d,
            d,
            d,
            90,
            90);

        path.CloseFigure();

        return path;
    }
}