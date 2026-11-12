namespace Loupedeck.MxDevKitPlugin;

// Draws a Bubble with BitmapBuilder. Shared with tools/BubblePreview, which renders the README images.
// The ring shows bubbles as circles, so everything stays inside the inscribed circle.
public static class BubblePainter
{
    private static readonly BitmapColor White = BitmapColor.White;
    private static readonly BitmapColor Track = new(255, 255, 255, 70);
    private static readonly BitmapColor SubtitleColor = new(255, 255, 255, 170);

    public static string? TitleFont { get; set; } = OperatingSystem.IsMacOS() ? "Avenir Next Condensed" : null;

    public static void Paint(BitmapBuilder b, Bubble bubble)
    {
        var w = b.Width;
        var h = b.Height;
        var s = (float)Math.Min(w, h);
        b.Clear(Color(bubble.Background));

        if (bubble.Progress is { } progress)
        {
            var radius = (int)(s * 0.445f);
            var stroke = Math.Max(2f, s * 0.035f);
            b.DrawArc(w / 2, h / 2, radius, 0, 360, Track, stroke);
            if (progress > 0)
            {
                b.DrawArc(w / 2, h / 2, radius, -90, (float)(360 * Math.Clamp(progress, 0, 1)), White, stroke);
            }
        }

        if (bubble.IsTitle)
        {
            PaintTitle(b, bubble, w, h, s);
            return;
        }

        if (bubble.Logo != AgentLogo.None)
        {
            PaintLogo(b, bubble, w, h, s);
            return;
        }

        var stroke2 = Math.Max(2.5f, s * 0.042f);
        if (string.IsNullOrEmpty(bubble.Value))
        {
            DrawGlyph(b, bubble.Glyph, w / 2f, h / 2f, s * 0.2f, stroke2 * 1.3f);
            return;
        }

        if (bubble.Glyph != Glyph.None)
        {
            DrawGlyph(b, bubble.Glyph, w / 2f, h * 0.24f, s * 0.1f, stroke2);
        }

        // Numbers very big, short words a bit less, longer words smaller still.
        var value = bubble.Value;
        var number = char.IsDigit(value[0]);
        var size = number && value.Length <= 2 ? s * 0.36 : value.Length <= 4 ? s * 0.26 : value.Length <= 9 ? s * 0.19 : s * 0.16;
        b.DrawText(value, 0, (int)(h * 0.48), w, (int)(h * 0.42), White, (int)size);
    }

    // The agent's mark, big in the middle, or small above a word ("Install").
    private static void PaintLogo(BitmapBuilder b, Bubble bubble, int w, int h, float s)
    {
        var alone = string.IsNullOrEmpty(bubble.Value);
        var px = (int)(s * (alone ? 0.5f : 0.3f));
        var cy = alone ? h / 2f : h * 0.33f;
        var image = AgentLogos.Image(bubble.Logo, px, bubble.LogoMono);
        if (image != null)
        {
            b.DrawImage(image, (int)(w / 2f - px / 2f), (int)(cy - px / 2f), px, px);
        }
        else
        {
            DrawGlyph(b, Glyph.Prompt, w / 2f, cy, px / 2.4f, Math.Max(2.5f, s * 0.042f));
        }

        if (!alone)
        {
            b.DrawText(bubble.Value, 0, (int)(h * 0.52), w, (int)(h * 0.3), SubtitleColor, (int)(s * 0.17));
        }
    }

