namespace MpcLyrics.Core;

public sealed class CommandLineOptions
{
    public string? PlayerPath { get; private set; }
    public bool ShowSettings { get; private set; }
    public List<string> MediaFiles { get; } = new();

    public static CommandLineOptions Parse(IEnumerable<string> arguments)
    {
        var result = new CommandLineOptions();
        var items = arguments.ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var argument = items[index];
            if (argument.Equals("--player", StringComparison.OrdinalIgnoreCase) && index + 1 < items.Length)
            {
                result.PlayerPath = items[++index];
            }
            else if (argument.Equals("--settings", StringComparison.OrdinalIgnoreCase)
                     || argument.Equals("/settings", StringComparison.OrdinalIgnoreCase))
            {
                result.ShowSettings = true;
            }
            else if (!argument.StartsWith('-') && !argument.StartsWith('/'))
            {
                result.MediaFiles.Add(argument);
            }
            else if (Path.IsPathFullyQualified(argument) || File.Exists(argument))
            {
                result.MediaFiles.Add(argument);
            }
        }
        return result;
    }
}
