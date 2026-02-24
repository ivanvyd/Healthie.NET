using Microsoft.AspNetCore.Components;

namespace Healthie.Dashboard.Components;

/// <summary>
/// Inline SVG icons for the Healthie dashboard. Lucide-style, stroke-based,
/// using currentColor for CSS color inheritance.
/// </summary>
internal static class HealthieIcons
{
    // Status icons
    public static MarkupString Heart => Svg(
        "<path d='M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z'/>");

    public static MarkupString AlertTriangle => Svg(
        "<path d='m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z'/><path d='M12 9v4'/><path d='M12 17h.01'/>");

    public static MarkupString XCircle => Svg(
        "<circle cx='12' cy='12' r='10'/><path d='m15 9-6 6'/><path d='m9 9 6 6'/>");

    public static MarkupString HelpCircle => Svg(
        "<circle cx='12' cy='12' r='10'/><path d='M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3'/><path d='M12 17h.01'/>");

    public static MarkupString LayoutGrid => Svg(
        "<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/>");

    // Action icons
    public static MarkupString Play => Svg(
        "<polygon points='5 3 19 12 5 21 5 3'/>");

    public static MarkupString Pause => Svg(
        "<rect x='6' y='4' width='4' height='16' rx='1'/><rect x='14' y='4' width='4' height='16' rx='1'/>");

    public static MarkupString Zap => Svg(
        "<polygon points='13 2 3 14 12 14 11 22 21 10 12 10 13 2'/>");

    public static MarkupString Square => Svg(
        "<rect x='3' y='3' width='18' height='18' rx='2' ry='2'/>");

    public static MarkupString ChevronDown => Svg(
        "<path d='m6 9 6 6 6-6'/>");

    public static MarkupString Trash => Svg(
        "<polyline points='3 6 5 6 21 6'/><path d='M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>");

    public static MarkupString RotateCcw => Svg(
        "<polyline points='1 4 1 10 7 10'/><path d='M3.51 15a9 9 0 1 0 2.13-9.36L1 10'/>");

    public static MarkupString Check => Svg(
        "<polyline points='20 6 9 17 4 12'/>");

    // Theme toggle icons
    public static MarkupString Sun => Svg(
        "<circle cx='12' cy='12' r='5'/><line x1='12' y1='1' x2='12' y2='3'/><line x1='12' y1='21' x2='12' y2='23'/><line x1='4.22' y1='4.22' x2='5.64' y2='5.64'/><line x1='18.36' y1='18.36' x2='19.78' y2='19.78'/><line x1='1' y1='12' x2='3' y2='12'/><line x1='21' y1='12' x2='23' y2='12'/><line x1='4.22' y1='19.78' x2='5.64' y2='18.36'/><line x1='18.36' y1='5.64' x2='19.78' y2='4.22'/>");

    public static MarkupString Moon => Svg(
        "<path d='M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z'/>");

    private static MarkupString Svg(string content) => (MarkupString)
        $"<svg class='healthie-icon' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{content}</svg>";
}
