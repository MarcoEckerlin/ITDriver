using System.Windows;

namespace ITDriver.App;

/// <summary>Attached property for the icon glyph (Segoe Fluent Icons / MDL2 Assets) of a navigation entry.</summary>
public static class NavProps
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(NavProps), new PropertyMetadata(""));

    public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string value) => o.SetValue(GlyphProperty, value);
}
