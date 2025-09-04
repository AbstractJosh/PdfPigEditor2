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

namespace PdfStudio
{
    /// <summary>
    /// Editable overlay: background page bitmap + line-level text boxes.
    /// </summary>
    public sealed class EditPageView : WinUserControl
    {
        private readonly WinGrid _root;
        private readonly WinImage _pageImage;
        private readonly WinCanvas _overlay;

        // one editor per "line"
        private readonly List<WinTextBox> _lineEditors = new();

        private double _imgScale = 1.0;          // pixels per PDF point
        private ParsedPage? _parsed;
        private WinBitmapSource? _background;

        // Holds which original word spans belong to each line (for advanced mapping later)
        private readonly List<List<TextSpan>> _lineWordSpans = new();

        public EditPageView()
        {
            var scroll = new WinScrollViewer
            {
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
            };

            _root = new WinGrid
            {
                Background = new WinSolidColorBrush(WinColor.FromRgb(43, 43, 43))
            };

            _pageImage = new WinImage
            {
                Stretch = WinStretch.Uniform,
                SnapsToDevicePixels = true
            };

            _overlay = new WinCanvas
            {
                Background = WinBrushes.Transparent,
                IsHitTestVisible = true
            };

            _root.Children.Add(_pageImage);  // background first
            _root.Children.Add(_overlay);    // overlay on top

            scroll.Content = _root;
            Content = scroll;

            SizeChanged += (_, __) => Relayout();
        }

        /// <summary>
        /// Load a parsed page and its rendered bitmap.
        /// Background is *visible* by default for better UX.
        /// </summary>
        public void Load(ParsedPage parsed, WinBitmapSource pageBitmap)
        {
            _parsed = parsed;
            _background = pageBitmap;

            _pageImage.Source = _background;
            _pageImage.Visibility = Visibility.Visible; // <-- show background now

            _root.Width  = pageBitmap.PixelWidth;
            _root.Height = pageBitmap.PixelHeight;

            _overlay.Children.Clear();
            _lineEditors.Clear();
            _lineWordSpans.Clear();

            _imgScale = pageBitmap.PixelWidth / parsed.WidthPt;

            // ---- group words into lines (baseline clustering) ----
            // Sort by Y (top to bottom); Pdf coordinates are bottom-left so convert to "top" proxy
            var words = parsed.Texts.OrderByDescending(w => w.YPt).ToList();

            var lines = new List<List<TextSpan>>();
            const double BASELINE_TOLERANCE_PT = 3.0; // heuristic; tweak if needed

            foreach (var w in words)
            {
                // try to place 'w' into an existing line with close baseline
                bool placed = false;
                foreach (var line in lines)
                {
                    // Compare to first word baseline in the line
                    var refY = line[0].YPt;
                    if (Math.Abs(w.YPt - refY) <= Math.Max(BASELINE_TOLERANCE_PT, 0.35 * w.HeightPt))
                    {
                        line.Add(w);
                        placed = true;
                        break;
                    }
                }
                if (!placed) lines.Add(new List<TextSpan> { w });
            }

            // Normalize: sort words in each line by X ascending
            foreach (var line in lines) line.Sort((a, b) => a.XPt.CompareTo(b.XPt));

            // ---- create one textbox per line ----
            foreach (var line in lines)
            {
                if (line.Count == 0) continue;

                // Build line text with simple spacing heuristic
                var pieces = new List<string>(line.Count);
                for (int i = 0; i < line.Count; i++)
                {
                    if (i == 0) { pieces.Add(line[i].Text); continue; }

                    var prev = line[i - 1];
                    var cur  = line[i];
                    var gap  = cur.XPt - (prev.XPt + prev.WidthPt);

                    // Insert a space when a visible horizontal gap exists
                    pieces.Add((gap > Math.Max(1.0, 0.15 * prev.WidthPt)) ? (" " + cur.Text) : cur.Text);
                }
                var lineText = string.Concat(pieces);

                // Bounding rect (in PDF points)
                var leftPt = line.First().XPt;
                var rightPt = line.Last().XPt + line.Last().WidthPt;
                var widthPt = Math.Max(4, rightPt - leftPt);
                var heightPt = line.Max(w => w.HeightPt);
                var baselinePt = line.First().YPt;

                // Convert to pixels & canvas coords
                var xPx = leftPt * _imgScale;
                var yPxBottom = baselinePt * _imgScale;
                var yPxTop = _root.Height - (yPxBottom + heightPt * _imgScale);

                var tb = new WinTextBox
                {
                    Text = lineText,

                    // transparent look; subtle focus border only
                    Background = WinBrushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),

                    // font-size heuristic (scale PDF size to pixels)
                    FontSize = Math.Max(8.0, heightPt * _imgScale * 0.9),
                    MinWidth = 12
                };

                // focus styling
                tb.GotFocus += (_, __) =>
                {
                    tb.BorderBrush = WinBrushes.DeepSkyBlue;
                    tb.BorderThickness = new Thickness(1);
                };
                tb.LostFocus += (_, __) =>
                {
                    tb.BorderThickness = new Thickness(0);
                    tb.BorderBrush = WinBrushes.Transparent;
                };

                _overlay.Children.Add(tb);
                _lineEditors.Add(tb);
                _lineWordSpans.Add(line);

                WinCanvas.SetLeft(tb, xPx);
                WinCanvas.SetTop(tb,  yPxTop);
                tb.Width = Math.Max(8, widthPt * _imgScale); // one long box per line
            }
        }

        /// <summary>
        /// Toggle the PDF background image on/off (kept cached either way).
        /// </summary>
        public void ToggleBackground(bool show)
        {
            if (_background == null) return;
            _pageImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Collect edits and return a simplified page made of line-level TextSpans.
        /// (Each line becomes one span positioned at the line's left/baseline.)
        /// </summary>
        public ParsedPage ApplyEdits()
        {
            if (_parsed == null)
                throw new InvalidOperationException("No page loaded.");

            var edited = new List<TextSpan>(_lineEditors.Count);

            for (int i = 0; i < _lineEditors.Count; i++)
            {
                var tb = _lineEditors[i];
                var words = _lineWordSpans[i];
                if (words.Count == 0) continue;

                var leftPt = words.First().XPt;
                var baselinePt = words.First().YPt;
                var widthPt = (words.Last().XPt + words.Last().WidthPt) - leftPt;
                var heightPt = words.Max(w => w.HeightPt);
                var avgSize = Math.Max(1.0, words.Average(w => w.FontSizePt));

                edited.Add(new TextSpan(
                    Index: i,
                    Text: tb.Text,
                    XPt: leftPt,
                    YPt: baselinePt,
                    WidthPt: widthPt,
                    HeightPt: heightPt,
                    FontSizePt: avgSize,
                    FontName: words.First().FontName
                ));
            }

            return new ParsedPage(_parsed.PageNumber, _parsed.WidthPt, _parsed.HeightPt, edited);
        }

        private void Relayout()
        {
            // We fix _root size to the bitmap; nothing to recompute on resize in this pass.
            // If you later add zooming inside the editor, recompute positions/sizes here.
        }
    }
}
