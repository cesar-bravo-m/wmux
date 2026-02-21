# wmux

A terminal multiplexer for Windows, heavily inspired by tmux. Built on top of the Windows ConPTY API and .NET 10.

## Quickstart

Download the latest executable from the [releases page]() and add it to your PATH. Then run `wmux` in your terminal to start a new session.

## Screenshots

<img width="1635" height="976" alt="image" src="https://github.com/user-attachments/assets/7680baa2-1b90-4ca5-afa6-e8429ae11704" />

<img width="2628" height="1245" alt="image" src="https://github.com/user-attachments/assets/fae124b2-314a-4d17-8fe6-c3ef9690f36b" />

## Features

- Horizontal & vertical pane splits
- Multiple windows and sessions

## Key Bindings

| Sequence | Action |
|----------|--------|
| `za s` | Split horizontally |
| `za v` | Split vertically |
| `za [hjkl]` | Navigate panes |
| `za c` | New window |
| `za n/p` | Next/previous window |
| `za d` | Detach terminal from wmux session |
| `za x` | Kill current pane/window/session |
| `za :` | Command mode |

## Todo

- Implement a command to detach a pane into a new window
- Interactive session selector
- Implement a command to freeze terminal output
- Read configuration from wmux.yml

## License

[ISC](LICENSE) Copyright (c) 2026, César Bravo Molina