    // Project name on one or two lines (cut at the most central dash), branch underneath.
    private static void PaintTitle(BitmapBuilder b, Bubble bubble, int w, int h, float s)
    {
        var name = bubble.Value;
        var lines = new[] { name };
        if (name.Length > 11)
        {
            var cut = -1;
            for (var i = 1; i < name.Length - 1; i++)
            {
                if ((name[i] is '-' or '_' or ' ' or '.') && (cut < 0 || Math.Abs(i - name.Length / 2) < Math.Abs(cut - name.Length / 2)))
                {
                    cut = i;
                }
            }

            if (cut > 0)
            {
                lines = new[] { name[..(cut + 1)].TrimEnd(' '), name[(cut + 1)..] };
            }
        }

        var longest = lines.Max(l => l.Length);
        var size = Math.Min(s * 0.3, w * 1.9 / Math.Max(4, longest));
        var height = lines.Length == 1 ? h * 0.34 : h * 0.52;
        var top = bubble.Subtitle == null ? (h - height) / 2 : h * 0.64 - height;
        b.DrawText(string.Join("\n", lines), (int)(w * 0.06), (int)top, (int)(w * 0.88), (int)height, White,
            fontSize: (int)size, lineHeight: (int)(size * 1.05), fontName: TitleFont);

        if (bubble.Subtitle != null)
        {
            var sub = bubble.Subtitle.Length > 16 ? bubble.Subtitle[..15] + "…" : bubble.Subtitle;
            b.DrawText(sub, (int)(w * 0.14), (int)(h * 0.66), (int)(w * 0.72), (int)(h * 0.18), SubtitleColor,
                fontSize: (int)(s * 0.14), fontName: TitleFont);
        }
    }

    // A pictogram: the octicon, or the same shape in lines if Skia cannot read the SVG.
    // (cx, cy): centre, r: half size, t: stroke of the lines.
    public static void DrawGlyph(BitmapBuilder b, Glyph glyph, float cx, float cy, float r, float t)
    {
        var px = Math.Max(1, (int)MathF.Round(r * 2.4f));
        if (Octicons.Image(glyph, px) is { } image)
        {
            b.DrawImage(image, (int)MathF.Round(cx - px / 2f), (int)MathF.Round(cy - px / 2f), px, px);
            return;
        }

        DrawLineGlyph(b, glyph, cx, cy, r, t);
    }

