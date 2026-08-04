using System.Collections.Generic;
using System.Text;
using MpcLyrics.Core;

namespace MpcLyrics.Services;

public static class LrcParser
{
    private const long TranslationToleranceMs = 750;
    private const long LastLineFallbackMs = 6_000;
    private const long MinTokenDurationMs = 80;

    static LrcParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    internal static void ExerciseForSmokeTest()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "MpcLyricsLrcSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var mediaPath = Path.Combine(testRoot, "blank-markers.flac");
            File.WriteAllBytes(mediaPath, Array.Empty<byte>());
            File.WriteAllText(
                Path.ChangeExtension(mediaPath, ".lrc"),
                "[00:01.00]First line\n" +
                "[00:02.00]\n" +
                "[00:03.00]Second line\n" +
                "[00:04.00]   \n" +
                "[00:05.00]Third line\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(testRoot, "blank-markers.translated.lrc"),
                "[00:01.00]第一句\n" +
                "[00:03.00]第二句\n" +
                "[00:05.00]第三句\n",
                new UTF8Encoding(false));

            var document = LoadForAudio(mediaPath);
            var expectedOriginal = new[] { "First line", "Second line", "Third line" };
            var expectedTranslation = new[] { "第一句", "第二句", "第三句" };
            if (document?.Lines.Count != expectedOriginal.Length)
                throw new InvalidOperationException("Empty LRC markers became display lines.");
            for (var index = 0; index < expectedOriginal.Length; index++)
            {
                if (document.Lines[index].Original != expectedOriginal[index]
                    || document.Lines[index].Translation != expectedTranslation[index])
                {
                    throw new InvalidOperationException(
                        $"LRC line {index} did not retain its matching translation.");
                }
            }
            if (document.FindLineIndex(2_500) != 0)
                throw new InvalidOperationException("An LRC blank marker interrupted the current line.");

