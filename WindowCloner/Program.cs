using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

namespace WindowCloner
{
    internal static class Program
    {
        public static List<WindowLayoutObject> windowLayouts = new List<WindowLayoutObject>();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [STAThread]
        static void Main(string[] args)
        {
            Welcome? welcomeForm = null;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0)
            {
                FreeConsole();
                welcomeForm = new Welcome();
                Application.Run(welcomeForm);
            }
            else
            {
                try
                {
                    welcomeForm = new Welcome();

                    string programName = args[0].Substring(1);
                    Welcome.WindowItem? windowItem = null;
                    bool windowTitleInUse = args[0][0] == 'w';

                    welcomeForm.LoadPublic();
                    welcomeForm.RefreshWindowList();

                    if (args[0].StartsWith("-removePreset="))
                    {
                        string presetQuotedName = args[0].Split('=')[1];
                        string process = "";
                        if (HasLayoutOfName(presetQuotedName, out WindowLayoutObject? wlo) && wlo != null)
                        {
                            process = wlo.processName;
                            Console.WriteLine("Are you sure?\n");
                            Console.Write("(y/n) ");
                            ConsoleKey ansChar = Console.ReadKey().Key;
                            if (ansChar == ConsoleKey.Y)
                            {
                                windowLayouts.Remove(wlo);
                            }
                            else
                            {
                                Environment.Exit(0);
                            }
                            Console.WriteLine();
                        }
                        else
                        {
                            ConsoleWriteError($"Layout ({presetQuotedName}) not found, exiting the program!");
                        }
                        SaveLayoutFile();
                        ConsoleWriteWithColor($"Layout ({presetQuotedName}) for {process} has been removed sucessfully!", ConsoleColor.Green);
                        Environment.Exit(0);
                    }

                    switch (args[0][0])
                    {
                        default:
                            ConsoleWriteError("The type of the first argument must be specified before the first quote!\nExample:\n   WindowCloner e\"exename\" ...\n   \x1b[1mWindowCloner w\"Window Name\" ...");
                            break;
                        case 'w':
                            windowItem = welcomeForm.FindWindowItemByWindowName(programName);
                            break;
                        case 'e':
                            windowItem = welcomeForm.FindWindowItemByProcessName(programName);
                            break;
                    }
                    if (windowItem == null)
                    {
                        ConsoleWriteError($"Failed to find a window by {(windowTitleInUse ? "window title" : "process name")}: {programName}!");
                        return;
                    }
                    NativeMethods.GetWindowRect(windowItem.Handle, out NativeMethods.RECT windowRect);
                    Point screenCenter = GetScreenCenter(windowItem, windowRect.Width, windowRect.Height);

                                        WindowLayoutObject? windowLayoutObject = WindowLayoutObject.FromString(
                        $"cli{DateTime.Now.ToString("ddMMyyyyHHmm")}=" +
                        $"{windowItem.Title};{windowItem.Process};" +
                        $"{screenCenter.X};{screenCenter.Y};" +
                        $"{windowRect.Left};{windowRect.Top};" +
                        $"{windowRect.Width};{windowRect.Height};" +
                        $"{windowRect.Width};{windowRect.Height};" +
                        $"1.0;1.0;" +
                        $"{windowRect.Width};{windowRect.Height}");

                    if (args.Length > 1)
                    {
                        string layout = args[1].Substring(1);
                        switch (args[1][0])
                        {
                            default:
                                ConsoleWriteError(
                                    "The type of the second argument must be specified before the first quote!\n" +
                                    "Example:\n" +
                                    "   WindowCloner e\"exename\" p\"My Layout Preset\"\n" +
                                    "   WindowCloner w\"Window Name\" s\"-1;-1;100;50;0;0;1200;780\"\n" +
                                    "   WindowCloner w\"Window Name\" s\"-1;-1;100;50;0;0;1200;780;1.5;1.5\"");
                                break;
                            case 'p':
                                windowLayoutObject = windowLayouts.FirstOrDefault(l => l.name == layout);
                                break;
                            case 's':
                                string[] splits = layout.Split(';');

                                if (splits.Length != 6 && splits.Length != 8)
                                {
                                    ConsoleWriteError(
                                        "The amount of parameters in the second argument must be 6 or 8!\n" +
                                        "Format: locX;locY;rectX;rectY;rectW;rectH[;scaleX;scaleY]\n" +
                                        "Examples:\n" +
                                        "   WindowCloner w\"Window Name\" s\"-1;-1;100;50;0;0;1200;780\"\n" +
                                        "   WindowCloner w\"Window Name\" s\"-1;-1;100;50;0;0;1200;780;1.5;1.5\"");
                                    return;
                                }

                                double scaleX = 1.0;
                                double scaleY = 1.0;
                                if (splits.Length == 8)
                                {
                                    if (!double.TryParse(splits[6], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out scaleX) ||
                                        !double.TryParse(splits[7], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out scaleY))
                                    {
                                        ConsoleWriteError(
                                            "scaleX and scaleY must be valid decimal numbers " +
                                            "(use '.' as the decimal separator).");
                                    }
                                    if (scaleX <= 0 || scaleY <= 0)
                                    {
                                        ConsoleWriteError("scaleX and scaleY must be greater than 0.");
                                    }
                                }

                                if (splits[0] == "-1" && splits[1] == "-1")
                                {
                                    int.TryParse(splits[4], out int width);
                                    int.TryParse(splits[5], out int height);
                                    screenCenter = GetScreenCenter(windowItem, width, height);
                                    splits[0] = screenCenter.X.ToString();
                                    splits[1] = screenCenter.Y.ToString();
                                }

                                string wloLine =
                                    $"cli{DateTime.Now.ToString("ddMMyyyyHHmm")}=" +
                                    $"{windowItem.Title};{windowItem.Process};" +
                                    $"{splits[0]};{splits[1]};" +
                                    $"{splits[2]};{splits[3]};" +
                                    $"{splits[4]};{splits[5]};" +
                                    $"{splits[4]};{splits[5]};" +
                                    $"{scaleX.ToString(CultureInfo.InvariantCulture)};" +
                                    $"{scaleY.ToString(CultureInfo.InvariantCulture)};" +
                                    $"{windowRect.Width};{windowRect.Height}";

                                windowLayoutObject = WindowLayoutObject.FromString(wloLine);
                                break;
                        }
                    }

                    if (windowLayoutObject == null)
                    {
                        ConsoleWriteError("Failed to construct window layout.");
                        return;
                    }

                    if (args.Length > 2 && args[2].StartsWith("-createPreset="))
                    {
                        string presetQuotedName = args[2].Split('=')[1];
                        windowLayoutObject.name = presetQuotedName.Trim();
                        if (HasLayoutOfName(windowLayoutObject.name, out WindowLayoutObject? wlo) && wlo != null)
                        {
                            Console.WriteLine("A layout with that name already exists!\nDo you wish to replace it?\n");
                            Console.Write("(y/n) ");
                            ConsoleKey ansChar = Console.ReadKey().Key;
                            if (ansChar == ConsoleKey.Y)
                            {
                                windowLayouts.Remove(wlo);
                                windowLayouts.Add(windowLayoutObject);
                            }
                            else
                            {
                                Environment.Exit(0);
                            }
                            Console.WriteLine();
                        }
                        else
                        {
                            windowLayouts.Add(windowLayoutObject);
                        }
                        SaveLayoutFile();
                        ConsoleWriteWithColor($"Saved layout ({windowLayoutObject.name}) for {windowLayoutObject.processName} sucessfully!", ConsoleColor.Green);
                        Environment.Exit(0);
                    }
                    else
                    {
                        FreeConsole();
                        welcomeForm.CaptureWindow(windowItem, windowLayoutObject);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWriteError(ex.ToString());
                }
            }
        }
        public static void ConsoleWriteWithColor(object msg, ConsoleColor color)
        {
            ConsoleColor consoleColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg.ToString());
            Console.ForegroundColor = consoleColor;
        }
        public static Point GetScreenCenter(Welcome.WindowItem? window, int width, int height)
        {
            Rectangle bounds = window != null
                ? Screen.FromHandle(window.Handle).Bounds
                : Screen.PrimaryScreen!.Bounds;

            return new Point(
                bounds.Left + (bounds.Width - width) / 2,
                bounds.Top + (bounds.Height - height) / 2
            );
        }
        public static string GetProcessNameFromHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return "Invalid Handle";
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            if (pid == 0)
            {
                return "Process Not Found";
            }

