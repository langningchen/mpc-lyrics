using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MpcLyrics.Core;
using SubtitlesParser.Classes;
using SubtitlesParser.Classes.Parsers;

namespace MpcLyrics.Services;

public static partial class SubtitleLoader
{
    private const long TranslationToleranceMs = 750;
    private const long LastCueFallbackMs = 6_000;

    public static readonly IReadOnlyList<string> SupportedExtensions = new[]
    {
        ".lrc", ".srt", ".vtt", ".ass", ".ssa", ".sub", ".sbv",
        ".ttml", ".dfxp", ".xml",
    };

    static SubtitleLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static LyricsDocument? LoadForMedia(string mediaPath)
    {
        // Keep the dedicated enhanced-LRC path: it preserves per-word timestamps
        // and the existing companion/embedded translation behavior.
        var lrc = LrcParser.LoadForAudio(mediaPath);
        if (lrc is not null) return lrc;

        var sourcePath = FindPrimarySubtitle(mediaPath);
        return sourcePath is null ? null : LoadTimedSubtitle(mediaPath, sourcePath);
    }

    internal static void ExerciseForSmokeTest()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "MpcLyricsSubtitleSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var samples = new[]
            {
                new SubtitleSample(
                    ".srt",
                    "1\n00:00:01,000 --> 00:00:03,000\nSRT smoke test\n\n",
                    "SRT smoke test"),
                new SubtitleSample(
                    ".vtt",
                    "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nVTT smoke test\n\n",
                    "VTT smoke test"),
                new SubtitleSample(
                    ".ass",
                    "[Script Info]\nScriptType: v4.00+\nWrapStyle: 0\n\n" +
                    "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
                    "Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,ASS smoke test\n",
                    "ASS smoke test"),
            };

            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                var mediaPath = Path.Combine(testRoot, $"sample-{index}.mp4");
                var subtitlePath = Path.ChangeExtension(mediaPath, sample.Extension);
                File.WriteAllBytes(mediaPath, Array.Empty<byte>());
                File.WriteAllText(subtitlePath, sample.Content, new UTF8Encoding(false));

