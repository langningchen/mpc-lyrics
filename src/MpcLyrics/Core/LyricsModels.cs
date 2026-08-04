namespace MpcLyrics.Core;

public sealed record TimedToken(long StartMs, long EndMs, string Text);

public sealed record LyricLine(
    long TimeMs,
    long EndMs,
    string Original,
    string? Translation,
    IReadOnlyList<TimedToken> OriginalTokens,
    IReadOnlyList<TimedToken> TranslationTokens);

public sealed class LyricsDocument
{
    public required IReadOnlyList<LyricLine> Lines { get; init; }
    public required string SourcePath { get; init; }
    public string? TranslationPath { get; init; }
    public string? TranslationError { get; init; }

    public int? FindLineIndex(long positionMs)
    {
        var low = 0;
        var high = Lines.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].TimeMs <= positionMs) low = middle + 1;
            else high = middle;
        }

        var index = low - 1;
        if (index < 0 || index >= Lines.Count) return null;
        var line = Lines[index];
        var displayEndMs = index + 1 < Lines.Count
            ? Math.Max(line.EndMs, Lines[index + 1].TimeMs)
            : line.EndMs;
        return positionMs < displayEndMs ? index : null;
    }
}
