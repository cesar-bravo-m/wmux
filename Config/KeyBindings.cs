namespace Wmux.Config;

/// <summary>
/// Defines the key bindings for wmux. Matches tmux defaults.
/// </summary>
public class KeyBindings
{
    public ConsoleModifiers PrefixModifier { get; set; } = ConsoleModifiers.Control;
    public ConsoleKey PrefixKey { get; set; } = ConsoleKey.A;

    // After prefix:
    public char SplitHorizontal { get; set; } = 's';
    public char SplitVertical { get; set; } = '|';
    public char NewWindow { get; set; } = 'c';
    public char NextWindow { get; set; } = 'n';
    public char PrevWindow { get; set; } = 'p';
    public char Detach { get; set; } = 'd';
    public char KillPane { get; set; } = 'x';
    public char CommandMode { get; set; } = ':';
    public char CopyMode { get; set; } = '[';
    public char RenameWindow { get; set; } = ',';
    public char KillWindow { get; set; } = '&';
    public char NextPane { get; set; } = 'o';
    public char CycleLayout { get; set; } = ' ';

    public bool IsPrefixKey(ConsoleKeyInfo key)
    {
        return key.Key == PrefixKey && key.Modifiers == PrefixModifier;
    }
}
