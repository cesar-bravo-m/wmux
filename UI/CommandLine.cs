using Wmux.Commands;
using Wmux.Core;

namespace Wmux.UI;

/// <summary>
/// Handles the : command input line at the bottom of the screen.
/// </summary>
public class CommandLine
{
    public bool IsActive { get; private set; }
    public string Input { get; private set; } = "";

    public void Activate()
    {
        IsActive = true;
        Input = "";
    }

    public void Deactivate()
    {
        IsActive = false;
        Input = "";
    }

    public string? HandleKey(ConsoleKeyInfo key)
    {
        if (!IsActive) return null;

        if (key.Key == ConsoleKey.Escape)
        {
            Deactivate();
            return null;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            var result = Input;
            Deactivate();
            return result;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (Input.Length > 0)
                Input = Input[..^1];
            return null;
        }

        if (key.KeyChar >= ' ')
        {
            Input += key.KeyChar;
        }

        return null;
    }
}
