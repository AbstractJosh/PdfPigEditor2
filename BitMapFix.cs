using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

private static BitmapSource CreateBitmapSourceAndFree(System.Drawing.Image img)
{
    // Ensure we have a Bitmap (Pdfium returns Image in some versions)
    using var bmp = new System.Drawing.Bitmap(img);

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
        // bmp disposed by using
        img.Dispose(); // also dispose original Image
    }
}

[DllImport("gdi32.dll")]
private static extern bool DeleteObject(IntPtr hObject);
