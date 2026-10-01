namespace ReelDiscovery.Helpers;

public static class ButtonHelper
{
    /// <summary>
    /// Applies the theme variant to a button created by CreateButton (a ThemedButton).
    /// </summary>
    public static void StyleButton(Button button, ButtonStyle style = ButtonStyle.Default)
    {
        if (button is ThemedButton themed)
        {
            themed.Variant = style;
        }
    }

    /// <summary>
    /// Creates a rounded theme button. minWidth and height are minimums in device pixels;
    /// the button grows to fit its text and font (so it stays proportional at high DPI).
    /// </summary>
    public static Button CreateButton(string text, int minWidth, int height, ButtonStyle style = ButtonStyle.Default)
    {
        var button = new ThemedButton { Text = text, AutoSize = false };
        button.Variant = style;
        button.FitToText(minWidth, height);
        return button;
    }
}

public enum ButtonStyle
{
    Default,
    Primary,
    Success,
    Secondary,
    Danger,

    /// <summary>
    /// Segment of a two-way toggle; set ThemedButton.Selected for the active one.
    /// </summary>
    Toggle
}
