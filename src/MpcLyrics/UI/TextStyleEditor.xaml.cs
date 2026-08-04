using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MpcLyrics.Core;
using Windows.Globalization.NumberFormatting;

namespace MpcLyrics.UI;

public sealed partial class TextStyleEditor : UserControl
{
    private AppSettings? _settings;
    private LyricTextTrack _track;
    private Action? _changed;
    private bool _updating = true;

    public TextStyleEditor()
    {
        InitializeComponent();
        OutlineWidthBox.NumberFormatter = new DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 1,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.1,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
            },
        };
    }

    internal void Configure(
        AppSettings settings,
        LyricTextTrack track,
        string heading,
        Action changed)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _track = track;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        HeadingText.Text = heading;
        Reload();
    }

    internal void Reload()
    {
        if (_settings is null) return;
        _updating = true;
        try
        {
            var style = _settings.GetTextStyle(_track);
            EnabledToggle.IsOn = style.Enabled;
            HideWhenEmptyToggle.IsOn = style.HideWhenEmpty;
            FontSizeBox.Value = style.FontSize;
            OutlineWidthBox.Value = Math.Round(style.OutlineWidth, 1);
            BoldButton.IsChecked = style.Bold;
            ItalicButton.IsChecked = style.Italic;
            AlignLeftButton.IsChecked = style.Alignment == LyricAlignment.Left;
            AlignCenterButton.IsChecked = style.Alignment == LyricAlignment.Center;
            AlignRightButton.IsChecked = style.Alignment == LyricAlignment.Right;
            AlignJustifyButton.IsChecked = style.Alignment == LyricAlignment.Justify;
            ReloadColorControls(style);
        }
        finally
        {
            _updating = false;
        }
    }

    internal void FocusEditor() => FontSizeBox.Focus(FocusState.Programmatic);

    internal void ExerciseColorModesForSmokeTest()
    {
        if (_settings is null) throw new InvalidOperationException("Editor is not configured.");
        var original = _settings.GetTextStyle(_track);
        try
        {
            _settings.SetTextStyle(_track, original with
            {
                HideWhenEmpty = !original.HideWhenEmpty,
                TextColorMode = ColorSourceMode.SystemAccent,
                OutlineColorMode = ColorSourceMode.SystemAccent,
                TextColor = original.TextColor.WithAlpha(128),
                OutlineColor = original.OutlineColor.WithAlpha(96),
            });
            Reload();
            if (FillColorPicker.IsEnabled || OutlineColorPicker.IsEnabled)
                throw new InvalidOperationException("Dynamic text colors left a picker enabled.");
            if (HideWhenEmptyToggle.IsOn == original.HideWhenEmpty)
                throw new InvalidOperationException("The empty-content visibility toggle was not loaded.");
            if (Math.Abs(FillOpacitySlider.Value - 128d / 255d * 100d) > 0.6d)
                throw new InvalidOperationException("System text-color opacity was not loaded.");

            _settings.SetTextStyle(_track, original with
            {
                TextColorMode = ColorSourceMode.Custom,
                OutlineColorMode = ColorSourceMode.Custom,
            });
            Reload();
            if (!FillColorPicker.IsEnabled || !OutlineColorPicker.IsEnabled)
                throw new InvalidOperationException("Custom text colors did not enable their pickers.");
        }
        finally
        {
            _settings.SetTextStyle(_track, original);
            Reload();
        }
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e) =>
        Update(style => style with { Enabled = EnabledToggle.IsOn });

    private void HideWhenEmptyToggle_Toggled(object sender, RoutedEventArgs e) =>
        Update(style => style with { HideWhenEmpty = HideWhenEmptyToggle.IsOn });

    private void FontSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        Update(style => style with { FontSize = (float)args.NewValue });
    }

    private void OutlineWidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        Update(style => style with { OutlineWidth = (float)args.NewValue });
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e) =>
        Update(style => style with { Bold = BoldButton.IsChecked == true });

    private void ItalicButton_Click(object sender, RoutedEventArgs e) =>
        Update(style => style with { Italic = ItalicButton.IsChecked == true });

    private void AlignLeftButton_Click(object sender, RoutedEventArgs e) =>
        SetAlignment(LyricAlignment.Left);

    private void AlignCenterButton_Click(object sender, RoutedEventArgs e) =>
        SetAlignment(LyricAlignment.Center);

    private void AlignRightButton_Click(object sender, RoutedEventArgs e) =>
        SetAlignment(LyricAlignment.Right);

    private void AlignJustifyButton_Click(object sender, RoutedEventArgs e) =>
        SetAlignment(LyricAlignment.Justify);

    private void SetAlignment(LyricAlignment alignment)
    {
        Update(style => style with { Alignment = alignment });
        Reload();
    }

    private void OutlineColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_updating || _settings is null) return;
        var style = _settings.GetTextStyle(_track);
        if (style.OutlineColorMode != ColorSourceMode.Custom) return;
        var color = new RgbaColor(
            args.NewColor.R,
            args.NewColor.G,
            args.NewColor.B,
            style.OutlineColor.A);
        Update(current => current with { OutlineColor = color });
        UpdateColorPreview(OutlineColorPreview, ColorSourceMode.Custom, color);
    }

    private void FillColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_updating || _settings is null) return;
        var style = _settings.GetTextStyle(_track);
        if (style.TextColorMode != ColorSourceMode.Custom) return;
        var color = new RgbaColor(
            args.NewColor.R,
            args.NewColor.G,
            args.NewColor.B,
            style.TextColor.A);
        Update(current => current with { TextColor = color });
        UpdateColorPreview(FillColorPreview, ColorSourceMode.Custom, color);
    }

    private void OutlineColorModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || OutlineColorModeCombo.SelectedIndex < 0) return;
        Update(style => style with
        {
            OutlineColorMode = (ColorSourceMode)OutlineColorModeCombo.SelectedIndex,
        });
        Reload();
    }

    private void FillColorModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || FillColorModeCombo.SelectedIndex < 0) return;
        Update(style => style with
        {
            TextColorMode = (ColorSourceMode)FillColorModeCombo.SelectedIndex,
        });
        Reload();
    }

    private void OutlineOpacitySlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        var alpha = PercentToAlpha(e.NewValue);
        OutlineOpacityValue.Text = $"{e.NewValue:0}%";
        Update(style => style with { OutlineColor = style.OutlineColor.WithAlpha(alpha) });
        if (_settings is not null)
        {
            var style = _settings.GetTextStyle(_track);
            UpdateColorPreview(OutlineColorPreview, style.OutlineColorMode, style.OutlineColor);
        }
    }

    private void FillOpacitySlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        var alpha = PercentToAlpha(e.NewValue);
        FillOpacityValue.Text = $"{e.NewValue:0}%";
        Update(style => style with { TextColor = style.TextColor.WithAlpha(alpha) });
        if (_settings is not null)
        {
            var style = _settings.GetTextStyle(_track);
            UpdateColorPreview(FillColorPreview, style.TextColorMode, style.TextColor);
        }
    }

    private void Update(Func<LyricTextStyle, LyricTextStyle> update)
    {
        if (_updating || _settings is null) return;
        _settings.SetTextStyle(_track, update(_settings.GetTextStyle(_track)));
        _settings.Normalize();
        _changed?.Invoke();
    }

    private void ReloadColorControls(LyricTextStyle style)
    {
        OutlineColorModeCombo.SelectedIndex = (int)style.OutlineColorMode;
        FillColorModeCombo.SelectedIndex = (int)style.TextColorMode;
        OutlineColorPicker.IsEnabled = style.OutlineColorMode == ColorSourceMode.Custom;
        FillColorPicker.IsEnabled = style.TextColorMode == ColorSourceMode.Custom;
        OutlineColorPicker.Color = GetPickerColor(style.OutlineColorMode, style.OutlineColor);
        FillColorPicker.Color = GetPickerColor(style.TextColorMode, style.TextColor);
        OutlineOpacitySlider.Value = AlphaToPercent(style.OutlineColor.A);
        FillOpacitySlider.Value = AlphaToPercent(style.TextColor.A);
        OutlineOpacityValue.Text = $"{AlphaToPercent(style.OutlineColor.A):0}%";
        FillOpacityValue.Text = $"{AlphaToPercent(style.TextColor.A):0}%";
        UpdateColorPreview(OutlineColorPreview, style.OutlineColorMode, style.OutlineColor);
        UpdateColorPreview(FillColorPreview, style.TextColorMode, style.TextColor);
    }

    private static Windows.UI.Color GetPickerColor(ColorSourceMode mode, RgbaColor configured)
    {
        var color = ColorResolver.Resolve(mode, configured).WithAlpha(255);
        return color.ToWindowsColor();
    }

    private static void UpdateColorPreview(
        Border preview,
        ColorSourceMode mode,
        RgbaColor configured)
    {
        preview.Background = new SolidColorBrush(
            ColorResolver.Resolve(mode, configured).ToWindowsColor());
    }

    private static double AlphaToPercent(byte alpha) => alpha / 255d * 100d;

    private static byte PercentToAlpha(double percent) =>
        (byte)Math.Clamp((int)Math.Round(percent / 100d * 255d), 0, 255);
}
