using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace AppAvvio;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("AppAvvio") is string;
    }
    public static void Register(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("AppAvvio", $"\"{Environment.ProcessPath}\" --startup");
        else key.DeleteValue("AppAvvio", false);
    }
    public static List<string> FindConflicts(IEnumerable<LaunchItem> items)
    {
        var managed = items.Where(x => x.Enabled).ToList();
        return StartupCatalog.Read()
            .Where(entry => managed.Any(item => item.OriginalStartup != null
                ? entry.Identity == item.OriginalStartup.Identity
                : item.Name.Length > 2 && entry.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Name + " — " + entry.Description)
            .Distinct().ToList();
    }
    public static async Task RunAsync(IEnumerable<LaunchItem> items, CancellationToken token)
    {
        foreach (var item in items.Where(x => x.Enabled))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                item.Status = $"In attesa: {item.DelaySeconds} s";
                await Task.Delay(TimeSpan.FromSeconds(item.DelaySeconds), token);
                token.ThrowIfCancellationRequested();
                if (!File.Exists(item.Path)) { item.Status = "File non trovato"; continue; }
                if (System.IO.Path.GetExtension(item.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && IsRunning(item.Path))
                { item.Status = "Già in esecuzione"; continue; }
                using var process = Process.Start(new ProcessStartInfo(item.Path, item.Arguments)
                { UseShellExecute = true, WorkingDirectory = string.IsNullOrWhiteSpace(item.WorkingDirectory) ? System.IO.Path.GetDirectoryName(item.Path)! : Environment.ExpandEnvironmentVariables(item.WorkingDirectory) });
                item.Status = "Avvio richiesto";
            }
            catch (OperationCanceledException) { item.Status = "Annullato"; throw; }
            catch (Exception ex) { item.Status = "Errore: " + ex.Message; }
        }
    }
    private static bool IsRunning(string path)
    {
        foreach (var process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }
}