                var document = LoadForMedia(mediaPath);
                if (document?.Lines.Count != 1
                    || document.Lines[0].Original != sample.ExpectedText
                    || document.Lines[0].TimeMs != 1_000
                    || document.Lines[0].EndMs != 3_000)
                {
                    throw new InvalidOperationException(
                        $"Subtitle smoke test failed for {sample.Extension}.");
                }
            }

            var bilingualMedia = Path.Combine(testRoot, "bilingual.mp4");
            File.WriteAllBytes(bilingualMedia, Array.Empty<byte>());
            File.WriteAllText(
                Path.ChangeExtension(bilingualMedia, ".srt"),
                "1\n00:00:01,000 --> 00:00:03,000\nOriginal line\n\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(testRoot, "bilingual.zh.srt"),
                "1\n00:00:01,000 --> 00:00:03,000\n翻译行\n\n",
                new UTF8Encoding(false));
            var bilingual = LoadForMedia(bilingualMedia);
            if (bilingual?.Lines.Single().Translation != "翻译行")
                throw new InvalidOperationException("Companion translation subtitle smoke test failed.");

            AppLogger.Startup("SMOKE_TEST: SRT, VTT, ASS, and companion translation parsing passed");
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static LyricsDocument? LoadTimedSubtitle(string mediaPath, string sourcePath)
    {
        var source = ParseFile(sourcePath);
        if (source.Count == 0) return null;

        var translationPath = FindTranslationSubtitle(mediaPath, sourcePath);
        List<ParsedCue>? translation = null;
        string? translationError = null;
        if (translationPath is not null)
        {
            try
            {
                translation = ParseFile(translationPath);
            }
            catch (Exception error)
            {
                translationError = $"Unable to read {translationPath}: {error.Message}";
            }
        }

        var lines = new List<LyricLine>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var cue = source[index];
            var endMs = cue.EndMs > cue.StartMs
                ? cue.EndMs
                : index + 1 < source.Count && source[index + 1].StartMs > cue.StartMs
                    ? source[index + 1].StartMs
                    : cue.StartMs + LastCueFallbackMs;

            var original = cue.Lines[0];
            string? translated = cue.Lines.Count > 1
                ? string.Join(" / ", cue.Lines.Skip(1))
                : null;
            if (translation is { Count: > 0 })
            {
                var nearest = FindNearestCue(translation, cue.StartMs);
                if (nearest is not null)
                    translated = string.Join(" / ", nearest.Lines);
            }

            lines.Add(new LyricLine(
                cue.StartMs,
                endMs,
                original,
                translated,
                BuildTimedTokens(original, cue.StartMs, endMs),
                string.IsNullOrWhiteSpace(translated)
                    ? Array.Empty<TimedToken>()
                    : BuildTimedTokens(translated, cue.StartMs, endMs)));
        }

        return new LyricsDocument
        {
            Lines = lines,
            SourcePath = sourcePath,
            TranslationPath = translationPath,
            TranslationError = translationError,
        };
    }

    private static List<ParsedCue> ParseFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        var parser = new SubParser();
        var format = parser.GetMostLikelyFormat(path);
        var items = parser.ParseStream(stream, encoding, format);

        return items
            .Where(item => item.StartTime >= 0)
            .Select(item => new ParsedCue(
                item.StartTime,
                item.EndTime,
                NormalizeLines(item)))
            .Where(item => item.Lines.Count > 0)
            .OrderBy(item => item.StartMs)
            .ToList();
    }

    private static List<string> NormalizeLines(SubtitleItem item)
    {
        var candidates = item.PlaintextLines.Count > 0 ? item.PlaintextLines : item.Lines;
        return candidates
            .Select(line => WebUtility.HtmlDecode(FormattingTagRegex().Replace(line, string.Empty)))
            .Select(line => line.Replace(@"\N", " ").Replace(@"\n", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static string? FindPrimarySubtitle(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        foreach (var extension in SupportedExtensions.Skip(1))
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory, stem + extension),
                         mediaPath + extension,
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string? FindTranslationSubtitle(string mediaPath, string sourcePath)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        var suffixes = new[]
        {
            "trans", "translation", "translated", "zh", "zh-CN", "zh_CN",
            "cn", "chs", "中文", "翻译",
        };

        foreach (var suffix in suffixes)
        {
            foreach (var extension in SupportedExtensions.Skip(1))
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(directory, $"{stem}.{suffix}{extension}"),
                             Path.Combine(directory, $"{stem} ({suffix}){extension}"),
                             Path.Combine(directory, $"{stem} [{suffix}]{extension}"),
                         })
                {
                    if (!string.Equals(
                            Path.GetFullPath(candidate),
                            Path.GetFullPath(sourcePath),
                            StringComparison.OrdinalIgnoreCase)
                        && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }

    private static ParsedCue? FindNearestCue(IReadOnlyList<ParsedCue> cues, long timeMs)
    {
        ParsedCue? nearest = null;
        var nearestDistance = long.MaxValue;
        foreach (var cue in cues)
        {
            var distance = Math.Abs(cue.StartMs - timeMs);
            if (distance < nearestDistance)
            {
                nearest = cue;
                nearestDistance = distance;
            }
            if (cue.StartMs > timeMs + TranslationToleranceMs) break;
        }
        return nearestDistance <= TranslationToleranceMs ? nearest : null;
    }

    private static IReadOnlyList<TimedToken> BuildTimedTokens(string text, long startMs, long endMs)
    {
        var units = LrcParser.SplitDisplayUnits(text);
        if (units.Count == 0) return Array.Empty<TimedToken>();

        var weights = units
            .Select(unit => Math.Clamp(unit.Count(character => !char.IsWhiteSpace(character)), 1, 8))
            .ToArray();
        var totalWeight = Math.Max(1L, weights.Sum(weight => (long)weight));
        var duration = Math.Max(1, endMs - startMs);
        long elapsedWeight = 0;
        var tokens = new List<TimedToken>(units.Count);
        for (var index = 0; index < units.Count; index++)
        {
            var tokenStart = startMs + duration * elapsedWeight / totalWeight;
            elapsedWeight += weights[index];
            var tokenEnd = index == units.Count - 1
                ? endMs
                : startMs + duration * elapsedWeight / totalWeight;
            tokens.Add(new TimedToken(tokenStart, Math.Max(tokenStart + 1, tokenEnd), units[index]));
        }
        return tokens;
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return Encoding.UTF8;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return Encoding.Unicode;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return Encoding.BigEndianUnicode;

        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(936);
        }
    }

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex FormattingTagRegex();

    private sealed record ParsedCue(long StartMs, long EndMs, IReadOnlyList<string> Lines);

    private sealed record SubtitleSample(string Extension, string Content, string ExpectedText);
}
