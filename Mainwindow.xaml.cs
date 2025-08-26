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
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PdfStudio
{
    public partial class MainWindow : Window
    {
        // ---------- Fields ----------
        private readonly PdfRenderer _renderer;          // WinForms renderer hosted in WPF
        private PdfiumDocument? _pdfiumDoc;              // currently loaded Pdfium doc
        private byte[]? _pdfBytesCache;                  // original bytes (for Save As)
        private string? _currentPath;                    // original path (for Save As)

        // ---------- Ctor ----------
        public MainWindow()
        {
            InitializeComponent();

            _renderer = new PdfRenderer
            {
                Dock = WF.DockStyle.Fill,
                BackColor = System.Drawing.Color.White
            };
            WinFormsHost.Child = _renderer;              // <— required

            ZoomBox.SelectedIndex = 2; // "100%"
            EnableViewerUi(false);
        }

        // ---------- UI Handlers ----------
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
            var p = Math.Max(0, _renderer.Page - 1);      // renderer uses 0-based page index
            _renderer.Page = p;
            PageBox.Text = (p + 1).ToString();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfiumDoc == null) return;
            var p = Math.Min(_pdfiumDoc.PageCount - 1, _renderer.Page + 1);
            _renderer.Page = p;
            PageBox.Text = (p + 1).ToString();
        }

        private void PageBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter && _pdfiumDoc != null)
            {
                if (int.TryParse(PageBox.Text, out var oneBased))
                {
                    var zero = Math.Clamp(oneBased - 1, 0, _pdfiumDoc.PageCount - 1);
                    _renderer.Page = zero;
                    PageBox.Text = (zero + 1).ToString();
                }
            }
        }

        private void ZoomBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_pdfiumDoc == null || ZoomBox.SelectedItem == null) return;

            var text = (ZoomBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            if (text != null && text.EndsWith("%") && int.TryParse(text.TrimEnd('%'), out var percent))
            {
                // For builds where ZoomMode enums differ, simply set Zoom directly.
                _renderer.Zoom = percent / 100.0;
            }
        }

        // ---------- Loading / Disposing ----------
        private void LoadIntoViewer(string path)
        {
            DisposeCurrent();

            _currentPath = path;
            _pdfBytesCache = File.ReadAllBytes(path);

            _pdfiumDoc = PdfiumDocument.Load(new MemoryStream(_pdfBytesCache, writable: false));
            _renderer.Load(_pdfiumDoc);

            EnableViewerUi(true);
            PageCountText.Text = $"/ {_pdfiumDoc.PageCount}";
            _renderer.Page = 0;
            PageBox.Text = "1";

            // Try to fit width if your PdfiumViewer build exposes the enum.
            try { _renderer.ZoomMode = PdfViewerZoomMode.FitWidth; } catch { /* ignore if not available */ }
        }

        private void EnableViewerUi(bool on)
        {
            SaveAsButton.IsEnabled = on;
            PrevBtn.IsEnabled = on;
            NextBtn.IsEnabled = on;
            PageBox.IsEnabled = on;
            ZoomBox.IsEnabled = on;
        }

        private void DisposeCurrent()
        {
            // Clear renderer & dispose previous doc
            if (_renderer.Document != null)
            {
                _renderer.Document.Dispose();
                _renderer.Document = null;
            }
            _pdfiumDoc?.Dispose();
            _pdfiumDoc = null;

            _currentPath = null;
            _pdfBytesCache = null;

            EnableViewerUi(false);
            PageBox.Text = "";
            PageCountText.Text = "/ 0";
        }

        // ---------- Utility: create a simple PDF with PDFsharp ----------
        private static void CreateSamplePdf(string outputPath)
        {
            var doc = new PdfSharpDocument();
            doc.Info.Title = "New PDF from PDFsharp";

            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var font = new XFont("Arial", 20, XFontStyle.Bold);
                gfx.DrawString("Hello from PDFsharp + .NET 6", font, XBrushes.Black, new XPoint(60, 100));

                var small = new XFont("Arial", 12, XFontStyle.Regular);
                gfx.DrawString("You created this PDF and opened it here automatically.",
                               small, XBrushes.Black, new XPoint(60, 140));
            }

            doc.Save(outputPath);
            doc.Close();
        }

        // ---------- Cleanup ----------
        protected override void OnClosed(EventArgs e)
        {
            DisposeCurrent();
            base.OnClosed(e);
        }
    }
}
