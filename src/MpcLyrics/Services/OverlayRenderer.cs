using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using MpcLyrics.Core;

namespace MpcLyrics.Services;

public sealed class OverlayRenderer : IDisposable
{
    private float _originalScroll;
    private float _translationScroll;
    private float _nextOriginalScroll;
    private float _nextTranslationScroll;
    private int? _lineIndex;
    private Image? _backgroundImage;
    private string _backgroundImagePath = string.Empty;
    private DateTime _backgroundImageWriteTimeUtc;

    internal bool NeedsPositionUpdates { get; private set; }

    public void ResetScroll()
    {
        _originalScroll = 0;
        _translationScroll = 0;
        _nextOriginalScroll = 0;
        _nextTranslationScroll = 0;
        _lineIndex = null;
    }

    public Bitmap Render(
        int width,
        int height,
        AppSettings settings,
        LyricLine? line,
        LyricLine? nextLine,
        int? lineIndex,
        long positionMs)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        NeedsPositionUpdates = false;
        if (_lineIndex != lineIndex)
        {
            _lineIndex = lineIndex;
            _originalScroll = 0;
            _translationScroll = 0;
            _nextOriginalScroll = 0;
            _nextTranslationScroll = 0;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var backgroundGraphics = Graphics.FromImage(bitmap))
        {
            ConfigureGraphics(backgroundGraphics);
            backgroundGraphics.CompositingMode = CompositingMode.SourceCopy;
            backgroundGraphics.Clear(Color.Transparent);
            backgroundGraphics.CompositingMode = CompositingMode.SourceOver;
            DrawBackground(backgroundGraphics, width, height, settings);
            if (!settings.Locked)
            {
                // A fully transparent layered-window pixel is mouse-transparent on
                // Windows. Very low alpha values are also rounded away by some DWM/GDI
                // paths, so use a still-subtle edit surface that reliably receives input.
                using var editSurface = new SolidBrush(Color.FromArgb(16, 0, 0, 0));
                backgroundGraphics.FillRectangle(editSurface, 0, 0, width, height);
                if (width > 2 && height > 2)
                {
                    using var editBorder = new Pen(Color.FromArgb(220, 96, 165, 250), 2f);
                    backgroundGraphics.DrawRectangle(editBorder, 1, 1, width - 2, height - 2);
                }
            }
        }

        if (line is null)
        {
            return bitmap;
        }