            try
            {
                using (Process proc = Process.GetProcessById((int)pid))
                {
                    return proc.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return "Process Exited";
            }
        }
        public static void ConsoleWriteError(object msg)
        {
            ConsoleColor color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg.ToString());
            Console.ForegroundColor = color;
            Environment.Exit(-1);
        }
        public static void ReadLayoutFile()
        {
            windowLayouts.Clear();
            string path = Path.Combine(AppContext.BaseDirectory, "res", "layouts.txt");
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    WindowLayoutObject? wlo = WindowLayoutObject.FromString(line);
                    if (wlo != null)
                        windowLayouts.Add(wlo);
                }
            }
        }
        public static void SaveLayoutFile()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "layouts.txt");
            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < windowLayouts.Count; i++)
            {
                stringBuilder.AppendLine(windowLayouts[i].ToString());
            }
            File.WriteAllText(path, stringBuilder.ToString());
        }
        public static bool HasLayoutOfName(string name, out WindowLayoutObject? wlo)
        {
            wlo = windowLayouts.FirstOrDefault(l => l.name == name);
            return wlo != null;
        }
        public static void CenterToForm(Form form, Form? parent = null)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            parent ??= form.Owner;

            if (parent == null)
            {
                return;
            }

            if (!parent.Visible)
            {
                return;
            }

            var parentBounds = parent.WindowState == FormWindowState.Normal
                ? parent.Bounds
                : parent.RestoreBounds;

            int x = parentBounds.Left + (parentBounds.Width - form.Width) / 2;
            int y = parentBounds.Top + (parentBounds.Height - form.Height) / 2;

            var screen = Screen.FromControl(parent);
            var workingArea = screen.WorkingArea;

            x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - form.Width));
            y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - form.Height));

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(x, y);
        }
    }
}