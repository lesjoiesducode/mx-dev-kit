namespace MxDevKit.Preview;

using Loupedeck.MxDevKitPlugin;

// Approximations of the Actions Ring for the README: the real ring draws the same images, smaller.
internal static class Previews
{
    private const int Radius = 170;
    private const int Diameter = 100;
    private const int Margin = 40;

    private static readonly BitmapColor Background = new(28, 33, 40);
    private static readonly BitmapColor CloseButton = new(110, 118, 129);
    private static readonly BitmapColor CloseCross = new(230, 237, 243);
    private static readonly BitmapColor Pill = new(246, 248, 250);
    private static readonly BitmapColor PillText = new(31, 35, 40);

    public static void SaveBubble(Bubble bubble, int size, string path)
    {
        using var b = new BitmapBuilder(size, size);
        BubblePainter.Paint(b, bubble);
        File.WriteAllBytes(path, b.ToArray(BitmapImageFormat.Png));
    }

    public static void SaveRing(Bubble[] ring, int hovered, string path)
    {
        var size = 2 * (Radius + Diameter / 2 + Margin);
        using var canvas = new BitmapBuilder(size, size);
        canvas.Clear(Background);
        DrawRing(canvas, ring, hovered, size / 2, size / 2);
        File.WriteAllBytes(path, canvas.ToArray(BitmapImageFormat.Png));
    }

    public static void SavePair(Bubble[] left, int leftHovered, Bubble[] right, int rightHovered, string path)
    {
        const int gap = 120;
        var half = 2 * (Radius + Diameter / 2 + Margin);
        using var canvas = new BitmapBuilder(half * 2 + gap, half);
        canvas.Clear(Background);
        DrawRing(canvas, left, leftHovered, half / 2, half / 2);
        DrawRing(canvas, right, rightHovered, half + gap + half / 2, half / 2);
        File.WriteAllBytes(path, canvas.ToArray(BitmapImageFormat.Png));
    }

    // Eight small bubbles around a centre, in the colours of the plugin.
    public static void SaveIcon(string path)
    {
        using var b = new BitmapBuilder(256, 256);
        b.Clear(BubblePainter.Color(Palette.Slate));
        var colours = new[] { Palette.Green, Palette.Purple, Palette.Red, Palette.Amber, Palette.Blue, Palette.Orange, Palette.Tomato, new Rgb(139, 148, 158) };
        for (var i = 0; i < 8; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 4;
            b.FillCircle(128 + (float)(Math.Cos(a) * 82), 128 + (float)(Math.Sin(a) * 82), 27, BubblePainter.Color(colours[i]));
        }

        b.FillCircle(128, 128, 15, new BitmapColor(110, 118, 129));
        File.WriteAllBytes(path, b.ToArray(BitmapImageFormat.Png));
    }

    private static void DrawRing(BitmapBuilder canvas, Bubble[] ring, int hovered, int cx, int cy)
    {
        var close = Diameter * 0.32f;
        canvas.FillCircle(cx, cy, close, CloseButton);
        var x = close * 0.32f;
        canvas.DrawLine(cx - x, cy - x, cx + x, cy + x, CloseCross, 3);
        canvas.DrawLine(cx + x, cy - x, cx - x, cy + x, CloseCross, 3);

        for (var i = 0; i < ring.Length; i++)
        {
            var a = -Math.PI / 2 + i * 2 * Math.PI / ring.Length;
            var bx = cx + (int)Math.Round(Math.Cos(a) * Radius);
            var by = cy + (int)Math.Round(Math.Sin(a) * Radius);
            canvas.DrawImage(RoundBubble(ring[i]), bx - Diameter / 2, by - Diameter / 2, Diameter, Diameter);

            if (i == hovered)
            {
                canvas.DrawArc(bx, by, Diameter / 2 + 4, 0, 360, CloseCross, 3);
                DrawLabel(canvas, ring[i].Label, bx, by, rightSide: Math.Cos(a) >= -0.01);
            }
        }
    }

    // Square bubble with the corners painted in the background colour, so it reads as a circle.
    private static byte[] RoundBubble(Bubble bubble)
    {
        using var b = new BitmapBuilder(Diameter, Diameter);
        BubblePainter.Paint(b, bubble);
        var band = Diameter * 0.26f;
        b.DrawArc(Diameter / 2, Diameter / 2, (int)(Diameter / 2 + band / 2), 0, 360, Background, band);
        return b.ToArray(BitmapImageFormat.Png);
    }

    private static void DrawLabel(BitmapBuilder canvas, string text, int bx, int by, bool rightSide)
    {
        const int height = 34;
        var width = 26 + (int)(text.Length * 10.5);
        var left = rightSide ? bx + Diameter / 2 + 14 : bx - Diameter / 2 - 14 - width;
        var top = by - height / 2;
        var r = height / 2;
        canvas.FillRectangle(left + r, top, width - 2 * r, height, Pill);
        canvas.FillCircle(left + r, top + r, r, Pill);
        canvas.FillCircle(left + width - r, top + r, r, Pill);
        canvas.DrawText(text, left, top, width, height, PillText, 19);
    }
}
