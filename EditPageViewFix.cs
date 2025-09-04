using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WinTextBox = System.Windows.Controls.TextBox;
using WinImage = System.Windows.Controls.Image;
using WinGrid = System.Windows.Controls.Grid;
using WinCanvas = System.Windows.Controls.Canvas;
using WinScrollViewer = System.Windows.Controls.ScrollViewer;
using WinUserControl = System.Windows.Controls.UserControl;
using WinBrushes = System.Windows.Media.Brushes;
using WinSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WinColor = System.Windows.Media.Color;
using WinBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using WinStretch = System.Windows.Media.Stretch;
using WinThickness = System.Windows.Thickness;

namespace PdfStudio
{
    /// <summary>
    /// Editable overlay: background page bitmap + paragraph-level editors.
    /// </summary>
    public sealed class EditPageView : WinUserControl
    {
        private readonly WinGrid _root;
        private readonly WinImage _pageImage;
        private readonly WinCanvas _overlay;

        private readonly List<WinTextBox> _paraEditors = new();

        private double _imgScale = 1.0;          // pixels per PDF point
        private ParsedPage? _parsed;
        private WinBitmapSource? _background;

        // keep paragraph geometry for export if you want later
        private readonly List<ParagraphGeom> _paraGeoms = new();

        private sealed record ParagraphGeom(double LeftPt, double TopPt, double WidthPt, double HeightPt, double BaseFontPt);

