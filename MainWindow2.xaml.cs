// ===== Aliases to avoid type/name collisions =====
using WinOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox    = System.Windows.MessageBox;
using WpfKeyEventArgs  = System.Windows.Input.KeyEventArgs;

using WF  = System.Windows.Forms;                    // WinForms
using WFI = System.Windows.Forms.Integration;       // WindowsFormsHost interop

using PdfiumDocument   = PdfiumViewer.PdfDocument;  // Pdfium doc
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;  // PDFsharp doc

// ===== Normal usings =====
using PdfiumViewer;
using PdfSharp.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Parsing (PdfPig)
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfStudio
{
    public partial class MainWindow : Window
    {
        // ---------- Viewer & state ----------
        private readonly PdfRenderer _renderer;          // WinForms renderer hosted in WPF
        private WFI.WindowsFormsHost? _viewerHost;       // host created dynamically
        private PdfiumDocument? _pdfiumDoc;              // currently loaded Pdfium doc
        private byte[]? _pdfBytesCache;                  // original bytes (for Save As)
        private string? _currentPath;                    // original path (for Save As)

        // ---------- Edit mode ----------
        private EditPageView? _editView;                 // code-only editor surface
        private ParsedDocument? _parsedDoc;              // parsed text/layout (v1: words only)

        // ---------- Ctor ----------
        public MainWindow()
        {
            InitializeComponent();

            _renderer = new PdfRenderer
            {
                Dock = WF.DockStyle.Fill,
                BackColor = System.Drawing.Color.White
            };

            // Default to VIEW mode: host the renderer inside CenterHost
            _viewerHost = new WFI.WindowsFormsHost { Background = Brushes.White, Child = _renderer };
            CenterHost.Content = _viewerHost;

            // optional: set default zoom box item if your XAML has one
            try { ZoomBox.SelectedIndex = 2; } catch { /* ignore if not present */ }

            EnableViewerUi(false);
        }

        // =========================================================
        // UI Handlers (Open / Create / SaveAs / Paging / Zoom)
        // =========================================================

        private void OpenPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WinOpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Open PDF" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    LoadIntoViewer(dlg.FileName);
                    StatusText.Text = $"Opened: {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(this, ex.ToString(), "Load error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CreateNewPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"PDFStudio_{Guid.NewGuid():N}.pdf");
                CreateSamplePdf(tempPath);
                LoadIntoViewer(tempPath);
                StatusText.Text = "Created new PDF and loaded it.";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, ex.Message, "Create PDF error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            // If we have parsed/edited content, export the edited PDF
            if (_parsedDoc != null)
            {
                var dlgOut = new WinSaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Export Edited PDF" };
                if (dlgOut.ShowDialog() == true)
                {
                    try
                    {
                        ExportEditedPdf(dlgOut.FileName, _parsedDoc);
                        StatusText.Text = $"Exported edited PDF: {dlgOut.FileName}";
                    }
                    catch (Exception ex)
                    {
                        WpfMessageBox.Show(this, ex.ToString(), "Export error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                return;
            }

            // Otherwise, simple "Save As" copy of the original
            if (_pdfiumDoc == null) return;

            var dlg = new WinSaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Save PDF As" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
                    {
                        File.Copy(_currentPath, dlg.FileName, overwrite: true);
                    }
                    else if (_pdfBytesCache != null)
                    {
                        File.WriteAllBytes(dlg.FileName, _pdfBytesCache);
                    }
                    else
                    {
                        WpfMessageBox.Show(this, "No original PDF bytes to save.", "Save As",
                                           MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    StatusText.Text = $"Saved: {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(this, ex.Message, "Save As error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfiumDoc == null) return;

            // If in edit view, persist edits back into the model before switching
            CaptureEditsIfEditing();

            var p = Math.Max(0, _renderer.Page - 1);      // renderer uses 0-based index
            _renderer.Page = p;
            PageBox.Text = (p + 1).ToString();

            // If we were editing, refresh the edit surface for new page
            RefreshEditSurfaceIfActive();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfiumDoc == null) return;

            CaptureEditsIfEditing();

            var p = Math.Min((_pdfiumDoc.PageCount - 1), _renderer.Page + 1);
            _renderer.Page = p;
            PageBox.Text = (p + 1).ToString();

            RefreshEditSurfaceIfActive();
        }

        private void PageBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter && _pdfiumDoc != null &&
                int.TryParse(PageBox.Text, out var oneBased))
            {
                CaptureEditsIfEditing();

                var zero = Math.Clamp(oneBased - 1, 0, _pdfiumDoc.PageCount - 1);
                _renderer.Page = zero;
                PageBox.Text = (zero + 1).ToString();

                RefreshEditSurfaceIfActive();
            }
        }

        private void ZoomBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_pdfiumDoc == null || ZoomBox.SelectedItem == null) return;

            var text = (ZoomBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (text != null && text.EndsWith("%") && int.TryParse(text.TrimEnd('%'), out var percent))
            {
                if (IsInEditMode)
                {
                    // editing surface auto-scales to bitmap; for v1, ignore or rebuild the edit view at new DPI
                    StatusText.Text = "Edit mode zoom not implemented; using page bitmap scale.";
                }
                else
                {
                    _renderer.Zoom = percent / 100.0; // do not set ZoomMode if your build lacks enums
                }
            }
        }

        // =========================================================
        // Load / Parse / Edit Mode Swap
        // =========================================================

        private void LoadIntoViewer(string path)
        {
            DisposeCurrent();

            _currentPath = path;
            _pdfBytesCache = File.ReadAllBytes(path);

            _pdfiumDoc = PdfiumDocument.Load(new MemoryStream(_pdfBytesCache, writable: false));
            _renderer.Load(_pdfiumDoc);

            EnableViewerUi(true);
            PageBox.Text = "1";
            PageCountText.Text = $"/ {_pdfiumDoc.PageCount}";
            _renderer.Page = 0;

            // Try fit width if available; otherwise rely on manual percent zoom
            try { _renderer.ZoomMode = PdfViewerZoomMode.FitWidth; } catch { /* some builds lack this */ }

            // Parse synchronously for v1 (text only)
            _parsedDoc = PdfExtractor.Parse(_currentPath);
        }

        private bool IsInEditMode => CenterHost.Content is EditPageView;

        // Call when switching page or toggling edit to capture edits
        private void CaptureEditsIfEditing()
        {
            if (_editView != null && _parsedDoc != null && IsInEditMode)
            {
                var edited = _editView.ApplyEdits();
                var pages = _parsedDoc.Pages.ToList();
                // Ensure index exists (guard if user navigates beyond parsed bounds)
                var pageIndex = Math.Clamp(_renderer.Page, 0, pages.Count - 1);
                pages[pageIndex] = edited;
                _parsedDoc = new ParsedDocument(pages);
            }
        }

        // If we are in edit mode, rebuild the edit surface for the current page
        private void RefreshEditSurfaceIfActive()
        {
            if (_pdfiumDoc == null || _parsedDoc == null) return;
            if (!IsInEditMode) return;

            // Render page bitmap
            var page = Math.Clamp(_renderer.Page, 0, _pdfiumDoc.PageCount - 1);
            var parsed = _parsedDoc.Pages[Math.Clamp(page, 0, _parsedDoc.Pages.Count - 1)];

            // Render at nice DPI
            const int targetDpi = 144;
            int pxW = (int)Math.Round(parsed.WidthPt * targetDpi / 72.0);
            int pxH = (int)Math.Round(parsed.HeightPt * targetDpi / 72.0);

            using var bmp = _pdfiumDoc.Render(page, pxW, pxH, targetDpi, targetDpi, true);
            var source = CreateBitmapSourceAndFree(bmp);

            _editView = new EditPageView();
            _editView.Load(parsed, source);
            CenterHost.Content = _editView;
        }

        // =========================================================
        // Public Edit Mode toggles (wire these to a ToggleButton if you like)
        // =========================================================

        // Example: call this to enter edit mode (or wire it to a ToggleButton.Checked)
        private void EnterEditMode()
        {
            if (_pdfiumDoc == null || _parsedDoc == null) return;
            RefreshEditSurfaceIfActive(); // will create _editView and set CenterHost.Content
        }

        // Example: call this to leave edit mode (or wire it to ToggleButton.Unchecked)
        private void LeaveEditMode()
        {
            CaptureEditsIfEditing();

            // Return to viewer
            _viewerHost = new WFI.WindowsFormsHost { Background = Brushes.White, Child = _renderer };
            CenterHost.Content = _viewerHost;
        }

        // =========================================================
        // Export of edited PDF (text only overlay for v1)
        // =========================================================

        private static void ExportEditedPdf(string path, ParsedDocument doc)
        {
            var outDoc = new PdfSharpDocument();
            foreach (var page in doc.Pages)
            {
                var p = outDoc.AddPage();
                p.Width = page.WidthPt;
                p.Height = page.HeightPt;

                using var gfx = XGraphics.FromPdfPage(p);
                // TODO: (optional) draw rendered original page as background image for WYSIWYG

                foreach (var t in page.Texts)
                {
                    var font = new XFont("Arial", Math.Max(1, t.FontSizePt), XFontStyle.Regular);
                    // PDF coordinate space: (0,0) bottom-left; XGraphics uses same baseline by default
                    gfx.DrawString(t.Text, font, XBrushes.Black, new XPoint(t.XPt, t.YPt));
                }
            }
            outDoc.Save(path);
            outDoc.Close();
        }

        // =========================================================
        // Utility / Cleanup
        // =========================================================

        private void EnableViewerUi(bool on)
        {
            try { SaveAsButton.IsEnabled = on; } catch { }
            try { PrevBtn.IsEnabled = on; } catch { }
            try { NextBtn.IsEnabled = on; } catch { }
            try { PageBox.IsEnabled = on; } catch { }
            try { ZoomBox.IsEnabled = on; } catch { }
        }

        private void DisposeCurrent()
        {
            // capture pending edits if we were in editor
            CaptureEditsIfEditing();

            try { _pdfiumDoc?.Dispose(); } catch { }
            _pdfiumDoc = null;

            _currentPath = null;
            _pdfBytesCache = null;

            _parsedDoc = null;
            _editView = null;

            // return to viewer host (blank)
            _viewerHost = new WFI.WindowsFormsHost { Background = Brushes.White, Child = _renderer };
            CenterHost.Content = _viewerHost;

            EnableViewerUi(false);
            try { PageBox.Text = ""; } catch { }
            try { PageCountText.Text = "/ 0"; } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeCurrent();
            base.OnClosed(e);
        }

        // Render helper: create BitmapSource from GDI+ Bitmap and free handle to avoid leaks
        private static BitmapSource CreateBitmapSourceAndFree(System.Drawing.Bitmap bmp)
        {
            IntPtr hBmp = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(hBmp);
                bmp.Dispose();
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }

    // =============================================================
    // Parsing models + extractor (words only for v1)
    // =============================================================

    public record ParsedDocument(List<ParsedPage> Pages);
    public record ParsedPage(int PageNumber, double WidthPt, double HeightPt, List<TextSpan> Texts);
    public record TextSpan(int Index, string Text, double XPt, double YPt, double WidthPt, double HeightPt, double FontSizePt, string? FontName);

    public static class PdfExtractor
    {
        public static ParsedDocument Parse(string path)
        {
            var pages = new List<ParsedPage>();
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);

            foreach (var page in pdf.GetPages())
            {
                double widthPt = page.Width;
                double heightPt = page.Height;

                var spans = new List<TextSpan>();
                int idx = 0;

                // Word-level extraction (good start). For finer control, iterate Letters.
                foreach (var w in page.GetWords())
                {
                    var bb = w.BoundingBox;
                    double x = bb.Left;
                    double y = bb.Bottom;
                    double wpt = bb.Width;
                    double hpt = bb.Height;
                    double approxFont = hpt; // heuristic

                    spans.Add(new TextSpan(idx++, w.Text, x, y, wpt, hpt, approxFont, null));
                }

                pages.Add(new ParsedPage(page.Number - 1, widthPt, heightPt, spans)); // 0-based index
            }

            return new ParsedDocument(pages);
        }
    }

    // =============================================================
    // Code-only editable overlay view (Image + Canvas + TextBoxes)
    // =============================================================

    public sealed class EditPageView : UserControl
    {
        private readonly Grid _root;
        private readonly Image _pageImage;
        private readonly Canvas _overlay;
        private readonly List<TextBox> _boxes = new();

        private double _imgScale = 1.0;      // pixels per PDF point
        private ParsedPage? _parsed;

        public EditPageView()
        {
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _root = new Grid { Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)) };
            _pageImage = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            _overlay = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = true };

            _root.Children.Add(_pageImage);
            _root.Children.Add(_overlay);
            scroll.Content = _root;

            Content = scroll;
            SizeChanged += (_, __) => Relayout();
        }

        public void Load(ParsedPage parsed, BitmapSource pageBitmap)
        {
            _parsed = parsed;
            _pageImage.Source = pageBitmap;

            // compute scale factor: PDF points -> pixels
            _imgScale = pageBitmap.PixelWidth / parsed.WidthPt;

            // lock the grid to exact pixel size for 1:1 mapping
            _root.Width = pageBitmap.PixelWidth;
            _root.Height = pageBitmap.PixelHeight;

            _overlay.Children.Clear();
            _boxes.Clear();

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

        public ParsedPage ApplyEdits()
        {
            if (_parsed == null) throw new InvalidOperationException("No page loaded.");

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

            // PDF origin is bottom-left; WPF Canvas origin is top-left.
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

            // If parent layout changes, recompute placement (scale unchanged unless bitmap changes)
            for (int i = 0; i < _boxes.Count; i++)
                PositionBox(_boxes[i], _parsed.Texts[i]);
        }
    }

    // =============================================================
    // Simple PDF creation for "Create New PDF"
    // =============================================================

    public partial class MainWindow
    {
        private static void CreateSamplePdf(string outputPath)
        {
            var doc = new PdfSharpDocument();
            doc.Info.Title = "New PDF from PDFsharp";

            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var title = new XFont("Arial", 20, XFontStyle.Bold);
                var body  = new XFont("Arial", 12, XFontStyle.Regular);

                gfx.DrawString("Hello from PDFsharp + .NET 6", title, XBrushes.Black, new XPoint(60, 100));
                gfx.DrawString("You created this PDF and opened it here automatically.", body, XBrushes.Black, new XPoint(60, 140));
            }

            doc.Save(outputPath);
            doc.Close();
        }
    }
}
