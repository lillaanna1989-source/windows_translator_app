using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WindowsWritingAssistant;

public static class ScreenOcrTranslator
{
    private const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    public static async Task CaptureAndTranslateAsync()
    {
        var overlay = new ScreenCaptureOverlay();
        overlay.ShowDialog();
        if (!overlay.Accepted) return;

        var r = overlay.SelectedScreenRect;
        string png = Path.Combine(Path.GetTempPath(), "WWA_OCR_" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            SaveScreenRegionAsPng(r, png);
            string text = await OcrWithWindowsAsync(png);

            if (String.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(
                    "No text was recognized. Windows OCR needs the appropriate OCR language installed for the text on screen.",
                    "Screen Translation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string target = CultureInfo.CurrentUICulture.Name;
            if (String.IsNullOrWhiteSpace(target)) target = "en";

            string translated = await TextAssistant.TranslateForOcrAsync(text, target);
            new OcrResultWindow(text, translated, CultureInfo.CurrentUICulture.DisplayName).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Screen translation couldn't complete.\n\n" + ex.Message,
                "Screen Translation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { File.Delete(png); } catch { }
        }
    }

    // Only one SmartReplace request can be active at once. Overlays are hidden
    // before the selection/capture so OCR never reads WWA's own translations.
    private static readonly System.Threading.SemaphoreSlim replaceGate = new(1, 1);
    private static readonly System.Collections.Generic.List<SmartReplaceOverlay> activeOverlays = new();
    private static readonly System.Collections.Generic.Dictionary<string, string> translationCache = new();

    public static async Task CaptureAndReplaceAsync(string targetLanguage)
    {
        if (!await replaceGate.WaitAsync(0)) return;
        string? png = null;
        try
        {
            // Hide overlays while the user selects a region and while capturing it.
            foreach (var existing in activeOverlays.ToArray())
                if (existing.IsLoaded) existing.Hide();

            var selection = new ScreenCaptureOverlay();
            selection.ShowDialog();
            if (!selection.Accepted) return;

            var rect = selection.SelectedScreenRect;
            png = Path.Combine(Path.GetTempPath(), "WWA_OCR_" + Guid.NewGuid().ToString("N") + ".png");
            SaveScreenRegionAsPng(rect, png);
            string original = (await OcrWithWindowsAsync(png)).Trim();
            if (String.IsNullOrWhiteSpace(original)) return;

            string language = String.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
            string cacheKey = language.ToLowerInvariant() + "\n" + original;
            if (!translationCache.TryGetValue(cacheKey, out string? translated))
            {
                translated = (await TextAssistant.TranslateForOcrAsync(original, language)).Trim();
                if (translationCache.Count >= 150) translationCache.Clear();
                translationCache[cacheKey] = translated;
            }

            // Do not overlay text that is unchanged by translation (often already
            // in the destination language). This is not perfect language detection.
            if (String.IsNullOrWhiteSpace(translated) ||
                String.Equals(translated, original, StringComparison.OrdinalIgnoreCase)) return;

            // Replace any previous WWA overlay covering the same region.
            foreach (var existing in activeOverlays.ToArray())
            {
                if (!existing.IsLoaded) { activeOverlays.Remove(existing); continue; }
                if (existing.ScreenRegion.IntersectsWith(rect)) existing.Close();
            }
            var display = new SmartReplaceOverlay(rect, translated);
            display.Closed += (_, _) => activeOverlays.Remove(display);
            activeOverlays.Add(display);
            display.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WWA SmartReplace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            // Restore other translations only after OCR has finished.
            foreach (var existing in activeOverlays.ToArray())
                if (existing.IsLoaded && !existing.IsVisible) existing.Show();
            if (png != null) { try { File.Delete(png); } catch { } }
            replaceGate.Release();
        }
    }

internal static void SaveScreenRegionAsPng(Rect r, string path)
    {
        int x = (int)Math.Floor(r.X);
        int y = (int)Math.Floor(r.Y);
        int width = Math.Max(1, (int)Math.Ceiling(r.Width));
        int height = Math.Max(1, (int)Math.Ceiling(r.Height));

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new InvalidOperationException("Could not access the screen.");

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) throw new InvalidOperationException("Could not create a screen capture context.");

            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("Could not create the screen capture bitmap.");

            oldBitmap = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
                throw new InvalidOperationException("Screen capture failed.");

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static async Task<string> OcrWithWindowsAsync(string path)
    {
        string esc = path.Replace("'", "''");
        string ps = "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Runtime.WindowsRuntime; " +
            "$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]; " +
            "$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]; " +
            "$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]; " +
            "function A($o,[type]$t){$m=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1; if(!$m){throw 'WinRT AsTask<T> overload missing'}; $m.MakeGenericMethod($t).Invoke($null,@($o)).GetAwaiter().GetResult()}; " +
            "$f=A ([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + esc + "')) ([Windows.Storage.StorageFile]); " +
            "$s=A ($f.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream]); " +
            "$d=A ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($s)) ([Windows.Graphics.Imaging.BitmapDecoder]); " +
            "$b=A ($d.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap]); " +
            "$e=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages(); " +
            "if($null -eq $e){throw 'Windows OCR is unavailable.'}; " +
            "$r=A ($e.RecognizeAsync($b)) ([Windows.Media.Ocr.OcrResult]); [Console]::OutputEncoding=[Text.Encoding]::UTF8; $r.Text";

        var si = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps.Replace("\"", "\\\"") + "\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var p = Process.Start(si)!;
        string output = await p.StandardOutput.ReadToEndAsync();
        string err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new Exception(String.IsNullOrWhiteSpace(err) ? "Windows OCR failed." : err.Trim());
        return output.Trim();
    }
}