        public EditPageView()
        {
            var scroll = new WinScrollViewer
            {
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = System.Windows.Controls.ScrollBarVisibility.Auto
            };

            _root = new WinGrid
            {
                // white paper look by default; we’ll still show the PDF bitmap if available
                Background = WinBrushes.White
            };

            _pageImage = new WinImage
            {
                Stretch = WinStretch.Uniform,
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _overlay = new WinCanvas
            {
                Background = WinBrushes.Transparent,
                IsHitTestVisible = true
            };

            _root.Children.Add(_pageImage);  // background
            _root.Children.Add(_overlay);    // editors

            scroll.Content = _root;
            Content = scroll;

            SizeChanged += (_, __) => Relayout();
        }

        /// <summary>
        /// Load a parsed page and its rendered bitmap. Background shown by default.
        /// </summary>
        public void Load(ParsedPage parsed, WinBitmapSource pageBitmap)
        {
            _parsed = parsed;
            _background = pageBitmap;

            _pageImage.Source = _background;
            _pageImage.Visibility = Visibility.Visible;        // show PDF page by default
            _root.Background = WinBrushes.White;                // if hidden, we keep white “paper”

            _root.Width  = pageBitmap.PixelWidth;
            _root.Height = pageBitmap.PixelHeight;

            _overlay.Children.Clear();
            _paraEditors.Clear();
            _paraGeoms.Clear();

            _imgScale = pageBitmap.PixelWidth / parsed.WidthPt;

            // ---- 1) words -> lines (baseline clustering) ----
            var lines = ClusterLines(parsed.Texts);

            // ---- 2) lines -> paragraphs (indent / vertical gap heuristic) ----
            var paragraphs = ClusterParagraphs(lines);

            // ---- 3) create one multiline TextBox per paragraph ----
            const double MARGIN_PT = 6.0; // tiny inner margin
            foreach (var para in paragraphs)
            {
                // Build paragraph text preserving big gaps as multiple spaces
                // and line breaks between logical lines in the paragraph.
                var paraText = BuildParagraphText(para);

                // Paragraph rectangle (points)
                var leftPt   = para.Min(w => w.XPt) - 0.5;
                var rightPt  = para.Max(w => w.XPt + w.WidthPt) + 0.5;
                var topPt    = para.Max(w => w.YPt + w.HeightPt); // highest top
                var botPt    = para.Min(w => w.YPt);              // lowest baseline
                var widthPt  = Math.Max(8, rightPt - leftPt);
                var heightPt = Math.Max(8, topPt - botPt);

                // Convert to canvas pixels/top-left
                var xPx = leftPt * _imgScale;
                var yPxTop = _root.Height - (topPt * _imgScale);

                // font size: median letter/word height in the paragraph
                var baseFontPt = Median(para.Select(w => w.FontSizePt > 1 ? w.FontSizePt : w.HeightPt));
                var editor = new WinTextBox
                {
                    Text = paraText,
                    Background = WinBrushes.Transparent,
                    BorderThickness = new WinThickness(0),
                    Padding = new WinThickness(0),
                    AcceptsReturn = true,
                    TextWrapping = System.Windows.TextWrapping.Wrap, // span across line ends
                    FontSize = Math.Max(8.0, baseFontPt * _imgScale * 0.95),
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility   = System.Windows.Controls.ScrollBarVisibility.Disabled
                };

                // focus styling (subtle)
                editor.GotFocus += (_, __) =>
                {
                    editor.BorderBrush = WinBrushes.DeepSkyBlue;
                    editor.BorderThickness = new WinThickness(1);
                };
                editor.LostFocus += (_, __) =>
                {
                    editor.BorderThickness = new WinThickness(0);
                    editor.BorderBrush = WinBrushes.Transparent;
                };

                _overlay.Children.Add(editor);
                _paraEditors.Add(editor);
                _paraGeoms.Add(new ParagraphGeom(leftPt + MARGIN_PT, topPt - MARGIN_PT, Math.Max(8, widthPt - 2*MARGIN_PT), heightPt - 2*MARGIN_PT, baseFontPt));

                WinCanvas.SetLeft(editor, xPx);
                WinCanvas.SetTop (editor, yPxTop);
                editor.Width  = Math.Max(8, widthPt * _imgScale);
                editor.Height = Math.Max(8, heightPt * _imgScale);
            }
        }

        /// <summary>Show or hide the PDF background. When hidden, page stays white.</summary>
        public void ToggleBackground(bool show)
        {
            _pageImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _root.Background = WinBrushes.White; // ensure not gray when hidden
        }

        /// <summary>
        /// Collect edits. Each paragraph becomes a single TextSpan placed at its top-left.
        /// </summary>
        public ParsedPage ApplyEdits()
        {
            if (_parsed == null) throw new InvalidOperationException("No page loaded.");

            var edited = new List<TextSpan>(_paraEditors.Count);
            for (int i = 0; i < _paraEditors.Count; i++)
            {
                var tb = _paraEditors[i];
                var g  = _paraGeoms[i];

                edited.Add(new TextSpan(
                    Index: i,
                    Text: tb.Text,
                    XPt: g.LeftPt,
                    YPt: g.TopPt - g.BaseFontPt,     // baseline approx from top
                    WidthPt: g.WidthPt,
                    HeightPt: g.HeightPt,
                    FontSizePt: g.BaseFontPt,
                    FontName: null
                ));
            }

            return new ParsedPage(_parsed.PageNumber, _parsed.WidthPt, _parsed.HeightPt, edited);
        }

        private void Relayout() { /* page is fixed-size; no-op for now */ }

        // ---------------- internals: clustering & text building ----------------

        private static List<List<TextSpan>> ClusterLines(List<TextSpan> words)
        {
            // Sort by Y descending (visual top to bottom)
            var ordered = words.OrderByDescending(w => w.YPt).ToList();
            var lines = new List<List<TextSpan>>();
            const double BASE_TOL = 3.0;

            foreach (var w in ordered)
            {
                bool placed = false;
                foreach (var line in lines)
                {
                    var refY = line[0].YPt;
                    if (Math.Abs(w.YPt - refY) <= Math.Max(BASE_TOL, 0.35 * w.HeightPt))
                    {
                        line.Add(w);
                        placed = true;
                        break;
                    }
                }
                if (!placed) lines.Add(new List<TextSpan> { w });
            }
            foreach (var line in lines) line.Sort((a, b) => a.XPt.CompareTo(b.XPt));
            return lines;
        }

        private static List<List<TextSpan>> ClusterParagraphs(List<List<TextSpan>> lines)
        {
            // Paragraph if left indents are close and vertical gap is small
            var paras = new List<List<TextSpan>>();
            if (lines.Count == 0) return paras;

            var current = new List<TextSpan>(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                var prev = lines[i - 1];
                var cur  = lines[i];

                double prevLeft = prev.Min(w => w.XPt);
                double curLeft  = cur .Min(w => w.XPt);
                double leftDiff = Math.Abs(prevLeft - curLeft);

                double prevBottom = prev.Min(w => w.YPt);
                double curTop     = cur .Max(w => w.YPt + w.HeightPt);
                double vertGap    = prevBottom - curTop; // positive if close

                bool sameIndent = leftDiff < 10.0;
                bool smallGap   = vertGap < 18.0; // tweak as needed

                if (sameIndent && smallGap)
                {
                    current.AddRange(cur);
                }
                else
                {
                    paras.Add(current);
                    current = new List<TextSpan>(cur);
                }
            }
            paras.Add(current);
            return paras;
        }

        private string BuildParagraphText(List<TextSpan> lineWords)
        {
            // We were given a *paragraph* as a flat list of words still grouped by lines.
            // Re-group by baseline to reconstruct line breaks inside the paragraph.
            var lines = ClusterLines(lineWords);

            var parts = new List<string>();
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                if (line.Count == 0) continue;

                // Estimate average char width for spacing heuristic
                var avgFontPt = Median(line.Select(w => w.FontSizePt > 1 ? w.FontSizePt : w.HeightPt));
                var approxChar = Math.Max(0.5, avgFontPt * 0.5); // crude

                for (int i = 0; i < line.Count; i++)
                {
                    if (i == 0) { parts.Add(line[i].Text); continue; }

                    var prev = line[i - 1];
                    var cur  = line[i];
                    var gap  = cur.XPt - (prev.XPt + prev.WidthPt);

                    // turn significant gaps into multiple spaces
                    int spaces = gap <= 0 ? 0 : (int)Math.Round(gap / (approxChar * 0.6));
                    spaces = Math.Clamp(spaces, 1, 10);

                    parts.Add(new string(' ', spaces));
                    parts.Add(cur.Text);
                }

                if (li < lines.Count - 1)
                    parts.Add(Environment.NewLine);
            }
            return string.Concat(parts);
        }

        private static double Median(IEnumerable<double> data)
        {
            var arr = data.Where(d => d > 0).OrderBy(d => d).ToArray();
            if (arr.Length == 0) return 10;
            int mid = arr.Length / 2;
            return (arr.Length % 2 == 1) ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
        }
    }
}
