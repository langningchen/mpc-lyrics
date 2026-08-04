using Windows.UI.ViewManagement;

namespace MpcLyrics.Core;

internal static class ColorResolver
{
    private static readonly object Sync = new();
    private static DateTime _accentExpiresUtc;
    private static RgbaColor _cachedAccent = new(0, 120, 212, 255);

    public static RgbaColor Resolve(ColorSourceMode mode, RgbaColor configured) => mode switch
    {
        ColorSourceMode.SystemAccent => GetSystemAccent(configured.A),
        _ => configured,
    };

    public static RgbaColor GetSystemAccent(byte alpha)
    {
        lock (Sync)
        {
            if (DateTime.UtcNow >= _accentExpiresUtc)
            {
                try
                {
                    var color = new UISettings().GetColorValue(UIColorType.Accent);
                    _cachedAccent = new RgbaColor(color.R, color.G, color.B, 255);
                }
                catch
                {
                    _cachedAccent = new RgbaColor(0, 120, 212, 255);
                }
                _accentExpiresUtc = DateTime.UtcNow.AddSeconds(2);
            }

            return _cachedAccent.WithAlpha(alpha);
        }
    }
}
