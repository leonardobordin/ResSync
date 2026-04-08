using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Collections.Generic;

class IconGen
{
    static void Main()
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();

        foreach (int sz in sizes)
        {
            using var bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float r = sz * 0.18f; // corner radius

            // Background: dark gradient
            using (var path = RoundedRect(0, 0, sz, sz, r))
            {
                using var bgBrush = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(sz, sz),
                    Color.FromArgb(255, 18, 18, 32),
                    Color.FromArgb(255, 10, 10, 22));
                g.FillPath(bgBrush, path);
            }

            // Subtle border
            using (var path = RoundedRect(0.5f, 0.5f, sz - 1, sz - 1, r))
            {
                using var pen = new Pen(Color.FromArgb(60, 0, 212, 255), sz > 32 ? 1.5f : 1f);
                g.DrawPath(pen, path);
            }

            // Cyan accent bar on the left
            float barW = sz * 0.06f;
            float barH = sz * 0.45f;
            float barX = sz * 0.14f;
            float barY = (sz - barH) / 2f;
            using (var barPath = RoundedRect(barX, barY, barW, barH, barW / 2f))
            {
                using var barBrush = new LinearGradientBrush(
                    new PointF(barX, barY), new PointF(barX, barY + barH),
                    Color.FromArgb(255, 0, 212, 255),
                    Color.FromArgb(255, 0, 140, 220));
                g.FillPath(barBrush, barPath);
            }

            // "R" letter — bold, modern
            float fontSize = sz * 0.52f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            string text = "R";
            var textSize = g.MeasureString(text, font);
            float tx = sz * 0.30f + (sz * 0.56f - textSize.Width) / 2f;
            float ty = (sz - textSize.Height) / 2f;

            // Text glow
            using (var glowBrush = new SolidBrush(Color.FromArgb(40, 0, 212, 255)))
            {
                for (float dx = -1.5f; dx <= 1.5f; dx += 0.75f)
                    for (float dy = -1.5f; dy <= 1.5f; dy += 0.75f)
                        g.DrawString(text, font, glowBrush, tx + dx, ty + dy);
            }

            // Main text
            using var textBrush = new LinearGradientBrush(
                new PointF(tx, ty), new PointF(tx, ty + textSize.Height),
                Color.FromArgb(255, 230, 230, 245),
                Color.FromArgb(255, 180, 180, 210));
            g.DrawString(text, font, textBrush, tx, ty);

            // Small cyan dot accent (bottom-right)
            float dotR = sz * 0.05f;
            float dotX = sz * 0.78f;
            float dotY = sz * 0.72f;
            using var dotBrush = new SolidBrush(Color.FromArgb(255, 0, 212, 255));
            g.FillEllipse(dotBrush, dotX - dotR, dotY - dotR, dotR * 2, dotR * 2);

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        // Write ICO file
        using var ico = new FileStream("app.ico", FileMode.Create);
        using var bw = new BinaryWriter(ico);

        // ICO header
        bw.Write((short)0);           // reserved
        bw.Write((short)1);           // type = icon
        bw.Write((short)sizes.Length); // count

        int dataOffset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte w = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
            byte h = w;
            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0);  // color palette
            bw.Write((byte)0);  // reserved
            bw.Write((short)1); // color planes
            bw.Write((short)32); // bits per pixel
            bw.Write(pngs[i].Length);
            bw.Write(dataOffset);
            dataOffset += pngs[i].Length;
        }

        foreach (var png in pngs)
            bw.Write(png);

        Console.WriteLine($"Icon written: {sizes.Length} sizes, {ico.Length} bytes");
    }

    static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var gp = new GraphicsPath();
        if (r <= 0) { gp.AddRectangle(new RectangleF(x, y, w, h)); return gp; }
        float d = r * 2;
        gp.AddArc(x, y, d, d, 180, 90);
        gp.AddArc(x + w - d, y, d, d, 270, 90);
        gp.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        gp.AddArc(x, y + h - d, d, d, 90, 90);
        gp.CloseFigure();
        return gp;
    }
}