    private static void DrawLineGlyph(BitmapBuilder b, Glyph glyph, float cx, float cy, float r, float t)
    {
        // Circles are drawn on whole pixels: a fractional centre would leave the lines of a glyph
        // a pixel away from its circles in the small images the ring asks for.
        cx = MathF.Round(cx);
        cy = MathF.Round(cy);
        var c = White;
        switch (glyph)
        {
            case Glyph.Issue:
                Circle(b, cx, cy, r, c, t);
                Dot(b, cx, cy, t * 2.2f, c);
                break;

            case Glyph.PullRequest:
            {
                var left = cx - r * 0.75f;
                var right = cx + r * 0.75f;
                var top = cy - r * 0.8f;
                var bottom = cy + r * 0.8f;
                var p = r * 0.28f;
                var tt = t * 0.8f;
                Circle(b, left, top, p, c, tt);
                Circle(b, left, bottom, p, c, tt);
                b.DrawLine(left, top + p, left, bottom - p, c, tt);
                Circle(b, right, bottom, p, c, tt);
                b.DrawLine(right, bottom - p, right, top + p * 0.6f, c, tt);
                b.DrawLine(right, top + p * 0.6f, right - p, top, c, tt);
                b.DrawLine(right - p, top, cx - p * 0.2f, top, c, tt);
                b.DrawLine(cx - p * 0.2f, top, cx + p * 0.7f, top - p * 0.9f, c, tt);
                b.DrawLine(cx - p * 0.2f, top, cx + p * 0.7f, top + p * 0.9f, c, tt);
                break;
            }

            case Glyph.Check:
                Circle(b, cx, cy, r, c, t);
                b.DrawLine(cx - r * 0.45f, cy + r * 0.02f, cx - r * 0.1f, cy + r * 0.38f, c, t);
                b.DrawLine(cx - r * 0.1f, cy + r * 0.38f, cx + r * 0.5f, cy - r * 0.35f, c, t);
                break;

            case Glyph.Cross:
                Circle(b, cx, cy, r, c, t);
                b.DrawLine(cx - r * 0.38f, cy - r * 0.38f, cx + r * 0.38f, cy + r * 0.38f, c, t);
                b.DrawLine(cx + r * 0.38f, cy - r * 0.38f, cx - r * 0.38f, cy + r * 0.38f, c, t);
                break;

            case Glyph.Clock:
                Circle(b, cx, cy, r, c, t);
                b.DrawLine(cx, cy, cx, cy - r * 0.55f, c, t);
                b.DrawLine(cx, cy, cx + r * 0.4f, cy + r * 0.25f, c, t);
                break;

            case Glyph.Timer:
                Circle(b, cx, cy + r * 0.12f, r * 0.88f, c, t);
                b.DrawLine(cx - r * 0.3f, cy - r * 1.05f, cx + r * 0.3f, cy - r * 1.05f, c, t);
                b.DrawLine(cx, cy + r * 0.12f, cx + r * 0.38f, cy - r * 0.3f, c, t);
                break;

            case Glyph.Shield:
            {
                var topY = cy - r;
                var shoulder = cy - r * 0.55f;
                var flank = cy + r * 0.15f;
                var tip = cy + r * 1.05f;
                var l = r * 0.85f;
                b.DrawLine(cx, topY, cx + l, shoulder, c, t);
                b.DrawLine(cx + l, shoulder, cx + l, flank, c, t);
                b.DrawLine(cx + l, flank, cx, tip, c, t);
                b.DrawLine(cx, tip, cx - l, flank, c, t);
                b.DrawLine(cx - l, flank, cx - l, shoulder, c, t);
                b.DrawLine(cx - l, shoulder, cx, topY, c, t);
                break;
            }

            case Glyph.Commit:
                Circle(b, cx, cy, r * 0.42f, c, t);
                b.DrawLine(cx - r * 1.1f, cy, cx - r * 0.42f, cy, c, t);
                b.DrawLine(cx + r * 0.42f, cy, cx + r * 1.1f, cy, c, t);
                break;

            case Glyph.ArrowUp:
                b.DrawLine(cx, cy + r * 0.7f, cx, cy - r, c, t);
                b.DrawLine(cx, cy - r, cx - r * 0.6f, cy - r * 0.4f, c, t);
                b.DrawLine(cx, cy - r, cx + r * 0.6f, cy - r * 0.4f, c, t);
                break;

            case Glyph.ArrowDown:
                b.DrawLine(cx, cy - r, cx, cy + r * 0.7f, c, t);
                b.DrawLine(cx, cy + r * 0.7f, cx - r * 0.6f, cy + r * 0.1f, c, t);
                b.DrawLine(cx, cy + r * 0.7f, cx + r * 0.6f, cy + r * 0.1f, c, t);
                break;

            case Glyph.Eye:
            {
                // Two arcs meeting at (cx +- r, cy), and the pupil.
                var k = r * 0.8f;
                var radius = (int)Math.Sqrt(r * r + k * k);
                var a = (float)(Math.Atan2(k, r) * 180 / Math.PI);
                b.DrawArc((int)cx, (int)(cy + k), radius, -180 + a, 180 - 2 * a, c, t);
                b.DrawArc((int)cx, (int)(cy - k), radius, a, 180 - 2 * a, c, t);
                Dot(b, cx, cy, r * 0.62f, c);
                break;
            }

            case Glyph.Bell:
            {
                // Dome, flared skirt, rim and clapper.
                var top = cy - r * 0.15f;
                var w = r * 0.62f;
                b.DrawArc((int)cx, (int)top, (int)w, 180, 180, c, t);
                b.DrawLine(cx - w, top, cx - w * 1.05f, cy + r * 0.5f, c, t);
                b.DrawLine(cx + w, top, cx + w * 1.05f, cy + r * 0.5f, c, t);
                b.DrawLine(cx - r * 0.95f, cy + r * 0.62f, cx + r * 0.95f, cy + r * 0.62f, c, t);
                Dot(b, cx, cy + r * 0.92f, t * 2.2f, c);
                b.DrawLine(cx, top - w, cx, top - w - r * 0.18f, c, t);
                break;
            }

            case Glyph.At:
            {
                // @: a small ring, its stem on the right, and the big circle that the stem's hook opens into.
                var tt = t * 0.7f;
                var inner = r * 0.36f;
                Circle(b, cx, cy, inner, c, tt);
                b.DrawLine(cx + inner, cy - inner * 1.1f, cx + inner, cy + inner * 0.5f, c, tt);
                b.DrawArc((int)(cx + inner * 1.55f), (int)(cy + inner * 0.5f), Math.Max(1, (int)(inner * 0.55f)), 0, 180, c, tt);
                b.DrawArc((int)cx, (int)cy, (int)(r * 1.05f), 15, 300, c, tt);
                break;
            }

            case Glyph.Comment:
            {
                // A speech bubble with a tail, and two lines of text.
                var hw = r * 1.05f;
                var hh = r * 0.72f;
                var y = cy - r * 0.12f;
                RoundedRect(b, cx - hw, y - hh, cx + hw, y + hh, r * 0.25f, c, t * 0.8f);
                b.DrawLine(cx - r * 0.35f, y + hh, cx - r * 0.6f, y + hh + r * 0.45f, c, t * 0.8f);
                b.DrawLine(cx - r * 0.6f, y + hh + r * 0.45f, cx - r * 0.05f, y + hh, c, t * 0.8f);
                b.DrawLine(cx - r * 0.55f, y - r * 0.22f, cx + r * 0.55f, y - r * 0.22f, c, t * 0.8f);
                b.DrawLine(cx - r * 0.55f, y + r * 0.22f, cx + r * 0.25f, y + r * 0.22f, c, t * 0.8f);
                break;
            }

            case Glyph.Prompt:
            {
                // A terminal window with a prompt: >_
                var hw = r * 1.15f;
                var hh = r * 0.85f;
                RoundedRect(b, cx - hw, cy - hh, cx + hw, cy + hh, r * 0.22f, c, t * 0.8f);
                b.DrawLine(cx - r * 0.65f, cy - r * 0.35f, cx - r * 0.2f, cy, c, t);
                b.DrawLine(cx - r * 0.2f, cy, cx - r * 0.65f, cy + r * 0.35f, c, t);
                b.DrawLine(cx + r * 0.02f, cy + r * 0.38f, cx + r * 0.62f, cy + r * 0.38f, c, t);
                break;
            }

            case Glyph.Smiley:
                Circle(b, cx, cy, r, c, t);
                Dot(b, cx - r * 0.38f, cy - r * 0.2f, t * 2f, c);
                Dot(b, cx + r * 0.38f, cy - r * 0.2f, t * 2f, c);
                b.DrawArc((int)cx, (int)cy, Math.Max(1, (int)(r * 0.5f)), 30, 120, c, t);
                break;
        }
    }

