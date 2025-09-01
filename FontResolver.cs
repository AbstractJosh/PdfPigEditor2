using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;

namespace PdfStudio
{
    // Registers Arial from Windows or your app's Fonts folder.
    // You can add other families later.
    public sealed class AppFontResolver : IFontResolver
    {
        // Map faceName => filename
        private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial#Regular"]    = "arial.ttf",
            ["Arial#Bold"]       = "arialbd.ttf",
            ["Arial#Italic"]     = "ariali.ttf",
            ["Arial#BoldItalic"] = "arialbi.ttf",
        };

        public string DefaultFontName => "Arial";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            // Normalize to Arial; you can branch by familyName if you support more
            var fam = "Arial";
            var style = (isBold, isItalic) switch
            {
                (true, true)   => "BoldItalic",
                (true, false)  => "Bold",
                (false, true)  => "Italic",
                _              => "Regular"
            };
            return new FontResolverInfo($"{fam}#{style}");
        }

        public byte[] GetFont(string faceName)
        {
            if (!_map.TryGetValue(faceName, out var file))
                file = "arial.ttf";

            // 1) Try Windows Fonts (works on Windows machines)
            var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var path = Path.Combine(winFonts, file);
            if (File.Exists(path)) return File.ReadAllBytes(path);

            // 2) Fallback to app-local Fonts folder (ship TTFs with your app)
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", file);
            if (File.Exists(local)) return File.ReadAllBytes(local);

            throw new FileNotFoundException($"Font file not found for '{faceName}'. Looked for '{file}'.");
        }
    }

    public static class AppFonts
    {
        private static bool _done;
        public static void Ensure()
        {
            if (_done) return;
            if (GlobalFontSettings.FontResolver is not AppFontResolver)
                GlobalFontSettings.FontResolver = new AppFontResolver();
            _done = true;
        }
    }
}
