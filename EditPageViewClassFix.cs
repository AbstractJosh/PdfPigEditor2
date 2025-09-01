using System;
using System.Collections.Generic;
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
    public sealed class EditPageView : WinUserControl
    {
        private readonly WinGrid _root;
        private readonly WinImage _pageImage;
        private readonly WinCanvas _overlay;
        private readonly List<WinTextBox> _boxes = new();

        private double _imgScale = 1.0;               // pixels per PDF point
        private ParsedPage? _parsed;                  // parsed page data
        private WinBitmapSource? _background;         // cached PDF render

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

            _root.Children.Add(_pageImage);
            _root.Children.Add(_overlay);

            scroll.Content = _root;
            Content = scroll;

            SizeChanged += (_, __) => Relayout();
        }

        /// <summary>
        /// Load a parsed page and its background bitmap into the editor.
        /// </summary>
        public void Load(ParsedPage parsed, WinBitmapSource pageBitmap)
        {
            _parsed = parsed;
            _background = pageBitmap;

            _pageImage.Source = _background;

            // Start with background hidden in edit mode
            _pageImage.Visibility = Visibility.Collapsed;

            _root.Width = pageBitmap.PixelWidth;
            _root.Height = pageBitmap.PixelHeight;

            _overlay.Children.Clear();
            _boxes.Clear();

            // compute scale factor: PDF points -> pixels
            _imgScale = pageBitmap.PixelWidth / parsed.WidthPt;

            foreach (var t in parsed.Texts)
            {
                var tb = new WinTextBox
                {
                    Text = t.Text,
                    FontSize = Math.Max(8.0, t.FontSizePt * _imgScale * 0.9),
                    Background = new WinSolidColorBrush(WinColor.FromArgb(40, 255, 255, 0)),
                    BorderBrush = WinBrushes.Goldenrod,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(2),
                    MinWidth = 12
                };

                _overlay.Children.Add(tb);
                _boxes.Add(tb);
                PositionBox(tb, t);
            }
        }

        /// <summary>
        /// Toggle the PDF background image on/off.
        /// </summary>
        public void ToggleBackground(bool show)
        {
            if (_background == null) return;
            _pageImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Collect current edits and return an updated ParsedPage.
        /// </summary>
        public ParsedPage ApplyEdits()
        {
            if (_parsed == null)
                throw new InvalidOperationException("No page loaded.");

            var edited = new List<TextSpan>(_parsed.Texts.Count);
            for (int i = 0; i < _parsed.Texts.Count; i++)
            {
                var orig = _parsed.Texts[i];
                var tb = _boxes[i];
                edited.Add(orig with { Text = tb.Text });
            }

            return new ParsedPage(_parsed.PageNumber, _parsed.WidthPt, _parsed.HeightPt, edited);
        }

        private void PositionBox(WinTextBox tb, TextSpan span)
        {
            if (_parsed == null) return;

            // PDF origin is bottom-left; WPF Canvas origin is top-left
            double xPx = span.XPt * _imgScale;
            double yPxBottom = span.YPt * _imgScale;
            double yPxTop = _root.Height - (yPxBottom + span.HeightPt * _imgScale);

            WinCanvas.SetLeft(tb, xPx);
            WinCanvas.SetTop(tb, yPxTop);

            tb.Width = Math.Max(8, span.WidthPt * _imgScale + 4);
        }

        private void Relayout()
        {
            if (_parsed == null || _pageImage.Source == null) return;

            for (int i = 0; i < _boxes.Count; i++)
                PositionBox(_boxes[i], _parsed.Texts[i]);
        }
    }
}
