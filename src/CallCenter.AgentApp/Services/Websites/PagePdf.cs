using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace CallCenter.AgentApp.Services.Websites;

/// <summary>
/// A website's page saved as a PDF in the agent's Downloads folder, as a
/// browser's Print, Save as PDF would (Dia, 4 Oct 2026).
/// </summary>
/// <remarks>
/// Named after the page and the time, so a second save of the same page never
/// replaces the first. Backgrounds are printed: a receipt or an order that
/// marks things with colour should look on paper as it did on screen.
/// </remarks>
public static class PagePdf
{
    /// <summary>Prints the page to a new file in Downloads. Returns its path, or null if WebView2 said no.</summary>
    public static async Task<string?> SaveAsync(CoreWebView2 core, string fallbackName)
    {
        var folder = DownloadsFolder();
        Directory.CreateDirectory(folder);

        var name = string.IsNullOrWhiteSpace(core.DocumentTitle) ? fallbackName : core.DocumentTitle;
        var path = UniquePath(folder, $"{Clean(name)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}");

        var settings = core.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;

        return await core.PrintToPdfAsync(path, settings) ? path : null;
    }

    /// <summary>
    /// Where Windows says Downloads is: it may have been moved, to OneDrive or
    /// another drive. The usual place under the profile if Windows does not say.
    /// </summary>
    internal static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var buffer) == 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(buffer) is { Length: > 0 } path)
                {
                    return path;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>The name with what a file name cannot hold taken out, and kept short.</summary>
    internal static string Clean(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim().TrimEnd('.');

        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].Trim();
        }

        return cleaned.Length > 0 ? cleaned : "Page";
    }

    private static string UniquePath(string folder, string stem)
    {
        var path = Path.Combine(folder, stem + ".pdf");

        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}).pdf");
        }

        return path;
    }

    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