    public static BitmapColor Color(Rgb rgb) => new(rgb.R, rgb.G, rgb.B);

    private static void Circle(BitmapBuilder b, float cx, float cy, float r, BitmapColor c, float t) =>
        b.DrawArc((int)cx, (int)cy, Math.Max(1, (int)r), 0, 360, c, t);

    private static void Dot(BitmapBuilder b, float cx, float cy, float diameter, BitmapColor c) =>
        b.DrawArc((int)cx, (int)cy, Math.Max(1, (int)(diameter / 4)), 0, 360, c, diameter / 2);

    private static void RoundedRect(BitmapBuilder b, float x1, float y1, float x2, float y2, float rr, BitmapColor c, float t)
    {
        b.DrawLine(x1 + rr, y1, x2 - rr, y1, c, t);
        b.DrawLine(x1 + rr, y2, x2 - rr, y2, c, t);
        b.DrawLine(x1, y1 + rr, x1, y2 - rr, c, t);
        b.DrawLine(x2, y1 + rr, x2, y2 - rr, c, t);
        var ir = Math.Max(1, (int)rr);
        b.DrawArc((int)(x1 + rr), (int)(y1 + rr), ir, 180, 90, c, t);
        b.DrawArc((int)(x2 - rr), (int)(y1 + rr), ir, 270, 90, c, t);
        b.DrawArc((int)(x2 - rr), (int)(y2 - rr), ir, 0, 90, c, t);
        b.DrawArc((int)(x1 + rr), (int)(y2 - rr), ir, 90, 90, c, t);
    }
}