            AppLogger.Startup("SMOKE_TEST: LRC blank-marker filtering passed");
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    public static LyricsDocument? LoadForAudio(string audioPath)
    {
        var sourcePath = FindOriginalLrc(audioPath);
        if (sourcePath is null) return null;

        var sourceText = DecodeFile(sourcePath);
        var source = Parse(sourceText);
        if (source.Count == 0) return null;

        var translationPath = FindTranslationLrc(audioPath, sourcePath);
        List<ParsedLine>? translation = null;
        string? translationError = null;
        if (translationPath is not null)
        {
            try
            {
                translation = Parse(DecodeFile(translationPath));
            }
            catch (Exception error)
            {
                translationError = $"Unable to read {translationPath}: {error.Message}";
            }
        }

        var lines = new List<LyricLine>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var sourceLine = source[index];
            var naturalEnd = index + 1 < source.Count
                ? source[index + 1].TimeMs
                : sourceLine.Primary.Segments.Count > 0
                    ? sourceLine.Primary.Segments[^1].StartMs + 1_500
                    : sourceLine.TimeMs + LastLineFallbackMs;
            var endMs = Math.Max(naturalEnd, sourceLine.TimeMs + MinTokenDurationMs);

            ParsedText? translationText = null;
            if (translation is { Count: > 0 })
            {
                translationText = FindNearestLine(translation, sourceLine.TimeMs)?.Primary;
            }

            translationText ??= sourceLine.Extras.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Text)
                && !string.Equals(item.Text.Trim(), sourceLine.Primary.Text.Trim(), StringComparison.Ordinal));

            if (translationText is not null
                && (string.IsNullOrWhiteSpace(translationText.Text)
                    || string.Equals(translationText.Text.Trim(), sourceLine.Primary.Text.Trim(), StringComparison.Ordinal)))
            {
                translationText = null;
            }

            lines.Add(new LyricLine(
                sourceLine.TimeMs,
                endMs,
                sourceLine.Primary.Text,
                translationText?.Text,
                BuildTimedTokens(sourceLine.Primary, sourceLine.TimeMs, endMs),
                translationText is null
                    ? Array.Empty<TimedToken>()
                    : BuildTimedTokens(translationText, sourceLine.TimeMs, endMs)));
        }

        return new LyricsDocument
        {
            Lines = lines,
            SourcePath = sourcePath,
            TranslationPath = translationPath,
            TranslationError = translationError,
        };
    }

    private static string? FindOriginalLrc(string audioPath)
    {
        var directory = Path.GetDirectoryName(audioPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        var candidates = new[]
        {
            Path.Combine(directory, stem + ".lrc"),
            audioPath + ".lrc",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindTranslationLrc(string audioPath, string sourcePath)
    {
        var directory = Path.GetDirectoryName(audioPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        var fileName = Path.GetFileName(audioPath);
        var suffixes = new[]
        {
            "trans", "translation", "translated", "zh", "zh-CN", "zh_CN",
            "cn", "chs", "中文", "翻译",
        };

        var candidates = new List<string> { Path.Combine(directory, stem + ".tlrc") };
        foreach (var suffix in suffixes)
        {
            candidates.Add(Path.Combine(directory, $"{stem}.{suffix}.lrc"));
            candidates.Add(Path.Combine(directory, $"{stem} ({suffix}).lrc"));
            candidates.Add(Path.Combine(directory, $"{stem} [{suffix}].lrc"));
        }
        candidates.Add(Path.Combine(directory, fileName + ".trans.lrc"));
        candidates.Add(Path.Combine(directory, fileName + ".translation.lrc"));

        return candidates.FirstOrDefault(path =>
            !string.Equals(Path.GetFullPath(path), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)
            && File.Exists(path));
    }

    private static string DecodeFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (LooksLikeUtf16(bytes, littleEndian: true))
            return Encoding.Unicode.GetString(bytes);
        if (LooksLikeUtf16(bytes, littleEndian: false))
            return Encoding.BigEndianUnicode.GetString(bytes);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static bool LooksLikeUtf16(byte[] bytes, bool littleEndian)
    {
        if (bytes.Length < 8) return false;
        var sampleLength = Math.Min(bytes.Length, 128) & ~1;
        var zeroes = 0;
        var checkedCount = 0;
        for (var index = 0; index < sampleLength; index += 2)
        {
            var candidate = bytes[index + (littleEndian ? 1 : 0)];
            checkedCount++;
            if (candidate == 0) zeroes++;
        }
        return zeroes * 3 >= checkedCount * 2;
    }

    private static List<ParsedLine> Parse(string text)
    {
        long offsetMs = 0;
        var grouped = new SortedDictionary<long, List<ParsedText>>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart('\uFEFF');
            if (TryParseOffset(line, out var offset))
            {
                offsetMs = offset;
                continue;
            }

            var (timestamps, lyric) = ParseLeadingTimestamps(line);
            if (timestamps.Count == 0) continue;
            var parsedText = ParseEnhancedText(lyric.Trim());
            foreach (var timestamp in timestamps)
            {
                if (!grouped.TryGetValue(timestamp, out var list))
                {
                    list = new List<ParsedText>();
                    grouped.Add(timestamp, list);
                }
                list.Add(parsedText.Clone());
            }
        }

        var result = new List<ParsedLine>(grouped.Count);
        foreach (var (rawTime, texts) in grouped)
        {
            // Empty timestamped LRC rows are clear markers, not lyric lines. If
            // they enter the display list they consume the preview slot, skew
            // odd/even placement, and can incorrectly match a nearby translation.
            var displayTexts = texts
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .ToList();
            if (displayTexts.Count == 0) continue;

            var adjustedTime = Math.Max(0, rawTime + offsetMs);
            var primary = displayTexts[0];
            AdjustSegments(primary, offsetMs, adjustedTime);
            var extras = displayTexts.Skip(1).ToList();
            foreach (var extra in extras) AdjustSegments(extra, offsetMs, adjustedTime);
            result.Add(new ParsedLine(adjustedTime, primary, extras));
        }
        return result;
    }

    private static void AdjustSegments(ParsedText text, long offsetMs, long lineTime)
    {
        foreach (var segment in text.Segments)
            segment.StartMs = Math.Max(lineTime, segment.StartMs + offsetMs);
    }

    private static bool TryParseOffset(string line, out long offset)
    {
        offset = 0;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']')) return false;
        var inside = trimmed[1..^1];
        var separator = inside.IndexOf(':');
        if (separator <= 0) return false;
        if (!inside[..separator].Trim().Equals("offset", StringComparison.OrdinalIgnoreCase)) return false;
        return long.TryParse(inside[(separator + 1)..].Trim(), out offset);
    }

    private static (List<long> Timestamps, string Remaining) ParseLeadingTimestamps(string line)
    {
        var timestamps = new List<long>();
        var cursor = 0;
        while (cursor < line.Length)
        {
            while (cursor < line.Length && char.IsWhiteSpace(line[cursor])) cursor++;
            if (cursor >= line.Length || line[cursor] != '[') break;
            var close = line.IndexOf(']', cursor + 1);
            if (close < 0) break;
            if (!TryParseTimestamp(line.AsSpan(cursor + 1, close - cursor - 1), out var timestamp)) break;
            timestamps.Add(timestamp);
            cursor = close + 1;
        }
        return (timestamps, line[cursor..]);
    }

    private static bool TryParseTimestamp(ReadOnlySpan<char> token, out long milliseconds)
    {
        milliseconds = 0;
        var text = token.Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0) return false;
        if (!long.TryParse(text[..colon], out var minutes) || minutes < 0) return false;

        var rest = text[(colon + 1)..];
        var fractionSeparator = rest.IndexOfAny('.', ':');
        ReadOnlySpan<char> secondsText;
        ReadOnlySpan<char> fractionText;
        if (fractionSeparator >= 0)
        {
            secondsText = rest[..fractionSeparator];
            fractionText = rest[(fractionSeparator + 1)..];
        }
        else
        {
            secondsText = rest;
            fractionText = ReadOnlySpan<char>.Empty;
        }

        if (!long.TryParse(secondsText.Trim(), out var seconds) || seconds is < 0 or >= 60) return false;
        var digits = new string(fractionText.ToString().TakeWhile(char.IsAsciiDigit).Take(3).ToArray());
        var fractionMs = digits.Length switch
        {
            0 => 0,
            1 => int.Parse(digits) * 100,
            2 => int.Parse(digits) * 10,
            _ => int.Parse(digits),
        };
        milliseconds = minutes * 60_000 + seconds * 1_000 + fractionMs;
        return true;
    }

    private static ParsedText ParseEnhancedText(string text)
    {
        var plain = new StringBuilder(text.Length);
        var active = new StringBuilder();
        var segments = new List<ParsedSegment>();
        long? activeStart = null;
        var cursor = 0;

        while (cursor < text.Length)
        {
            var open = text.IndexOf('<', cursor);
            if (open < 0)
            {
                var tail = text[cursor..];
                plain.Append(tail);
                active.Append(tail);
                break;
            }

            var prefix = text[cursor..open];
            plain.Append(prefix);
            active.Append(prefix);
            var close = text.IndexOf('>', open + 1);
            if (close < 0)
            {
                var tail = text[open..];
                plain.Append(tail);
                active.Append(tail);
                break;
            }

            if (TryParseTimestamp(text.AsSpan(open + 1, close - open - 1), out var timestamp))
            {
                if (active.Length > 0)
                {
                    segments.Add(new ParsedSegment(activeStart ?? timestamp, active.ToString()));
                    active.Clear();
                }
                activeStart = timestamp;
            }
            else
            {
                var literal = text[open..(close + 1)];
                plain.Append(literal);
                active.Append(literal);
            }
            cursor = close + 1;
        }

        if (active.Length > 0 && activeStart is long start)
            segments.Add(new ParsedSegment(start, active.ToString()));
        return new ParsedText(plain.ToString(), segments);
    }

    private static ParsedLine? FindNearestLine(IReadOnlyList<ParsedLine> lines, long timeMs)
    {
        if (lines.Count == 0) return null;
        var low = 0;
        var high = lines.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (lines[middle].TimeMs < timeMs) low = middle + 1;
            else high = middle;
        }

        ParsedLine? best = null;
        long bestDistance = long.MaxValue;
        foreach (var index in new[] { low - 1, low })
        {
            if (index < 0 || index >= lines.Count) continue;
            var distance = Math.Abs(lines[index].TimeMs - timeMs);
            if (distance < bestDistance)
            {
                best = lines[index];
                bestDistance = distance;
            }
        }
        return bestDistance <= TranslationToleranceMs ? best : null;
    }

    private static IReadOnlyList<TimedToken> BuildTimedTokens(ParsedText parsed, long lineStart, long lineEnd)
    {
        if (parsed.Segments.Count > 0)
        {
            var enhanced = new List<TimedToken>(parsed.Segments.Count);
            for (var index = 0; index < parsed.Segments.Count; index++)
            {
                var segment = parsed.Segments[index];
                if (segment.Text.Length == 0) continue;
                var start = Math.Clamp(segment.StartMs, lineStart, lineEnd);
                var next = index + 1 < parsed.Segments.Count
                    ? parsed.Segments[index + 1].StartMs
                    : lineEnd;
                var end = Math.Min(lineEnd, Math.Max(start + 1, next));
                if (end <= start)
                {
                    end = Math.Min(lineEnd, start + MinTokenDurationMs);
                }
                enhanced.Add(new TimedToken(start, Math.Max(start + 1, end), segment.Text));
            }
            if (enhanced.Count > 0) return enhanced;
        }

        var units = SplitDisplayUnits(parsed.Text);
        if (units.Count == 0) return Array.Empty<TimedToken>();
        var duration = Math.Max(lineEnd - lineStart, MinTokenDurationMs * units.Count);
        var weights = units.Select(TokenWeight).ToArray();
        var totalWeight = Math.Max(1L, weights.Sum(weight => (long)weight));
        long elapsedWeight = 0;
        var result = new List<TimedToken>(units.Count);
        for (var index = 0; index < units.Count; index++)
        {
            var start = lineStart + duration * elapsedWeight / totalWeight;
            elapsedWeight += weights[index];
            var end = index + 1 == units.Count
                ? lineEnd
                : lineStart + duration * elapsedWeight / totalWeight;
            result.Add(new TimedToken(start, Math.Max(start + 1, end), units[index]));
        }
        return result;
    }

    public static IReadOnlyList<string> SplitDisplayUnits(string text)
    {
        var units = new List<string>();
        var current = new StringBuilder();
        var currentIsLatin = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (current.Length == 0)
                {
                    if (units.Count > 0) units[^1] += character;
                    else current.Append(character);
                }
                else
                {
                    current.Append(character);
                    units.Add(current.ToString());
                    current.Clear();
                }
                currentIsLatin = false;
                continue;
            }

            var latin = char.IsAsciiLetterOrDigit(character) || character is '\'' or '’' or '-' or '_';
            if (latin)
            {
                if (current.Length > 0 && !currentIsLatin)
                {
                    units.Add(current.ToString());
                    current.Clear();
                }
                current.Append(character);
                currentIsLatin = true;
            }
            else if (IsCjk(character))
            {
                if (current.Length > 0)
                {
                    units.Add(current.ToString());
                    current.Clear();
                }
                units.Add(character.ToString());
                currentIsLatin = false;
            }
            else
            {
                if (current.Length == 0)
                {
                    if (units.Count > 0) units[^1] += character;
                    else current.Append(character);
                }
                else current.Append(character);
                currentIsLatin = false;
            }
        }

        if (current.Length > 0) units.Add(current.ToString());
        return units;
    }

    private static bool IsCjk(char character) => character is
        >= '\u3400' and <= '\u4DBF'
        or >= '\u4E00' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF'
        or >= '\u3040' and <= '\u30FF'
        or >= '\uAC00' and <= '\uD7AF';

    private static int TokenWeight(string text) =>
        Math.Clamp(text.Count(character => !char.IsWhiteSpace(character)), 1, 8);

    private sealed class ParsedLine(long timeMs, ParsedText primary, List<ParsedText> extras)
    {
        public long TimeMs { get; } = timeMs;
        public ParsedText Primary { get; } = primary;
        public List<ParsedText> Extras { get; } = extras;
    }

    private sealed class ParsedText(string text, List<ParsedSegment> segments)
    {
        public string Text { get; } = text;
        public List<ParsedSegment> Segments { get; } = segments;
        public ParsedText Clone() => new(Text, Segments.Select(item => new ParsedSegment(item.StartMs, item.Text)).ToList());
    }

    private sealed class ParsedSegment(long startMs, string text)
    {
        public long StartMs { get; set; } = startMs;
        public string Text { get; } = text;
    }
}
