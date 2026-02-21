# wmux

A terminal multiplexer for Windows, heavily inspired by tmux. Built on top of the Windows ConPTY API and .NET 10.

## Quickstart

Download the latest executable from the [releases page]() and add it to your PATH. Then run `wmux` in your terminal to start a new session.

## Screenshots

## Features

- Horizontal & vertical pane splits with binary tree layout engine
- Multiple windows with tmux-style navigation
- Client-server architecture over TCP sockets (attach/detach)

## Key Bindings

| Sequence | Action |
|----------|--------|
| `za s` | Split horizontally |
| `za v` | Split vertically |
| `za [hjkl]` | Navigate panes |
| `za c` | New window |
| `za n/p` | Next/previous window |
| `za d` | Detach from session |
| `za x` | Kill current pane/window/session |
| `za :` | Command mode |

## License

[ISC](LICENSE) Copyright (c) 2026, César Bravo Molina
