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
        var names = items.Where(x => x.Enabled).Select(x => x.Name).Where(x => x.Length > 2).ToArray();
        var conflicts = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(RunKey);
            if (key == null) continue;
            foreach (var name in key.GetValueNames().Where(n => n != "AppAvvio"))
            {
                var command = key.GetValue(name)?.ToString() ?? "";
                if (names.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase) || command.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    conflicts.Add($"{name} — {hive.Name}\\{RunKey}");
            }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var path = Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path))
                if (names.Any(n => System.IO.Path.GetFileNameWithoutExtension(file).Contains(n, StringComparison.OrdinalIgnoreCase)))
                    conflicts.Add(System.IO.Path.GetFileName(file) + " — cartella Esecuzione automatica");
        }
        return conflicts.Distinct().ToList();
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
                { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(item.Path)! });
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
