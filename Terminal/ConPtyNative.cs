using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wmux.Terminal;

internal static class ConPtyNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadConsoleInputW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadConsoleInput(
        IntPtr hConsoleInput,
        [Out] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfConsoleInputEvents(
        IntPtr hConsoleInput,
        out uint lpcNumberOfEvents);

    internal const int STD_INPUT_HANDLE = -10;
    internal const int STD_OUTPUT_HANDLE = -11;

    // Input mode flags
    internal const uint ENABLE_PROCESSED_INPUT        = 0x0001;
    internal const uint ENABLE_LINE_INPUT              = 0x0002;
    internal const uint ENABLE_ECHO_INPUT              = 0x0004;
    internal const uint ENABLE_WINDOW_INPUT            = 0x0008;
    internal const uint ENABLE_MOUSE_INPUT             = 0x0010;
    internal const uint ENABLE_QUICK_EDIT_MODE         = 0x0040;
    internal const uint ENABLE_EXTENDED_FLAGS          = 0x0080;
    internal const uint ENABLE_VIRTUAL_TERMINAL_INPUT  = 0x0200;

    // Output mode flags
    internal const uint ENABLE_PROCESSED_OUTPUT            = 0x0001;
    internal const uint ENABLE_WRAP_AT_EOL_OUTPUT          = 0x0002;
    internal const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    internal const uint DISABLE_NEWLINE_AUTO_RETURN        = 0x0008;

    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016; // 131094
    internal const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    // ── Console input event types ───────────────────────────────────

    internal const ushort KEY_EVENT = 0x0001;
    internal const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

    // Virtual-key constants used by KEY_EVENT_RECORD
    internal const int VK_BACK      = 0x08;
    internal const int VK_TAB       = 0x09;
    internal const int VK_RETURN    = 0x0D;
    internal const int VK_ESCAPE    = 0x1B;
    internal const int VK_PRIOR     = 0x21; // Page Up
    internal const int VK_NEXT      = 0x22; // Page Down
    internal const int VK_END       = 0x23;
    internal const int VK_HOME      = 0x24;
    internal const int VK_LEFT      = 0x25;
    internal const int VK_UP        = 0x26;
    internal const int VK_RIGHT     = 0x27;
    internal const int VK_DOWN      = 0x28;
    internal const int VK_INSERT    = 0x2D;
    internal const int VK_DELETE    = 0x2E;
    internal const int VK_F1        = 0x70;
    internal const int VK_F2        = 0x71;
    internal const int VK_F3        = 0x72;
    internal const int VK_F4        = 0x73;
    internal const int VK_F5        = 0x74;
    internal const int VK_F6        = 0x75;
    internal const int VK_F7        = 0x76;
    internal const int VK_F8        = 0x77;
    internal const int VK_F9        = 0x78;
    internal const int VK_F10       = 0x79;
    internal const int VK_F11       = 0x7A;
    internal const int VK_F12       = 0x7B;

    // Control key state flags from KEY_EVENT_RECORD.dwControlKeyState
    internal const uint RIGHT_ALT_PRESSED  = 0x0001;
    internal const uint LEFT_ALT_PRESSED   = 0x0002;
    internal const uint RIGHT_CTRL_PRESSED = 0x0004;
    internal const uint LEFT_CTRL_PRESSED  = 0x0008;
    internal const uint SHIFT_PRESSED      = 0x0010;

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    internal struct KEY_EVENT_RECORD
    {
        [FieldOffset(0)]
        public int bKeyDown;          // BOOL
        [FieldOffset(4)]
        public ushort wRepeatCount;
        [FieldOffset(6)]
        public ushort wVirtualKeyCode;
        [FieldOffset(8)]
        public ushort wVirtualScanCode;
        [FieldOffset(10)]
        public char UnicodeChar;
        [FieldOffset(12)]
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOW_BUFFER_SIZE_RECORD
    {
        public COORD dwSize;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT_RECORD
    {
        [FieldOffset(0)]
        public ushort EventType;

        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;

        [FieldOffset(4)]
        public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
    }
}
