# wmux

A [terminal multiplexer](https://en.wikipedia.org/wiki/Terminal_multiplexer) for Windows, inspired by [tmux](https://en.wikipedia.org/wiki/Tmux). It allows you to run multiple shell sessions in split panes within a single window, detach and reattach to sessions, and manage windows and layouts -- all through a GUI built on [Windows Forms](https://en.wikipedia.org/wiki/Windows_Forms).

Built with C# on [.NET 10](https://dotnet.microsoft.com/), using zero external dependencies.

## Architecture

wmux follows a **client-server model** identical in spirit to tmux. A background server process owns all session state. Thin GUI clients connect to it, receive rendered screen snapshots, and forward user input.

```
  TerminalWindow (WinForms GUI)
        |
        v
  InputHandler ──prefix key?──> Command dispatch (split, new-window, etc.)
        |                              |
        | (raw keystroke)              v
        v                       Session / Window / Pane (state mutation)
  WmuxGuiClient                        |
        |                              v
   NamedPipe IPC  <────────>  WmuxServer (broadcast loop ~60fps)
        |                              |
        v                              v
  ScreenSnapshot ──render──>   ConPtyProcess (child shell)
  applied to grid                      |
        |                              v
        v                       VtParser + ScreenBuffer
  GDI+ paint to window         (virtual terminal state)
```

Three operational modes are supported:

| Mode | Description |
|------|-------------|
| **Embedded** | Server and client run in the same process. A single `wmux` invocation starts both. The server shuts down automatically when the last client disconnects. |
| **Standalone server** | `wmux start-server` launches a headless server. Clients connect later with `wmux attach`. |
| **Thin client** | `wmux new-session` or `wmux attach` connects a GUI window to an already-running server over [named pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes). |

## Core Concepts

### ConPTY (Windows Pseudo Console)

Each pane spawns a child shell (PowerShell 7 by default) inside a [Windows Pseudo Console](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) (ConPTY). ConPTY is the Windows equivalent of a Unix [PTY](https://en.wikipedia.org/wiki/Pseudoterminal) -- it creates a bidirectional channel between wmux and the child process where the child believes it is running in a real console.

The implementation uses [P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke) to call the Win32 functions `CreatePseudoConsole`, `ResizePseudoConsole`, and `ClosePseudoConsole` from `kernel32.dll`.

### VT Sequence Parsing

Child processes emit [ANSI escape sequences](https://en.wikipedia.org/wiki/ANSI_escape_code) (VT100/xterm) to control cursor position, colors, and screen clearing. The `VtParser` is a [finite-state machine](https://en.wikipedia.org/wiki/Finite-state_machine) with six states:

| State | Handles |
|-------|---------|
| `Ground` | Printable characters and C0 control codes (`\n`, `\r`, `\t`, `\b`, `\a`, `\x1b`) |
| `Escape` | The byte immediately after ESC (0x1B), dispatching to CSI, OSC, DCS, or direct commands |
| `Csi` / `CsiParam` | [CSI (Control Sequence Introducer)](https://en.wikipedia.org/wiki/ANSI_escape_code#CSI_sequences) sequences like cursor movement, erasing, scrolling, and [SGR](https://en.wikipedia.org/wiki/ANSI_escape_code#SGR_(Select_Graphic_Rendition)_parameters) color codes |
| `OscString` | [OSC (Operating System Command)](https://en.wikipedia.org/wiki/ANSI_escape_code#OSC_(Operating_System_Command)_sequences) sequences, used for setting window titles |
| `DcsString` | [DCS (Device Control String)](https://en.wikipedia.org/wiki/ANSI_escape_code#DCS_(Device_Control_String)_sequences) sequences (consumed and discarded) |

Color support includes 16-color, [256-color](https://en.wikipedia.org/wiki/ANSI_escape_code#8-bit), and [24-bit true color](https://en.wikipedia.org/wiki/ANSI_escape_code#24-bit) (mapped to the nearest `ConsoleColor`).

### Screen Buffer

Each pane maintains a virtual screen buffer -- a 2D grid of characters with per-cell foreground color, background color, and bold flag. The buffer implements:

- Cursor positioning and visibility (including saved/restored cursor state via `ESC 7` / `ESC 8`)
- [Scroll regions](https://vt100.net/docs/vt100-ug/chapter3.html#S3.5) (DECSTBM)
- Line insertion/deletion, character insertion/deletion
- Alternate screen buffer (used by programs like `vim` and `less`)
- Tab stops at every 8 columns

### Pane Layout Engine

Panes within a window are arranged using a [binary tree](https://en.wikipedia.org/wiki/Binary_tree). Each split (horizontal or vertical) creates two child nodes. The layout engine supports:

- Horizontal and vertical splits with 1-character borders
- Sibling promotion on pane removal (the remaining sibling inherits the parent's space)
- Automatic recalculation on window resize
- Five layout presets matching tmux:

| Layout | Description |
|--------|-------------|
| `even-horizontal` | Panes side by side, equal widths |
| `even-vertical` | Panes stacked, equal heights |
| `main-horizontal` | Large pane on top, others split below |
| `main-vertical` | Large pane on left, others stacked right |
| `tiled` | Grid arrangement approaching square proportions |

### IPC Protocol

Communication between server and client uses [named pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes) at `\\.\pipe\wmux-server`. Messages are [JSON](https://en.wikipedia.org/wiki/JSON)-serialized and framed with a 4-byte [length prefix](https://en.wikipedia.org/wiki/Type%E2%80%93length%E2%80%93value):

```
[4 bytes: message length (little-endian int32)] [N bytes: UTF-8 JSON payload]
```

Polymorphic deserialization uses .NET's `[JsonDerivedType]` attributes with a `$type` discriminator field. The 14 message types are:

| Message | Direction | Purpose |
|---------|-----------|---------|
| `AttachMessage` | Client -> Server | Attach to existing session |
| `DetachMessage` | Client -> Server | Detach from session |
| `NewSessionMessage` | Client -> Server | Create a new session with given dimensions |
| `ResizeMessage` | Client -> Server | Terminal window resized |
| `InputMessage` | Client -> Server | Raw keystroke data (VT sequences) |
| `CommandMessage` | Client -> Server | `:` command-mode input |
| `KillServerMessage` | Client -> Server | Shut down the server |
| `SessionInfoMessage` | Client -> Server | Request session list |
| `ScreenSnapshotMessage` | Server -> Client | Full rendered screen grid (chars + colors + cursor) |
| `CommandResultMessage` | Server -> Client | Output from a command |
| `SessionListMessage` | Server -> Client | List of active sessions |
| `SessionClosedMessage` | Server -> Client | Session was destroyed |
| `ErrorMessage` | Server -> Client | Error description |
| `OutputMessage` | Server -> Client | Raw pane output data |

The `ScreenSnapshotMessage` carries the fully composed screen as a flat character string (row-major) plus two `byte[]` arrays for foreground and background `ConsoleColor` indices, along with cursor position.

### Server Broadcast Loop

The server runs a broadcast loop at approximately 60 frames per second. On each tick it checks if any session is dirty (has received new output), renders the session state into a `ScreenSnapshotMessage`, and sends it to all attached clients for that session. A [lock file](https://en.wikipedia.org/wiki/Lock_(computer_science)#File_locking) (`wmux-server.lock` in `%TEMP%`) is used for server discovery -- clients check for this file to determine if a server is already running.

### GUI Rendering

The terminal window is a Windows Forms `Form` subclass that renders characters using [GDI+](https://learn.microsoft.com/en-us/windows/win32/gdiplus/-gdiplus-gdi-start). Rendering is cell-based: background colors are drawn as filled rectangles, then text is drawn on top using `DrawString` with a monospace font. Font selection cascades through Cascadia Mono, Consolas, Courier New, and Lucida Console.

Keyboard input is captured by overriding [WndProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-messages-and-message-queues) and intercepting `WM_KEYDOWN`, `WM_CHAR`, and `WM_SYSKEYDOWN` messages directly, bypassing the standard WinForms keyboard event system for full control over modifier keys and special key combinations.

The cursor blinks on a 530ms timer and repaints are coalesced -- multiple output events trigger only a single `Invalidate()` call.

## Data Model

```
Session
  ├── Name, Id, CreatedAt
  └── Windows[]
        ├── Name, Index
        ├── PaneLayout (binary tree)
        └── Panes[]
              ├── ConPtyProcess (child shell)
              ├── ScreenBuffer (virtual screen)
              ├── VtParser (escape sequence state machine)
              └── ScrollbackBuffer (ring buffer, 10,000 lines)
```

A **session** contains one or more **windows**. Each window contains one or more **panes** arranged in a binary split tree. Each pane owns a ConPTY child process, a screen buffer, and a VT parser.

## Key Bindings

wmux uses tmux-compatible key bindings with `Ctrl+B` as the [prefix key](https://en.wikipedia.org/wiki/Tmux#Prefix_key):

| Sequence | Action |
|----------|--------|
| `Ctrl+B` then `"` | Split pane horizontally |
| `Ctrl+B` then `%` | Split pane vertically |
| `Ctrl+B` then arrow key | Navigate to adjacent pane (by [Manhattan distance](https://en.wikipedia.org/wiki/Taxicab_geometry) from pane center) |
| `Ctrl+B` then `c` | Create new window |
| `Ctrl+B` then `n` / `p` | Next / previous window |
| `Ctrl+B` then `0`-`9` | Select window by index |
| `Ctrl+B` then `d` | Detach from session |
| `Ctrl+B` then `x` | Kill current pane |
| `Ctrl+B` then `z` | Toggle pane zoom (fullscreen) |
| `Ctrl+B` then `:` | Enter command mode |
| `Ctrl+B` then `,` | Rename current window |
| `Ctrl+B` then `&` | Kill current window |
| `Ctrl+B` then `o` | Cycle to next pane |
| `Ctrl+B` then `Space` | Cycle through layout presets |

## Command Mode

Pressing `Ctrl+B` then `:` opens a command line at the bottom of the screen. Available commands:

| Command | Description |
|---------|-------------|
| `new-window` | Create a new window |
| `kill-window` | Close the current window |
| `select-window -t N` | Switch to window N |
| `next-window` / `prev-window` | Navigate windows |
| `rename-window NAME` | Rename current window |
| `list-windows` | List all windows |
| `split-window [-h\|-v]` | Split pane horizontally or vertically |
| `kill-pane` | Close current pane |
| `select-pane -t N` | Switch to pane N |
| `next-pane` | Cycle to next pane |
| `zoom-pane` | Toggle zoom |
| `list-panes` | List all panes |
| `select-layout NAME` | Apply a layout preset |

## CLI Usage

```
wmux                      # Start embedded server + client (default)
wmux new-session [-s NAME]  # Create a named session on the running server
wmux attach [-t NAME]     # Attach to an existing session
wmux start-server         # Start standalone background server
wmux list-sessions        # List all active sessions
wmux kill-server          # Shut down the server
wmux help                 # Show usage information
wmux version              # Print version
```

## Configuration

wmux reads `~/.wmux.conf` on startup. Supported directives:

```
set-option default-shell /path/to/shell
set-option history-limit 5000
bind-key <key> <command>
```

## UI Components

**Status bar**: A green bar at the bottom of the screen showing `[session-name]` on the left, a list of windows (with `*` marking the active one), and `[pane-index/total] HH:mm` on the right -- matching the tmux default appearance.

**Pane borders**: Drawn with [Unicode box-drawing characters](https://en.wikipedia.org/wiki/Box-drawing_characters) (`│ ─ ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ┼`). The active pane's border is green; inactive borders are dark gray.

## Project Structure

```
wmux/
├── Program.cs                       Entry point, CLI command dispatch
├── Server/
│   ├── WmuxServer.cs                Named pipe server, session lifecycle, broadcast loop
│   ├── ServerIpc.cs                 IPC message types and length-prefixed JSON protocol
│   └── ServerRenderer.cs           Renders session state into ScreenSnapshotMessage
├── Client/
│   ├── WmuxGuiClient.cs            GUI client (standalone and thin-client modes)
│   ├── InputHandler.cs             Prefix key detection, keybinding dispatch, VT encoding
│   └── GuiRenderer.cs              Renders session state to TerminalWindow grid
├── Core/
│   ├── Session.cs                   Session model (contains windows)
│   ├── Window.cs                    Window model (contains panes and layout)
│   ├── Pane.cs                      Pane model (ConPTY + ScreenBuffer + VtParser)
│   ├── PaneLayout.cs                Binary tree layout engine
│   └── ScrollbackBuffer.cs         Ring buffer for scrollback history
├── Terminal/
│   ├── ConPtyProcess.cs             ConPTY wrapper (P/Invoke to kernel32.dll)
│   ├── ConPtyNative.cs              Win32 P/Invoke declarations and constants
│   ├── VtParser.cs                  VT100/xterm escape sequence state machine
│   ├── ScreenBuffer.cs             Virtual 2D character grid with colors
│   └── TerminalWindow.cs           WinForms window, GDI+ rendering, WndProc input
├── Config/
│   ├── WmuxConfig.cs                Configuration file parser (~/.wmux.conf)
│   └── KeyBindings.cs               Keybinding definitions
├── Commands/
│   ├── CommandParser.cs             Tokenizer for : command input
│   └── CommandRegistry.cs          Command implementations (16 commands)
├── UI/
│   ├── StatusBar.cs                 tmux-style status bar renderer
│   ├── PaneBorder.cs                Box-drawing border renderer
│   └── CommandLine.cs               : command input widget
└── wmux.Tests/                      xUnit test suite
```

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```
dotnet build
dotnet run
dotnet test wmux.Tests/
```

## Technical Requirements

- Windows 10 version 1809+ (for ConPTY support)
- .NET 10 runtime
- PowerShell 7 (`pwsh.exe`) recommended; falls back to `powershell.exe`
