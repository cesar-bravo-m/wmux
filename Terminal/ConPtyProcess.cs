using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Wmux.Terminal.ConPtyNative;

namespace Wmux.Terminal;

/// <summary>
/// Wraps a Windows ConPTY pseudo-console and spawns a child process (pwsh.exe).
/// Provides streams for reading output and writing input.
/// </summary>
public class ConPtyProcess : IDisposable
{
    private IntPtr _hPC;
    private PROCESS_INFORMATION _pi;
    private SafeFileHandle? _pipeFromPtyRead;
    private SafeFileHandle? _pipeToPtyWrite;
    private IntPtr _attributeList;
    private FileStream? _readStream;
    private FileStream? _writeStream;
    private bool _disposed;

    public Stream OutputStream => _readStream ?? throw new ObjectDisposedException(nameof(ConPtyProcess));
    public Stream InputStream => _writeStream ?? throw new ObjectDisposedException(nameof(ConPtyProcess));
    public int ProcessId => _pi.dwProcessId;
    public IntPtr ProcessHandle => _pi.hProcess;

    public bool HasExited
    {
        get
        {
            if (_pi.hProcess == IntPtr.Zero) return true;
            return WaitForSingleObject(_pi.hProcess, 0) == 0;
        }
    }

    public ConPtyProcess(short cols, short rows, string? command = null)
    {
        command ??= FindShell();

        // Create pipes for PTY input/output
        CreatePipe(out var pipeFromPtyRead, out var pipeFromPtyWrite, IntPtr.Zero, 0);
        CreatePipe(out var pipeToPtyRead, out var pipeToPtyWrite, IntPtr.Zero, 0);

        _pipeFromPtyRead = pipeFromPtyRead;
        _pipeToPtyWrite = pipeToPtyWrite;

        // Create the pseudo console
        var size = new COORD(cols, rows);
        int hr = CreatePseudoConsole(size, pipeToPtyRead, pipeFromPtyWrite, 0, out _hPC);
        if (hr != 0)
            throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");

        // Close the handles we don't need (the PTY owns copies now)
        pipeToPtyRead.Dispose();
        pipeFromPtyWrite.Dispose();

        // Initialize the startup info with the pseudo console
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Get attribute list size
        IntPtr attrListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);

        _attributeList = Marshal.AllocHGlobal(attrListSize);
        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref attrListSize))
            throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");

        // Set the pseudo console attribute
        if (!UpdateProcThreadAttribute(
            _attributeList, 0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _hPC,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");

        startupInfo.lpAttributeList = _attributeList;

        // Create the child process
        if (!CreateProcess(
            null, command,
            IntPtr.Zero, IntPtr.Zero,
            false,
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            null,
            ref startupInfo,
            out _pi))
            throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastWin32Error()}");

        _readStream = new FileStream(_pipeFromPtyRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
        _writeStream = new FileStream(_pipeToPtyWrite, FileAccess.Write, bufferSize: 4096, isAsync: false);
    }

    public void Resize(short cols, short rows)
    {
        if (_hPC != IntPtr.Zero)
            ResizePseudoConsole(_hPC, new COORD(cols, rows));
    }

    public void WriteInput(string text)
    {
        if (_writeStream == null) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        _writeStream.Write(bytes, 0, bytes.Length);
        _writeStream.Flush();
    }

    public void WriteInput(byte[] data)
    {
        if (_writeStream == null) return;
        _writeStream.Write(data, 0, data.Length);
        _writeStream.Flush();
    }

    private static string FindShell()
    {
        // Prefer pwsh (PowerShell 7+), fall back to powershell.exe, then cmd.exe
        var pwsh = Environment.GetEnvironmentVariable("ProgramFiles") + @"\PowerShell\7\pwsh.exe";
        if (File.Exists(pwsh)) return pwsh;

        // Check PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return "powershell.exe";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _readStream?.Dispose();
        _writeStream?.Dispose();
        _readStream = null;
        _writeStream = null;

        if (_hPC != IntPtr.Zero)
        {
            ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        if (_attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }

        if (_pi.hProcess != IntPtr.Zero)
        {
            CloseHandle(_pi.hProcess);
            _pi.hProcess = IntPtr.Zero;
        }
        if (_pi.hThread != IntPtr.Zero)
        {
            CloseHandle(_pi.hThread);
            _pi.hThread = IntPtr.Zero;
        }

        _pipeFromPtyRead?.Dispose();
        _pipeToPtyWrite?.Dispose();
        _pipeFromPtyRead = null;
        _pipeToPtyWrite = null;
    }
}
