namespace MpcLyrics.Core;

public enum LyricAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum LyricsBackgroundMode
{
    SolidColor,
    Image,
}

public enum BackgroundImageFillMode
{
    FillCrop,
    Fit,
    Stretch,
}

public enum ColorSourceMode
{
    Custom,
    SystemAccent,
}

public enum NextLinePosition
{
    Left,
    Top,
    Right,
    Bottom,
}

internal enum LyricTextTrack
{
    Original,
    Translation,
    NextOriginal,
    NextTranslation,
}

internal readonly record struct LyricTextStyle(
    bool Enabled,
    bool HideWhenEmpty,
    float FontSize,
    float OutlineWidth,
    bool Bold,
    bool Italic,
    LyricAlignment Alignment,
    RgbaColor TextColor,
    ColorSourceMode TextColorMode,
    RgbaColor OutlineColor,
    ColorSourceMode OutlineColorMode);

public sealed class AppSettings
{
    public int WindowX { get; set; } = 240;
    public int WindowY { get; set; } = 760;
    public int WindowWidth { get; set; } = 960;
    public int WindowHeight { get; set; } = 150;
    public bool Locked { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public string PlayerPath { get; set; } = string.Empty;

    public bool OriginalEnabled { get; set; } = true;
    public bool TranslationEnabled { get; set; } = true;
    public bool OriginalHideWhenEmpty { get; set; }
    public bool TranslationHideWhenEmpty { get; set; }
    public float OriginalFontSize { get; set; } = 34f;
    public float TranslationFontSize { get; set; } = 25f;
    public float OriginalOutlineWidth { get; set; } = 2.2f;
    public float TranslationOutlineWidth { get; set; } = 1.8f;
    public bool OriginalBold { get; set; } = true;
    public bool OriginalItalic { get; set; }
    public bool TranslationBold { get; set; } = true;
    public bool TranslationItalic { get; set; }
    public LyricAlignment OriginalAlignment { get; set; } = LyricAlignment.Center;
    public LyricAlignment TranslationAlignment { get; set; } = LyricAlignment.Center;

    public RgbaColor OriginalTextColor { get; set; } = new(255, 255, 255, 255);
    public ColorSourceMode OriginalTextColorMode { get; set; }
    public RgbaColor OriginalOutlineColor { get; set; } = new(0, 0, 0, 210);
    public ColorSourceMode OriginalOutlineColorMode { get; set; }
    public RgbaColor TranslationTextColor { get; set; } = new(225, 225, 225, 255);
    public ColorSourceMode TranslationTextColorMode { get; set; }
    public RgbaColor TranslationOutlineColor { get; set; } = new(0, 0, 0, 230);
    public ColorSourceMode TranslationOutlineColorMode { get; set; }

    public bool NextOriginalEnabled { get; set; } = true;
    public bool NextTranslationEnabled { get; set; } = true;
    public bool NextOriginalHideWhenEmpty { get; set; }
    public bool NextTranslationHideWhenEmpty { get; set; }
    public NextLinePosition NextLinePosition { get; set; } = MpcLyrics.Core.NextLinePosition.Bottom;
    public bool AlternateNextLinePosition { get; set; } = true;
    public float NextOriginalFontSize { get; set; } = 26f;
    public float NextTranslationFontSize { get; set; } = 20f;
    public float NextOriginalOutlineWidth { get; set; } = 1.8f;
    public float NextTranslationOutlineWidth { get; set; } = 1.5f;
    public bool NextOriginalBold { get; set; } = true;
    public bool NextOriginalItalic { get; set; }
    public bool NextTranslationBold { get; set; }
    public bool NextTranslationItalic { get; set; }
    public LyricAlignment NextOriginalAlignment { get; set; } = LyricAlignment.Center;
    public LyricAlignment NextTranslationAlignment { get; set; } = LyricAlignment.Center;
    public RgbaColor NextOriginalTextColor { get; set; } = new(220, 220, 220, 220);
    public ColorSourceMode NextOriginalTextColorMode { get; set; }
    public RgbaColor NextOriginalOutlineColor { get; set; } = new(0, 0, 0, 190);
    public ColorSourceMode NextOriginalOutlineColorMode { get; set; }
    public RgbaColor NextTranslationTextColor { get; set; } = new(190, 190, 190, 205);
    public ColorSourceMode NextTranslationTextColorMode { get; set; }
    public RgbaColor NextTranslationOutlineColor { get; set; } = new(0, 0, 0, 180);
    public ColorSourceMode NextTranslationOutlineColorMode { get; set; }

    public RgbaColor BackgroundColor { get; set; } = new(0, 0, 0, 0);
    public ColorSourceMode BackgroundColorMode { get; set; }
    public LyricsBackgroundMode BackgroundMode { get; set; } = LyricsBackgroundMode.SolidColor;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public BackgroundImageFillMode BackgroundImageFill { get; set; } = BackgroundImageFillMode.FillCrop;
    public float BackgroundImageOpacity { get; set; } = 1f;
    public bool AcrylicEnabled { get; set; }

    public static AppSettings Default() => new();

    public void Normalize()
    {
        WindowWidth = Math.Clamp(WindowWidth, 64, 4096);
        WindowHeight = Math.Clamp(WindowHeight, 20, 2160);
        OriginalFontSize = Math.Clamp(OriginalFontSize, 6f, 160f);
        TranslationFontSize = Math.Clamp(TranslationFontSize, 6f, 160f);
        NextOriginalFontSize = Math.Clamp(NextOriginalFontSize, 6f, 160f);
        NextTranslationFontSize = Math.Clamp(NextTranslationFontSize, 6f, 160f);
        OriginalOutlineWidth = Math.Clamp(OriginalOutlineWidth, 0f, 12f);
        TranslationOutlineWidth = Math.Clamp(TranslationOutlineWidth, 0f, 12f);
        NextOriginalOutlineWidth = Math.Clamp(NextOriginalOutlineWidth, 0f, 12f);
        NextTranslationOutlineWidth = Math.Clamp(NextTranslationOutlineWidth, 0f, 12f);
        if (!Enum.IsDefined(OriginalAlignment)) OriginalAlignment = LyricAlignment.Center;
        if (!Enum.IsDefined(TranslationAlignment)) TranslationAlignment = LyricAlignment.Center;
        if (!Enum.IsDefined(NextOriginalAlignment)) NextOriginalAlignment = LyricAlignment.Center;
        if (!Enum.IsDefined(NextTranslationAlignment)) NextTranslationAlignment = LyricAlignment.Center;
        if (!Enum.IsDefined(NextLinePosition))
            NextLinePosition = MpcLyrics.Core.NextLinePosition.Bottom;
        if (!Enum.IsDefined(BackgroundMode)) BackgroundMode = LyricsBackgroundMode.SolidColor;
        if (!Enum.IsDefined(BackgroundImageFill))
            BackgroundImageFill = BackgroundImageFillMode.FillCrop;
        OriginalTextColorMode = NormalizeColorMode(OriginalTextColorMode);
        OriginalOutlineColorMode = NormalizeColorMode(OriginalOutlineColorMode);
        TranslationTextColorMode = NormalizeColorMode(TranslationTextColorMode);
        TranslationOutlineColorMode = NormalizeColorMode(TranslationOutlineColorMode);
        NextOriginalTextColorMode = NormalizeColorMode(NextOriginalTextColorMode);
        NextOriginalOutlineColorMode = NormalizeColorMode(NextOriginalOutlineColorMode);
        NextTranslationTextColorMode = NormalizeColorMode(NextTranslationTextColorMode);
        NextTranslationOutlineColorMode = NormalizeColorMode(NextTranslationOutlineColorMode);
        BackgroundColorMode = NormalizeColorMode(BackgroundColorMode);
        BackgroundImageOpacity = Math.Clamp(BackgroundImageOpacity, 0f, 1f);
        PlayerPath ??= string.Empty;
        BackgroundImagePath ??= string.Empty;
    }

    internal LyricTextStyle GetTextStyle(LyricTextTrack track) => track switch
    {
        LyricTextTrack.Original => new(
            OriginalEnabled, OriginalHideWhenEmpty, OriginalFontSize, OriginalOutlineWidth,
            OriginalBold, OriginalItalic, OriginalAlignment,
            OriginalTextColor, OriginalTextColorMode,
            OriginalOutlineColor, OriginalOutlineColorMode),
        LyricTextTrack.Translation => new(
            TranslationEnabled, TranslationHideWhenEmpty, TranslationFontSize, TranslationOutlineWidth,
            TranslationBold, TranslationItalic, TranslationAlignment,
            TranslationTextColor, TranslationTextColorMode,
            TranslationOutlineColor, TranslationOutlineColorMode),
        LyricTextTrack.NextOriginal => new(
            NextOriginalEnabled, NextOriginalHideWhenEmpty, NextOriginalFontSize, NextOriginalOutlineWidth,
            NextOriginalBold, NextOriginalItalic, NextOriginalAlignment,
            NextOriginalTextColor, NextOriginalTextColorMode,
            NextOriginalOutlineColor, NextOriginalOutlineColorMode),
        LyricTextTrack.NextTranslation => new(
            NextTranslationEnabled, NextTranslationHideWhenEmpty, NextTranslationFontSize, NextTranslationOutlineWidth,
            NextTranslationBold, NextTranslationItalic, NextTranslationAlignment,
            NextTranslationTextColor, NextTranslationTextColorMode,
            NextTranslationOutlineColor, NextTranslationOutlineColorMode),
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    internal void SetTextStyle(LyricTextTrack track, LyricTextStyle style)
    {
        switch (track)
        {
            case LyricTextTrack.Original:
                OriginalEnabled = style.Enabled;
                OriginalHideWhenEmpty = style.HideWhenEmpty;
                OriginalFontSize = style.FontSize;
                OriginalOutlineWidth = style.OutlineWidth;
                OriginalBold = style.Bold;
                OriginalItalic = style.Italic;
                OriginalAlignment = style.Alignment;
                OriginalTextColor = style.TextColor;
                OriginalTextColorMode = style.TextColorMode;
                OriginalOutlineColor = style.OutlineColor;
                OriginalOutlineColorMode = style.OutlineColorMode;
                break;
            case LyricTextTrack.Translation:
                TranslationEnabled = style.Enabled;
                TranslationHideWhenEmpty = style.HideWhenEmpty;
                TranslationFontSize = style.FontSize;
                TranslationOutlineWidth = style.OutlineWidth;
                TranslationBold = style.Bold;
                TranslationItalic = style.Italic;
                TranslationAlignment = style.Alignment;
                TranslationTextColor = style.TextColor;
                TranslationTextColorMode = style.TextColorMode;
                TranslationOutlineColor = style.OutlineColor;
                TranslationOutlineColorMode = style.OutlineColorMode;
                break;
            case LyricTextTrack.NextOriginal:
                NextOriginalEnabled = style.Enabled;
                NextOriginalHideWhenEmpty = style.HideWhenEmpty;
                NextOriginalFontSize = style.FontSize;
                NextOriginalOutlineWidth = style.OutlineWidth;
                NextOriginalBold = style.Bold;
                NextOriginalItalic = style.Italic;
                NextOriginalAlignment = style.Alignment;
                NextOriginalTextColor = style.TextColor;
                NextOriginalTextColorMode = style.TextColorMode;
                NextOriginalOutlineColor = style.OutlineColor;
                NextOriginalOutlineColorMode = style.OutlineColorMode;
                break;
            case LyricTextTrack.NextTranslation:
                NextTranslationEnabled = style.Enabled;
                NextTranslationHideWhenEmpty = style.HideWhenEmpty;
                NextTranslationFontSize = style.FontSize;
                NextTranslationOutlineWidth = style.OutlineWidth;
                NextTranslationBold = style.Bold;
                NextTranslationItalic = style.Italic;
                NextTranslationAlignment = style.Alignment;
                NextTranslationTextColor = style.TextColor;
                NextTranslationTextColorMode = style.TextColorMode;
                NextTranslationOutlineColor = style.OutlineColor;
                NextTranslationOutlineColorMode = style.OutlineColorMode;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(track));
        }
    }

    private static ColorSourceMode NormalizeColorMode(ColorSourceMode mode) =>
        Enum.IsDefined(mode) ? mode : ColorSourceMode.Custom;
}
