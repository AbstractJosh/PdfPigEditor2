private bool IsInEditMode => CenterHost.Content is EditPageView;

// Toggle "Edit Mode" ON
private void EditToggle_Checked(object sender, RoutedEventArgs e)
{
    EnterEditMode();
}

// Toggle "Edit Mode" OFF
private void EditToggle_Unchecked(object sender, RoutedEventArgs e)
{
    LeaveEditMode();
}

private void EnterEditMode()
{
    if (_pdfiumDoc == null) { WpfMessageBox.Show(this, "Open a PDF first."); EditToggle.IsChecked = false; return; }

    // Ensure we have parsed content
    if (_parsedDoc == null)
    {
        if (string.IsNullOrEmpty(_currentPath)) { WpfMessageBox.Show(this, "Missing original path."); EditToggle.IsChecked = false; return; }
        _parsedDoc = PdfExtractor.Parse(_currentPath);
    }

    // Render current page to a bitmap
    var pageIdx = Math.Clamp(_renderer.Page, 0, _pdfiumDoc.PageCount - 1);
    var parsedPage = _parsedDoc.Pages[Math.Clamp(pageIdx, 0, _parsedDoc.Pages.Count - 1)];

    const int dpi = 144;
    int pxW = (int)Math.Round(parsedPage.WidthPt  * dpi / 72.0);
    int pxH = (int)Math.Round(parsedPage.HeightPt * dpi / 72.0);

    using var bmp = _pdfiumDoc.Render(pageIdx, pxW, pxH, dpi, dpi, true);
    var bmpSource = CreateBitmapSourceAndFree(bmp);

    // Build the editor surface and load this page
    _editView = new EditPageView();
    _editView.Load(parsedPage, bmpSource);

    // Swap center to editor
    CenterHost.Content = _editView;
}

private void LeaveEditMode()
{
    // Capture current edits back into the model
    CaptureEditsIfEditing();

    // Swap back to the PdfRenderer host
    _viewerHost = new WFI.WindowsFormsHost { Background = Brushes.White, Child = _renderer };
    CenterHost.Content = _viewerHost;
}

private void CaptureEditsIfEditing()
{
    if (_editView != null && _parsedDoc != null && IsInEditMode)
    {
        var edited = _editView.ApplyEdits();
        var idx = Math.Clamp(_renderer.Page, 0, _parsedDoc.Pages.Count - 1);
        var pages = _parsedDoc.Pages.ToList();
        pages[idx] = edited;
        _parsedDoc = new ParsedDocument(pages);
    }
}

// If user navigates pages while editing, rebuild the editor for that page
private void RefreshEditSurfaceIfActive()
{
    if (_pdfiumDoc == null || _parsedDoc == null || !IsInEditMode) return;

    var pageIdx = Math.Clamp(_renderer.Page, 0, _pdfiumDoc.PageCount - 1);
    var parsedPage = _parsedDoc.Pages[Math.Clamp(pageIdx, 0, _parsedDoc.Pages.Count - 1)];

    const int dpi = 144;
    int pxW = (int)Math.Round(parsedPage.WidthPt  * dpi / 72.0);
    int pxH = (int)Math.Round(parsedPage.HeightPt * dpi / 72.0);

    using var bmp = _pdfiumDoc.Render(pageIdx, pxW, pxH, dpi, dpi, true);
    var bmpSource = CreateBitmapSourceAndFree(bmp);

    _editView = new EditPageView();
    _editView.Load(parsedPage, bmpSource);
    CenterHost.Content = _editView;
}
