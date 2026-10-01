using OneRGB.Application.Devices.Input;
namespace OneRGB.Application.Devices.Mouse;
public static class HidKeys
{
    private static readonly Dictionary<string, int> Table = Build();
    private static readonly Dictionary<int, string> Reverse = Table.ToDictionary(p => p.Value, p => p.Key);

    public static int? Usage(string code) => Table.TryGetValue(code, out var usage) ? usage : null;

    public static string? Code(int usage) => Reverse.GetValueOrDefault(usage);

    private static Dictionary<string, int> Build()
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Digit0"] = 0x27, ["Enter"] = 0x28, ["Escape"] = 0x29, ["Backspace"] = 0x2A, ["Tab"] = 0x2B, ["Space"] = 0x2C,
            ["Minus"] = 0x2D, ["Equal"] = 0x2E, ["BracketLeft"] = 0x2F, ["BracketRight"] = 0x30, ["Backslash"] = 0x31,
            ["Semicolon"] = 0x33, ["Quote"] = 0x34, ["Backquote"] = 0x35, ["Comma"] = 0x36, ["Period"] = 0x37, ["Slash"] = 0x38,
            ["CapsLock"] = 0x39, ["PrintScreen"] = 0x46, ["ScrollLock"] = 0x47, ["Pause"] = 0x48, ["Insert"] = 0x49, ["Home"] = 0x4A,
            ["PageUp"] = 0x4B, ["Delete"] = 0x4C, ["End"] = 0x4D, ["PageDown"] = 0x4E, ["ArrowRight"] = 0x4F, ["ArrowLeft"] = 0x50,
            ["ArrowDown"] = 0x51, ["ArrowUp"] = 0x52, ["NumLock"] = 0x53, ["NumpadDivide"] = 0x54, ["NumpadMultiply"] = 0x55,
            ["NumpadSubtract"] = 0x56, ["NumpadAdd"] = 0x57, ["NumpadEnter"] = 0x58, ["Numpad0"] = 0x62, ["NumpadDecimal"] = 0x63,
            ["IntlBackslash"] = 0x64, ["ContextMenu"] = 0x65,
        };
        for (var i = 0; i < 26; i++)
        {
            table[$"Key{(char)('A' + i)}"] = 0x04 + i;
        }

        for (var i = 1; i <= 9; i++)
        {
            table[$"Digit{i}"] = 0x1E + i - 1;
            table[$"Numpad{i}"] = 0x59 + i - 1;
        }

        for (var i = 1; i <= 12; i++)
        {
            table[$"F{i}"] = 0x3A + i - 1;
            table[$"F{i + 12}"] = 0x68 + i - 1;
        }

        return table;
    }
}
