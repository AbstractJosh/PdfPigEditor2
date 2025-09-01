using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfStudio
{
    public sealed class EditPageView : UserControl
    {
        private readonly Grid _root;
        private readonly Image _pageImage;
        private readonly Canvas _overlay;
        private readonly List<TextBox> _boxes = new();

        private double _imgScale = 1.0;          // pixels per PDF point
        private ParsedPage? _parsed;             // parsed page data
        private BitmapSource? _background;       // cached PDF render

        public EditPageView()
        {
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _root = new Grid { Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)) };

            _pageImage = new Image
            {
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };

            _overlay = new Canvas
            {
                Background = Brushes.Transparent,
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
        public void Load(ParsedPage parsed, BitmapSource pageBitmap)
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
                var tb = new TextBox
                {
                    Text = t.Text,
                    FontSize = Math.Max(8.0, t.FontSizePt * _imgScale * 0.9),
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
                    BorderBrush = Brushes.Goldenrod,
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

        private void PositionBox(TextBox tb, TextSpan span)
        {
            if (_parsed == null) return;

            // PDF origin is bottom-left; WPF Canvas origin is top-left
            double xPx = span.XPt * _imgScale;
            double yPxBottom = span.YPt * _imgScale;
            double yPxTop = _root.Height - (yPxBottom + span.HeightPt * _imgScale);

            Canvas.SetLeft(tb, xPx);
            Canvas.SetTop(tb, yPxTop);

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
