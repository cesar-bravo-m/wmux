using System.Runtime.InteropServices;

namespace Wmux.Terminal;

/// <summary>
/// Win32 clipboard access via P/Invoke. Works from any thread.
/// </summary>
internal static class ClipboardHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// Read Unicode text from the Windows clipboard.
    /// Returns null if the clipboard is empty or doesn't contain text.
    /// </summary>
    public static string? GetText()
    {
        if (!OpenClipboard(IntPtr.Zero))
            return null;
        try
        {
            var hData = GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero) return null;

            var ptr = GlobalLock(hData);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(hData);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Copy the given text to the Windows clipboard.
    /// Returns true on success.
    /// </summary>
    public static bool SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;
        try
        {
            EmptyClipboard();
            int byteCount = (text.Length + 1) * 2; // UTF-16 + null terminator
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero) return false;

            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                // Write null terminator
                Marshal.WriteInt16(ptr + text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                return false;

            // After successful SetClipboardData, the system owns hGlobal.
            // Do NOT call GlobalFree.
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }
}