        const float outerPadding = 4f;
        var contentArea = new RectangleF(
            outerPadding,
            outerPadding,
            Math.Max(1f, width - outerPadding * 2),
            Math.Max(1f, height - outerPadding * 2));
        var currentOriginalStyle = settings.GetTextStyle(LyricTextTrack.Original);
        var currentTranslationStyle = settings.GetTextStyle(LyricTextTrack.Translation);
        var nextOriginalStyle = settings.GetTextStyle(LyricTextTrack.NextOriginal);
        var nextTranslationStyle = settings.GetTextStyle(LyricTextTrack.NextTranslation);
        var currentVisible = HasVisibleText(line, currentOriginalStyle, currentTranslationStyle);
        var nextVisible = nextLine is not null
                          && HasVisibleText(nextLine, nextOriginalStyle, nextTranslationStyle);
        var reserveCurrentArea = ReservesSpace(currentOriginalStyle, line.Original)
                                 || ReservesSpace(currentTranslationStyle, line.Translation);
        var reserveNextArea = ReservesSpace(nextOriginalStyle, nextLine?.Original)
                              || ReservesSpace(nextTranslationStyle, nextLine?.Translation);
        using var graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);

        var currentArea = contentArea;
        var nextArea = contentArea;
        if (reserveCurrentArea && reserveNextArea)
        {
            var nextPosition = ResolveNextLinePosition(settings, lineIndex);
            SplitAreas(contentArea, nextPosition, out currentArea, out nextArea);
        }

        if (currentVisible)
        {
            NeedsPositionUpdates = DrawLyricGroup(
                graphics,
                currentArea,
                line,
                currentOriginalStyle,
                currentTranslationStyle,
                positionMs,
                ref _originalScroll,
                ref _translationScroll);
        }

        if (nextVisible && nextLine is not null)
        {
            _ = DrawLyricGroup(
                graphics,
                nextArea,
                nextLine,
                nextOriginalStyle,
                nextTranslationStyle,
                nextLine.TimeMs,
                ref _nextOriginalScroll,
                ref _nextTranslationScroll);
        }

        return bitmap;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.PageUnit = GraphicsUnit.Pixel;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static NextLinePosition ResolveNextLinePosition(
        AppSettings settings,
        int? lineIndex)
    {
        if (!settings.AlternateNextLinePosition
            || lineIndex is not int index
            || (index & 1) == 0)
        {
            return settings.NextLinePosition;
        }

        return GetOppositePosition(settings.NextLinePosition);
    }

    private static NextLinePosition GetOppositePosition(NextLinePosition position) => position switch
    {
        NextLinePosition.Left => NextLinePosition.Right,
        NextLinePosition.Top => NextLinePosition.Bottom,
        NextLinePosition.Right => NextLinePosition.Left,
        NextLinePosition.Bottom => NextLinePosition.Top,
        _ => NextLinePosition.Bottom,
    };

    private static bool HasVisibleText(
        LyricLine line,
        LyricTextStyle originalStyle,
        LyricTextStyle translationStyle) =>
        (originalStyle.Enabled && !string.IsNullOrWhiteSpace(line.Original))
        || (translationStyle.Enabled && !string.IsNullOrWhiteSpace(line.Translation));

    private static bool ReservesSpace(LyricTextStyle style, string? text) =>
        style.Enabled
        && (!style.HideWhenEmpty || !string.IsNullOrWhiteSpace(text));

    private static void SplitAreas(
        RectangleF area,
        NextLinePosition nextPosition,
        out RectangleF currentArea,
        out RectangleF nextArea)
    {
        const float gap = 6f;
        if (nextPosition is NextLinePosition.Left or NextLinePosition.Right)
        {
            var halfWidth = Math.Max(1f, (area.Width - gap) / 2f);
            var left = new RectangleF(area.Left, area.Top, halfWidth, area.Height);
            var right = new RectangleF(
                area.Left + halfWidth + gap,
                area.Top,
                halfWidth,
                area.Height);
            var nextOnLeft = nextPosition == NextLinePosition.Left;
            currentArea = nextOnLeft ? right : left;
            nextArea = nextOnLeft ? left : right;
            return;
        }

        var halfHeight = Math.Max(1f, (area.Height - gap) / 2f);
        var top = new RectangleF(area.Left, area.Top, area.Width, halfHeight);
        var bottom = new RectangleF(
            area.Left,
            area.Top + halfHeight + gap,
            area.Width,
            halfHeight);
        var nextOnTop = nextPosition == NextLinePosition.Top;
        currentArea = nextOnTop ? bottom : top;
        nextArea = nextOnTop ? top : bottom;
    }

    private static bool DrawLyricGroup(
        Graphics graphics,
        RectangleF area,
        LyricLine line,
        LyricTextStyle originalStyle,
        LyricTextStyle translationStyle,
        long positionMs,
        ref float originalScroll,
        ref float translationScroll)
    {
        var showOriginal = originalStyle.Enabled && !string.IsNullOrWhiteSpace(line.Original);
        var showTranslation = translationStyle.Enabled && !string.IsNullOrWhiteSpace(line.Translation);
        if (!showOriginal && !showTranslation) return false;

        var needsPositionUpdates = false;

        var originalArea = area;
        var translationArea = area;
        if (ReservesSpace(originalStyle, line.Original)
            && ReservesSpace(translationStyle, line.Translation))
        {
            var totalWeight = originalStyle.FontSize + translationStyle.FontSize;
            var originalRatio = totalWeight <= 0
                ? 0.5f
                : originalStyle.FontSize / totalWeight;
            originalRatio = Math.Clamp(originalRatio, 0.35f, 0.65f);
            var originalHeight = area.Height * originalRatio;
            originalArea = new RectangleF(area.Left, area.Top, area.Width, originalHeight);
            translationArea = new RectangleF(
                area.Left,
                area.Top + originalHeight,
                area.Width,
                Math.Max(1f, area.Height - originalHeight));
        }

        if (showOriginal)
        {
            needsPositionUpdates |= DrawLine(
                graphics,
                originalArea,
                line.Original,
                line.OriginalTokens,
                originalStyle,
                positionMs,
                ref originalScroll);
        }

        if (showTranslation)
        {
            needsPositionUpdates |= DrawLine(
                graphics,
                translationArea,
                line.Translation!,
                line.TranslationTokens,
                translationStyle,
                positionMs,
                ref translationScroll);
        }
        return needsPositionUpdates;
    }

    private static bool DrawLine(
        Graphics graphics,
        RectangleF area,
        string text,
        IReadOnlyList<TimedToken> timedTokens,
        LyricTextStyle style,
        long positionMs,
        ref float scroll)
    {
        if (string.IsNullOrEmpty(text) || area.Width <= 0 || area.Height <= 0) return false;

        var padding = Math.Max(3f, style.OutlineWidth + 2f);
        var effectiveFontSize = Math.Min(
            style.FontSize,
            Math.Max(1f, area.Height - style.OutlineWidth * 2f - 2f));
        var viewport = new RectangleF(
            area.Left + padding,
            area.Top,
            Math.Max(1f, area.Width - padding * 2),
            area.Height);
        using var fontFamily = CreateFontFamily();
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        var fontStyle = FontStyle.Regular;
        if (style.Bold) fontStyle |= FontStyle.Bold;
        if (style.Italic) fontStyle |= FontStyle.Italic;

        var tokens = timedTokens.Count > 0
            ? timedTokens
            : new[] { new TimedToken(0, long.MaxValue, text) };
        var layouts = MeasureTokens(
            graphics,
            tokens,
            fontFamily,
            fontStyle,
            effectiveFontSize,
            format);
        var totalWidth = layouts.Sum(item => item.Width);

        using var previousClip = graphics.Clip;
        graphics.SetClip(viewport);
        try
        {
            if (totalWidth <= viewport.Width + 0.5f)
            {
                scroll = 0;
                if (style.Alignment == LyricAlignment.Justify && layouts.Count > 1)
                {
                    var addedGap = (viewport.Width - totalWidth) / (layouts.Count - 1);
                    var x = viewport.Left;
                    foreach (var layout in layouts)
                    {
                        DrawTextPath(
                            graphics, layout.Token.Text, fontFamily, fontStyle, effectiveFontSize,
                            x, viewport,
                            style.TextColor, style.TextColorMode,
                            style.OutlineColor, style.OutlineColorMode,
                            style.OutlineWidth, format);
                        x += layout.Width + addedGap;
                    }
                }
                else
                {
                    var x = style.Alignment switch
                    {
                        LyricAlignment.Left => viewport.Left,
                        LyricAlignment.Right => viewport.Right - totalWidth,
                        _ => viewport.Left + (viewport.Width - totalWidth) / 2f,
                    };
                    DrawTextPath(
                        graphics, text, fontFamily, fontStyle, effectiveFontSize,
                        x, viewport,
                        style.TextColor, style.TextColorMode,
                        style.OutlineColor, style.OutlineColorMode,
                        style.OutlineWidth, format);
                }
                return false;
            }

            var target = CalculateScrollTarget(viewport.Width, totalWidth, layouts, positionMs);
            scroll = Math.Abs(target - scroll) < 0.35f
                ? target
                : scroll + (target - scroll) * 0.32f;
            var currentX = viewport.Left + scroll;
            foreach (var layout in layouts)
            {
                DrawTextPath(
                    graphics, layout.Token.Text, fontFamily, fontStyle, effectiveFontSize,
                    currentX, viewport,
                    style.TextColor, style.TextColorMode,
                    style.OutlineColor, style.OutlineColorMode,
                    style.OutlineWidth, format);
                currentX += layout.Width;
            }
            return true;
        }
        finally
        {
            graphics.Clip = previousClip;
        }
    }

    private static float CalculateScrollTarget(
        float viewportWidth,
        float textWidth,
        IReadOnlyList<TokenLayout> tokens,
        long positionMs)
    {
        if (tokens.Count == 0 || textWidth <= viewportWidth) return 0;
        var endOffset = viewportWidth - textWidth;
        if (tokens.Count == 1 || positionMs < tokens[1].Token.StartMs) return 0;
        var lastIndex = tokens.Count - 1;
        if (positionMs >= tokens[lastIndex].Token.StartMs) return endOffset;

        var active = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Token.StartMs <= positionMs) active = index;
            else break;
        }
        active = Math.Clamp(active, 1, lastIndex - 1);
        var desired = viewportWidth / 2f - tokens[active].CenterX;
        return Math.Clamp(desired, endOffset, 0f);
    }

    private static List<TokenLayout> MeasureTokens(
        Graphics graphics,
        IReadOnlyList<TimedToken> tokens,
        FontFamily family,
        FontStyle style,
        float size,
        StringFormat format)
    {
        using var font = new Font(family, size, style, GraphicsUnit.Pixel);
        var result = new List<TokenLayout>(tokens.Count);
        var cursor = 0f;
        foreach (var token in tokens)
        {
            var measured = graphics.MeasureString(
                token.Text.Length == 0 ? " " : token.Text,
                font,
                int.MaxValue,
                format);
            var width = Math.Max(0.5f, measured.Width);
            result.Add(new TokenLayout(token, width, cursor + width / 2f));
            cursor += width;
        }
        return result;
    }

    private static void DrawTextPath(
        Graphics graphics,
        string text,
        FontFamily family,
        FontStyle style,
        float size,
        float x,
        RectangleF verticalArea,
        RgbaColor textColor,
        ColorSourceMode textColorMode,
        RgbaColor outlineColor,
        ColorSourceMode outlineColorMode,
        float outlineWidth,
        StringFormat format)
    {
        if (text.Length == 0) return;
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)style, size, new PointF(0, 0), format);
        var bounds = path.GetBounds();
        using var matrix = new Matrix();
        var y = verticalArea.Top + (verticalArea.Height - bounds.Height) / 2f - bounds.Top;
        matrix.Translate(x - bounds.Left, y);
        path.Transform(matrix);

        if (outlineWidth > 0.01f && outlineColor.A > 0)
        {
            var resolved = ColorResolver.Resolve(outlineColorMode, outlineColor);
            using var pen = new Pen(resolved.ToDrawingColor(), outlineWidth)
            {
                LineJoin = LineJoin.Round,
            };
            graphics.DrawPath(pen, path);
        }
        if (textColor.A > 0)
        {
            var resolved = ColorResolver.Resolve(textColorMode, textColor);
            using var brush = new SolidBrush(resolved.ToDrawingColor());
            graphics.FillPath(brush, path);
        }
    }

    private static FontFamily CreateFontFamily()
    {
        try { return new FontFamily("Segoe UI Variable Display"); }
        catch { return new FontFamily("Segoe UI"); }
    }

    private void DrawBackground(
        Graphics graphics,
        int width,
        int height,
        AppSettings settings)
    {
        if (settings.BackgroundMode == LyricsBackgroundMode.SolidColor)
        {
            var color = ColorResolver.Resolve(
                settings.BackgroundColorMode,
                settings.BackgroundColor);
            using var brush = new SolidBrush(color.ToDrawingColor());
            graphics.FillRectangle(brush, 0, 0, width, height);
            return;
        }

        var image = GetBackgroundImage(settings.BackgroundImagePath);
        if (image is null || settings.BackgroundImageOpacity <= 0.001f) return;

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = settings.BackgroundImageOpacity };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        var destination = new RectangleF(0, 0, width, height);
        var source = new RectangleF(0, 0, image.Width, image.Height);
        switch (settings.BackgroundImageFill)
        {
            case BackgroundImageFillMode.Fit:
            {
                var scale = Math.Min(width / (float)image.Width, height / (float)image.Height);
                var drawWidth = image.Width * scale;
                var drawHeight = image.Height * scale;
                destination = new RectangleF(
                    (width - drawWidth) / 2f,
                    (height - drawHeight) / 2f,
                    drawWidth,
                    drawHeight);
                break;
            }
            case BackgroundImageFillMode.FillCrop:
            {
                var destinationRatio = width / (float)height;
                var sourceRatio = image.Width / (float)image.Height;
                if (sourceRatio > destinationRatio)
                {
                    var sourceWidth = image.Height * destinationRatio;
                    source = new RectangleF((image.Width - sourceWidth) / 2f, 0, sourceWidth, image.Height);
                }
                else
                {
                    var sourceHeight = image.Width / destinationRatio;
                    source = new RectangleF(0, (image.Height - sourceHeight) / 2f, image.Width, sourceHeight);
                }
                break;
            }
        }

        graphics.DrawImage(
            image,
            Rectangle.Round(destination),
            source.X,
            source.Y,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private Image? GetBackgroundImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClearBackgroundImage();
            return null;
        }

        DateTime writeTime;
        try
        {
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }

        if (_backgroundImage is not null &&
            string.Equals(_backgroundImagePath, path, StringComparison.OrdinalIgnoreCase) &&
            _backgroundImageWriteTimeUtc == writeTime)
        {
            return _backgroundImage;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var source = Image.FromStream(stream);
            var loaded = new Bitmap(source);
            ClearBackgroundImage();
            _backgroundImage = loaded;
            _backgroundImagePath = path;
            _backgroundImageWriteTimeUtc = writeTime;
            return _backgroundImage;
        }
        catch (Exception error)
        {
            AppLogger.Log($"Unable to load lyrics background image '{path}': {error.Message}");
            ClearBackgroundImage();
            return null;
        }
    }

    private void ClearBackgroundImage()
    {
        _backgroundImage?.Dispose();
        _backgroundImage = null;
        _backgroundImagePath = string.Empty;
        _backgroundImageWriteTimeUtc = default;
    }

    internal static void ExerciseForSmokeTest()
    {
        var current = new LyricLine(
            1_000,
            3_000,
            "Current lyric",
            "当前歌词",
            new[] { new TimedToken(1_000, 3_000, "Current lyric") },
            new[] { new TimedToken(1_000, 3_000, "当前歌词") });
        var next = new LyricLine(
            3_000,
            5_000,
            "Next lyric",
            "下一句歌词",
            new[] { new TimedToken(3_000, 5_000, "Next lyric") },
            new[] { new TimedToken(3_000, 5_000, "下一句歌词") });
        var settings = AppSettings.Default();
        using var renderer = new OverlayRenderer();

        settings.OriginalTextColorMode = (ColorSourceMode)2;
        settings.BackgroundColorMode = (ColorSourceMode)2;
        settings.Normalize();
        if (settings.OriginalTextColorMode != ColorSourceMode.Custom
            || settings.BackgroundColorMode != ColorSourceMode.Custom)
        {
            throw new InvalidOperationException("Legacy inverse colors were not migrated to custom colors.");
        }

        settings.Locked = true;
        settings.BackgroundColor = new RgbaColor(0, 0, 0, 0);
        using (var blank = renderer.Render(320, 80, settings, null, null, null, 0))
        {
            for (var y = 0; y < blank.Height; y += 2)
            {
                for (var x = 0; x < blank.Width; x += 2)
                {
                    if (blank.GetPixel(x, y).A != 0)
                        throw new InvalidOperationException("An empty lyric rendered visible pixels.");
                }
            }
        }

        var accent = ColorResolver.Resolve(
            ColorSourceMode.SystemAccent,
            new RgbaColor(1, 2, 3, 123));
        if (accent.A != 123)
            throw new InvalidOperationException("System accent transparency was not preserved.");

        settings = AppSettings.Default();
        settings.Locked = true;
        settings.BackgroundColor = new RgbaColor(0, 0, 0, 0);
        settings.AlternateNextLinePosition = false;
        settings.NextLinePosition = NextLinePosition.Bottom;
        settings.OriginalFontSize = 20f;
        settings.TranslationFontSize = 20f;
        settings.NextOriginalFontSize = 20f;
        settings.NextTranslationFontSize = 20f;
        settings.OriginalOutlineWidth = 0f;
        settings.TranslationOutlineWidth = 0f;
        settings.NextOriginalOutlineWidth = 0f;
        settings.NextTranslationOutlineWidth = 0f;
        settings.OriginalTextColor = new RgbaColor(255, 255, 255, 255);
        settings.TranslationTextColor = new RgbaColor(255, 255, 255, 255);
        settings.NextOriginalTextColor = new RgbaColor(255, 255, 255, 255);
        settings.NextTranslationTextColor = new RgbaColor(255, 255, 255, 255);
        using (var fourTracks = renderer.Render(
                   400, 240, settings, current, next, 0, 1_500))
        {
            if (CountHorizontalTextBands(fourTracks) != 4)
                throw new InvalidOperationException("Bilingual current/next layout did not render four tracks.");
            if (renderer.NeedsPositionUpdates)
                throw new InvalidOperationException("A fitting lyric requested redundant position renders.");
        }
        using (var scrollingTrack = renderer.Render(
                   80, 80, settings, current, next, 0, 1_500))
        {
            if (scrollingTrack.Width != 80 || !renderer.NeedsPositionUpdates)
                throw new InvalidOperationException("An overflowing lyric did not request scrolling renders.");
        }
        using (var finalLine = renderer.Render(
                   400, 240, settings, current, null, 0, 1_500))
        {
            for (var y = finalLine.Height / 2 + 4; y < finalLine.Height; y++)
            {
                for (var x = 0; x < finalLine.Width; x++)
                {
                    if (finalLine.GetPixel(x, y).A > 0)
                        throw new InvalidOperationException("The missing next line did not keep an empty slot.");
                }
            }
        }
        settings.NextOriginalHideWhenEmpty = true;
        settings.NextTranslationHideWhenEmpty = true;
        using (var collapsedFinalLine = renderer.Render(
                   400, 240, settings, current, null, 0, 1_500))
        {
            var foundTextInLowerHalf = false;
            for (var y = collapsedFinalLine.Height / 2; y < collapsedFinalLine.Height; y++)
            {
                for (var x = 0; x < collapsedFinalLine.Width; x++)
                {
                    if (collapsedFinalLine.GetPixel(x, y).A <= 8) continue;
                    foundTextInLowerHalf = true;
                    break;
                }
                if (foundTextInLowerHalf) break;
            }
            if (!foundTextInLowerHalf)
                throw new InvalidOperationException("Empty next tracks did not release their slot.");
        }

        var originalOnly = new LyricLine(
            current.TimeMs,
            current.EndMs,
            current.Original,
            null,
            current.OriginalTokens,
            Array.Empty<TimedToken>());
        settings.NextOriginalEnabled = false;
        settings.NextTranslationEnabled = false;
        settings.TranslationHideWhenEmpty = false;
        float reservedCenterY;
        using (var reservedTranslation = renderer.Render(
                   400, 240, settings, originalOnly, null, 0, 1_500))
        {
            reservedCenterY = GetVisiblePixelCenterY(reservedTranslation);
        }
        settings.TranslationHideWhenEmpty = true;
        using (var hiddenTranslation = renderer.Render(
                   400, 240, settings, originalOnly, null, 0, 1_500))
        {
            var expandedCenterY = GetVisiblePixelCenterY(hiddenTranslation);
            if (expandedCenterY < reservedCenterY + 30f)
            {
                throw new InvalidOperationException(
                    "An empty current translation did not release its inner slot.");
            }
        }

        settings = AppSettings.Default();
        settings.BackgroundColor = new RgbaColor(8, 12, 16, 255);

        foreach (var position in Enum.GetValues<NextLinePosition>())
        {
            settings.NextLinePosition = position;
            if (ResolveNextLinePosition(settings, 0) != position
                || ResolveNextLinePosition(settings, 1) != GetOppositePosition(position))
            {
                throw new InvalidOperationException(
                    $"Alternating next-line position failed for {position}.");
            }
            using var bitmap = renderer.Render(
                960,
                240,
                settings,
                current,
                next,
                0,
                1_500);
            if (bitmap.Width != 960 || bitmap.Height != 240 || bitmap.GetPixel(0, 0).A == 0)
                throw new InvalidOperationException($"Overlay render smoke test failed for {position}.");
        }
    }

    private static int CountHorizontalTextBands(Bitmap bitmap)
    {
        var bands = 0;
        var insideBand = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var rowHasText = false;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A <= 8) continue;
                rowHasText = true;
                break;
            }

            if (rowHasText && !insideBand) bands++;
            insideBand = rowHasText;
        }
        return bands;
    }

    private static float GetVisiblePixelCenterY(Bitmap bitmap)
    {
        long weightedY = 0;
        long count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A <= 8) continue;
                weightedY += y;
                count++;
            }
        }
        if (count == 0) throw new InvalidOperationException("Expected visible lyric pixels.");
        return weightedY / (float)count;
    }

    public void Dispose() => ClearBackgroundImage();

    private sealed record TokenLayout(TimedToken Token, float Width, float CenterX);
}
