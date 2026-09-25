using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

namespace WindowCloner
{
    public partial class Welcome : Form
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
        }

        private const int SW_RESTORE = 9;

        public class WindowItem
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Process { get; set; } = string.Empty;
            public override string ToString() => Title;
        }

        private System.Windows.Forms.Timer? _refreshTimer;
        private uint _currentPid;
        private WindowItem? _selectedWindow;
        private Rectangle _selectedRect;

        public Welcome()
        {
            InitializeComponent();

            _currentPid = (uint)Process.GetCurrentProcess().Id;

            btnSelect.Click += BtnSelect_Click;
            listBoxWindows.SelectedIndexChanged += (s, e) => btnSelect.Enabled = listBoxWindows.SelectedItem != null;

            listBoxWindows.KeyDown += ListBoxWindows_KeyDown;

            Load += Welcome_Load;
            FormClosing += Welcome_FormClosing;

            ShowWindowList();
        }

        private void ListBoxWindows_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                if (listBoxWindows.SelectedItem is WindowItem)
                {
                    BtnSelect_Click(btnSelect, EventArgs.Empty);
                }
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = false;
            }
        }

        private void Welcome_Load(object? sender, EventArgs e)
        {
            LoadPublic();
        }

        public void LoadPublic()
        {
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, a) => RefreshWindowList();
            _refreshTimer.Start();
            RefreshWindowList();

            Program.ReadLayoutFile();

            this.ActiveControl = listBoxWindows;
        }

        internal WindowItem? FindWindowItemByWindowName(string windowName)
        {
            for (int i = 0; i < listBoxWindows.Items.Count; i++)
            {
                if (listBoxWindows.Items[i]?.ToString() == windowName)
                {
                    return listBoxWindows.Items[i] as WindowItem;
                }
            }
            return null;
        }

        internal WindowItem? FindWindowItemByProcessName(string procName)
        {
            for (int i = 0; i < listBoxWindows.Items.Count; i++)
            {
                WindowItem? windowItem = listBoxWindows.Items[i] as WindowItem;
                if (windowItem != null && windowItem.Process == procName)
                {
                    return windowItem;
                }
            }
            return null;
        }

        internal void RefreshWindowList()
        {
            var selectedItem = listBoxWindows.SelectedItem as WindowItem;
            IntPtr previouslySelectedHandle = selectedItem?.Handle ?? IntPtr.Zero;

            listBoxWindows.Items.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    GetWindowThreadProcessId(hWnd, out uint windowPid);

                    if (!string.IsNullOrEmpty(title) && windowPid != _currentPid && title != "Program Manager")
                    {
                        var item = new WindowItem { Handle = hWnd, Title = title, Process = Program.GetProcessNameFromHwnd(hWnd) };
                        int index = listBoxWindows.Items.Add(item);

                        if (hWnd == previouslySelectedHandle)
                        {
                            listBoxWindows.SelectedIndex = index;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (listBoxWindows.SelectedIndex < 0 && listBoxWindows.Items.Count > 0)
            {
                listBoxWindows.SelectedIndex = 0;
            }
        }

        private void BtnSelect_Click(object? sender, EventArgs e)
        {
            if (listBoxWindows.SelectedItem is WindowItem selected)
            {
                CaptureWindow(selected);
            }
        }

        internal void CaptureWindow(WindowItem window, WindowLayoutObject? overrideWlo = null)
        {
            _selectedWindow = window;

            if (IsIconic(window.Handle))
            {
                ShowWindow(window.Handle, SW_RESTORE);
                Thread.Sleep(100);
                BringWindowToTop(window.Handle);
                SetForegroundWindow(window.Handle);
                Thread.Sleep(200);
            }

            if (overrideWlo != null)
            {
                _selectedRect = overrideWlo.GetRectangle(window);
                StartCapture(overrideWlo, _selectedRect);
                return;
            }
            Bitmap? screenshot = WindowCapture32.CaptureWindow(window.Handle);
            if (screenshot != null)
            {
                using (var selector = new RectangleSelector(window.Handle, window.Title, window.Process, screenshot))
                {
                    if (selector.ShowDialog() == DialogResult.OK)
                    {
                        _selectedRect = selector.SelectedRect;
                        StartCapture(selector.SelectedLayout, selector.SelectedRect);
                    }
                }
            }
            else
            {
                MessageBox.Show("Failed to capture window. Please make sure the window is visible.",
                    "Capture Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StartCapture(WindowLayoutObject? wlo, Rectangle rect)
        {
            if (_selectedWindow != null && !_selectedRect.IsEmpty)
            {
                _refreshTimer?.Stop();
                Hide();

                Form1 mirrorForm = new Form1(wlo, rect, _selectedWindow.Handle, _selectedWindow.Title, _selectedWindow.Process, _selectedRect);
                mirrorForm.ShowDialog();
                Close();
            }
        }

        private void ShowWindowList()
        {
            listBoxWindows.Visible = true;
            btnSelect.Visible = true;
            lblTitle.Text = "Select a window to capture:";
            Text = "Window Cloner";
        }

        private void Welcome_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _refreshTimer?.Stop();
        }
    }
}