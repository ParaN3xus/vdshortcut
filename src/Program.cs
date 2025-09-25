using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsDesktop;

partial class Program
{
    // WM constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_1 = 0x31;
    private const int VK_9 = 0x39;

    // Configuration
    private static Config? config;
    private static HashSet<int> switchKeyCodes = [];
    private static HashSet<int> moveKeyCodes = [];
    private static HashSet<int> pinKeyCodes = [];

    // State tracking
    private static readonly HashSet<int> pressedKeys = [];

    // hooks
    private static readonly LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    public class Config
    {
        [JsonPropertyName("switchKeys")]
        public int[] SwitchKeys { get; set; } = [];

        [JsonPropertyName("moveKeys")]
        public int[] MoveKeys { get; set; } = [];

        [JsonPropertyName("pinKeys")]
        public int[] PinKeys { get; set; } = [];
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExA", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [LibraryImport("user32.dll", EntryPoint = "UnhookWindowsHookExA", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll", EntryPoint = "CallNextHookEx", SetLastError = true)]
    private static partial IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleA",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller)
    )]
    private static partial IntPtr GetModuleHandle(string lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetMessageA")]
    private static partial int GetMessage(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax
    );

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessageA")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageA")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (!LoadConfig())
        {
            Console.WriteLine("Failed to load config.jsonc. Press any key to exit.");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("Virtual Desktop Switcher loaded with config:");
        Console.WriteLine(
            $"Switch keys: {string.Join(" + ", config!.SwitchKeys.Select(k => $"0x{k:X2}"))} + (1-9)"
        );
        Console.WriteLine(
            $"Move keys: {string.Join(" + ", config.MoveKeys.Select(k => $"0x{k:X2}"))} + (1-9)"
        );
        Console.WriteLine(
            $"Pin keys: {string.Join(" + ", config.PinKeys.Select(k => $"0x{k:X2}"))} + (1-9)"
        );
        Console.WriteLine();

        _hookID = SetHook(_proc);

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = false;
            UnhookWindowsHookEx(_hookID);
            Environment.Exit(0);
        };

        // loop
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static bool LoadConfig()
    {
        try
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.jsonc");

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                CreateDefaultConfig(configPath);
                Console.WriteLine($"Created default config at: {configPath}");
            }

            string jsonc = File.ReadAllText(configPath);

            // Remove comments from JSONC
            string json = RemoveJsonComments(jsonc);

            config = JsonSerializer.Deserialize<Config>(json);

            if (config == null)
            {
                Console.WriteLine("Failed to deserialize config");
                return false;
            }

            // Convert arrays to HashSets
            switchKeyCodes = [.. config.SwitchKeys];
            moveKeyCodes = [.. config.MoveKeys];
            pinKeyCodes = [.. config.PinKeys];

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            return false;
        }
    }

    private static string RemoveJsonComments(string jsonc)
    {
        var lines = jsonc.Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            // Skip lines that start with // (single line comments)
            if (trimmed.StartsWith("//"))
                continue;

            // Remove inline comments (// after content)
            int commentIndex = line.IndexOf("//");
            if (commentIndex >= 0)
            {
                // Check if // is inside a string
                bool inString = false;
                bool escaped = false;
                bool isComment = true;

                for (int i = 0; i < commentIndex; i++)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (line[i] == '\\')
                    {
                        escaped = true;
                    }
                    else if (line[i] == '"')
                    {
                        inString = !inString;
                    }
                }

                if (inString)
                    isComment = false;

                if (isComment)
                    result.Add(line.Substring(0, commentIndex));
                else
                    result.Add(line);
            }
            else
            {
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    private static void CreateDefaultConfig(string path)
    {
        string defaultConfig = """
            {
                // Switch desktop key combination (just switch to desktop)
                // F13 = 0x7C, LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5
                "switchKeys": [124], // F13 only (0x7C = 124)
                
                // Move window and switch key combination (move window to desktop and switch)
                // This should have more keys than switchKeys to have higher priority
                "moveKeys": [124, 160], // F13 + Left Shift (0x7C = 124, 0xA0 = 160)
                
                // Pin/Unpin current window key combination
                "pinKeys": [124, 83] // F13 + S (0x7C = 124, 0x53 = 83)
            }
            """;

        File.WriteAllText(path, defaultConfig);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool isKeyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isKeyUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            // Update pressed keys state
            if (isKeyDown)
            {
                pressedKeys.Add(vkCode);
            }
            else if (isKeyUp)
            {
                pressedKeys.Remove(vkCode);
            }

            // Check for pin key combination (check before number keys)
            if (isKeyDown && AreKeysPressed(pinKeyCodes))
            {
                RunOnSTAThread(() => TogglePinCurrentWindow());
                Console.WriteLine(
                    $"[{string.Join("+", config!.PinKeys.Select(k => $"0x{k:X2}"))}] toggled pin for current window"
                );
                return (IntPtr)1; // prevent default
            }
            // Check for number keys when pressed down
            if (isKeyDown && vkCode >= VK_1 && vkCode <= VK_9)
            {
                int desktopIndex = vkCode - VK_1; // 0-8

                // Check if move keys are pressed (priority over switch keys)
                if (AreKeysPressed(moveKeyCodes))
                {
                    RunOnSTAThread(() => MoveWindowToDesktop(desktopIndex));
                    RunOnSTAThread(() => SwitchToDesktop(desktopIndex));
                    Console.WriteLine(
                        $"[{string.Join("+", config!.MoveKeys.Select(k => $"0x{k:X2}"))}+{desktopIndex + 1}] moved focused window to desktop {desktopIndex + 1}"
                    );
                    return (IntPtr)1; // prevent default
                }
                // Check if switch keys are pressed
                else if (AreKeysPressed(switchKeyCodes))
                {
                    RunOnSTAThread(() => SwitchToDesktop(desktopIndex));
                    Console.WriteLine(
                        $"[{string.Join("+", config!.SwitchKeys.Select(k => $"0x{k:X2}"))}+{desktopIndex + 1}] switched to desktop {desktopIndex + 1}"
                    );
                    return (IntPtr)1; // prevent default
                }
            }
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static bool AreKeysPressed(HashSet<int> requiredKeys)
    {
        return requiredKeys.Count > 0 && requiredKeys.All(key => pressedKeys.Contains(key));
    }

    private static void RunOnSTAThread(Action action)
    {
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed: {ex.Message}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void TogglePinCurrentWindow()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd != IntPtr.Zero)
        {
            if (VirtualDesktop.IsPinnedWindow(hWnd))
            {
                VirtualDesktop.UnpinWindow(hWnd);
                Console.WriteLine("Window unpinned");
            }
            else
            {
                VirtualDesktop.PinWindow(hWnd);
                Console.WriteLine("Window pinned");
            }
        }
    }

    private static void SwitchToDesktop(int index)
    {
        var desktops = VirtualDesktop.GetDesktops();

        while (desktops.Length <= index)
        {
            VirtualDesktop.Create();
            desktops = VirtualDesktop.GetDesktops();
        }

        desktops[index].Switch();
    }

    private static void MoveWindowToDesktop(int index)
    {
        var desktops = VirtualDesktop.GetDesktops();

        while (desktops.Length <= index)
        {
            VirtualDesktop.Create();
            desktops = VirtualDesktop.GetDesktops();
        }

        IntPtr hWnd = GetForegroundWindow();
        if (hWnd != IntPtr.Zero)
        {
            VirtualDesktop.MoveToDesktop(hWnd, desktops[index]);
        }
    }
}
