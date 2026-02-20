namespace Wmux.Commands;

public record ParsedCommand(string Name, string[] Args);

public static class CommandParser
{
    public static ParsedCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        return new ParsedCommand(parts[0].ToLowerInvariant(), parts[1..]);
    }
}
