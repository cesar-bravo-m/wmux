namespace Wmux.Config;

/// <summary>
/// Configuration model loaded from ~/.wmux.conf
/// </summary>
public class WmuxConfig
{
    public KeyBindings Keys { get; set; } = new();
    public int ScrollbackLimit { get; set; } = 10000;
    public string DefaultShell { get; set; } = "";

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wmux.conf");

    public static WmuxConfig Load()
    {
        var config = new WmuxConfig();
        if (!File.Exists(ConfigPath)) return config;

        foreach (var rawLine in File.ReadLines(ConfigPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var cmd = parts[0].ToLowerInvariant();
            var arg = parts[1].Trim();

            switch (cmd)
            {
                case "set-option" or "set":
                    ParseSetOption(config, arg);
                    break;
                case "bind-key" or "bind":
                    ParseBindKey(config, arg);
                    break;
            }
        }

        return config;
    }

    private static void ParseSetOption(WmuxConfig config, string arg)
    {
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        switch (parts[0])
        {
            case "default-shell":
                config.DefaultShell = parts[1];
                break;
            case "history-limit":
                if (int.TryParse(parts[1], out int limit))
                    config.ScrollbackLimit = limit;
                break;
        }
    }

    private static void ParseBindKey(WmuxConfig config, string arg)
    {
        // Simplified bind-key parsing
        // Format: bind-key <key> <action>
        // For now, we support the defaults and allow overrides
    }
}
