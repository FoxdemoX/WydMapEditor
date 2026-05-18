using System.Threading;
using System.Windows.Forms;

namespace WydMapEditor;

public static class Dialogs
{
    public static Task<string?> PickFolderAsync(string? initialPath = null, string? description = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(PickFolder(initialPath, description));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    public static string? PickFolder(string? initialPath = null, string? description = null)
    {
        using var dlg = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(initialPath))
            dlg.InitialDirectory = initialPath;
        if (!string.IsNullOrWhiteSpace(description))
            dlg.Description = description;
        dlg.UseDescriptionForTitle = true;

        var result = dlg.ShowDialog();
        if (result != DialogResult.OK)
            return null;
        return dlg.SelectedPath;
    }

    public static Task<string?> PickFileAsync(string filter, string? initialPath = null, string? title = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(PickFile(filter, initialPath, title));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    public static string? PickFile(string filter, string? initialPath = null, string? title = null)
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = filter;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dlg.InitialDirectory = initialPath;
        if (!string.IsNullOrWhiteSpace(title))
            dlg.Title = title;

        var result = dlg.ShowDialog();
        if (result != DialogResult.OK)
            return null;
        return dlg.FileName;
    }

    public static Task<string[]?> PickFilesAsync(string filter, string? initialPath = null, string? title = null)
    {
        var tcs = new TaskCompletionSource<string[]?>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(PickFiles(filter, initialPath, title));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    public static string[]? PickFiles(string filter, string? initialPath = null, string? title = null)
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = filter;
        dlg.Multiselect = true;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dlg.InitialDirectory = initialPath;
        if (!string.IsNullOrWhiteSpace(title))
            dlg.Title = title;

        var result = dlg.ShowDialog();
        if (result != DialogResult.OK)
            return null;
        return dlg.FileNames;
    }
}
