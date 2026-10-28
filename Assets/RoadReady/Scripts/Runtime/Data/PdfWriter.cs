using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RoadReady.Data
{
    /// <summary>
    /// Just enough PDF for a results report: A4 pages, wrapped Helvetica text, coloured bars and boxes, the three
    /// Rwanda-flag colours along the top. No external library; text is limited to plain Latin characters.
    /// </summary>
    public class PdfWriter
    {
        public const float PageWidth = 595f;
        public const float PageHeight = 842f;
        public const float Margin = 48f;

        static readonly Color k_Blue = new Color(0f, 0.63f, 0.87f);
        static readonly Color k_Yellow = new Color(0.98f, 0.82f, 0f);
        static readonly Color k_Green = new Color(0.125f, 0.376f, 0.24f);

        readonly List<StringBuilder> m_Pages = new List<StringBuilder>();
        StringBuilder m_Page;
        float m_Y;

        public PdfWriter() => NewPage();

        public float ContentWidth => PageWidth - 2f * Margin;

        void NewPage()
        {
            m_Page = new StringBuilder();
            m_Pages.Add(m_Page);
            // The flag colours as a band across the top of every page.
            Box(0f, PageHeight - 14f, PageWidth * 0.5f, 14f, k_Blue);
            Box(PageWidth * 0.5f, PageHeight - 14f, PageWidth * 0.25f, 14f, k_Yellow);
            Box(PageWidth * 0.75f, PageHeight - 14f, PageWidth * 0.25f, 14f, k_Green);
            m_Y = PageHeight - Margin - 8f;
        }

        void EnsureSpace(float height)
        {
            if (m_Y - height < Margin + 18f)
                NewPage();
        }

        public void Space(float height) => m_Y -= height;

        /// <summary>Keeps a heading together with what follows it.</summary>
        public void KeepTogether(float height) => EnsureSpace(height);

        public void Heading(string text, float size = 15f, Color? color = null)
        {
            EnsureSpace(size * 3f);
            Space(size * 0.6f);
            Text(text, size, true, color ?? k_Green);
        }

        public void Text(string text, float size = 10.5f, bool bold = false, Color? color = null, float indent = 0f)
        {
            var tint = color ?? new Color(0.1f, 0.12f, 0.15f);
            var charWidth = size * (bold ? 0.56f : 0.51f);
            var maxChars = Mathf.Max(8, Mathf.FloorToInt((ContentWidth - indent) / charWidth));
            foreach (var paragraph in Clean(text).Split('\n'))
            {
                foreach (var line in Wrap(paragraph, maxChars))
                {
                    EnsureSpace(size * 1.45f);
                    m_Y -= size * 1.45f;
                    m_Page.Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(F(size)).Append(" Tf ")
                        .Append(F(tint.r)).Append(' ').Append(F(tint.g)).Append(' ').Append(F(tint.b)).Append(" rg ")
                        .Append(F(Margin + indent)).Append(' ').Append(F(m_Y)).Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
                }
            }
        }

        /// <summary>A labelled bar (0-1) with the value printed at its end.</summary>
        public void Bar(string label, string value, float fill01, Color color)
        {
            EnsureSpace(30f);
            Text(label + "   " + value, 10.5f, false);
            const float height = 9f;
            EnsureSpace(height + 6f);
            m_Y -= height + 4f;
            Box(Margin, m_Y, ContentWidth, height, new Color(0.9f, 0.92f, 0.94f));
            Box(Margin, m_Y, ContentWidth * Mathf.Clamp01(fill01), height, color);
            m_Y -= 4f;
        }

        public void Rule()
        {
            EnsureSpace(12f);
            m_Y -= 6f;
            Box(Margin, m_Y, ContentWidth, 0.8f, new Color(0.8f, 0.83f, 0.86f));
            m_Y -= 6f;
        }

        public static Color Blue => k_Blue;
        public static Color Yellow => k_Yellow;
        public static Color Green => k_Green;
        public static Color Red => new Color(0.82f, 0.2f, 0.22f);
        public static Color Amber => new Color(0.85f, 0.55f, 0.1f);
        public static Color Grey => new Color(0.4f, 0.44f, 0.5f);

        void Box(float x, float y, float w, float h, Color c) =>
            m_Page.Append(F(c.r)).Append(' ').Append(F(c.g)).Append(' ').Append(F(c.b)).Append(" rg ")
                .Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re f\n");

        // ------------------------------------------------------------------ output

        public byte[] Build(string title)
        {
            // Footer on every page, now that the page count is known.
            for (var i = 0; i < m_Pages.Count; i++)
            {
                m_Pages[i].Append("BT /F1 8.5 Tf 0.45 0.5 0.55 rg ").Append(F(Margin)).Append(" 26 Td (")
                    .Append(Escape(Clean(title) + "   |   page " + (i + 1) + " of " + m_Pages.Count)).Append(") Tj ET\n");
            }

            // Objects: 1 catalog, 2 pages, 3 regular font, 4 bold font, then (page, content) pairs.
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                null, // pages, filled below
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            };
            var kids = new StringBuilder();
            for (var i = 0; i < m_Pages.Count; i++)
            {
                var pageId = 5 + i * 2;
                kids.Append(pageId).Append(" 0 R ");
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageId + 1} 0 R >>");
                var stream = m_Pages[i].ToString();
                objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
            }

            objects[1] = $"<< /Type /Pages /Kids [{kids}] /Count {m_Pages.Count} >>";

            using (var output = new MemoryStream())
            {
                void Write(string s)
                {
                    var bytes = new byte[s.Length]; for (var k = 0; k < s.Length; k++) bytes[k] = (byte)s[k];
                    output.Write(bytes, 0, bytes.Length);
                }

                Write("%PDF-1.4\n");
                var offsets = new List<long>();
                for (var i = 0; i < objects.Count; i++)
                {
                    offsets.Add(output.Position);
                    Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
                }

                var xref = output.Position;
                Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
                foreach (var offset in offsets)
                    Write(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                return output.ToArray();
            }
        }

        // ------------------------------------------------------------------ text helpers

        static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        /// <summary>Plain Latin text only: typographic dashes and quotes become ASCII, anything else a question mark.</summary>
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '–': case '—': sb.Append('-'); break;
                    case '‘': case '’': sb.Append('\''); break;
                    case '“': case '”': sb.Append('"'); break;
                    case '…': sb.Append("..."); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    default: sb.Append(ch == '\n' || (ch >= 32 && ch < 127) || (ch >= 160 && ch <= 255) ? ch : '?'); break;
                }
            }

            return sb.ToString();
        }

        static IEnumerable<string> Wrap(string paragraph, int maxChars)
        {
            if (paragraph.Length == 0)
            {
                yield return "";
                yield break;
            }

            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var piece = word;
                while (piece.Length > maxChars)
                {
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    yield return piece.Substring(0, maxChars);
                    piece = piece.Substring(maxChars);
                }

                if (line.Length > 0 && line.Length + 1 + piece.Length > maxChars)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');
                line.Append(piece);
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }
}
