using Microsoft.Win32;
using System.IO;

namespace AppAvvio;

public sealed class StartupEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Source { get; set; } = "";
    public string Location { get; set; } = "";
    public int ValueKind { get; set; }
    public string BackupPath { get; set; } = "";
    public string RegistryPath { get; set; } = StartupCatalog.RunKey;
    public int RegistryViewId { get; set; }
    public string TaskXml { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ManualReason { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string EnabledLabel => IsEnabled ? "Attiva" : "Disattivata";
    public string FriendlyDescription => CanManage ? "Puoi gestire questa app con AppAvvio" : "Gestione manuale · " + (ManualReason.Length > 0 ? ManualReason : "voce di sistema");
    public string Identity => Source + "|" + (RegistryViewId == 0 ? (Environment.Is64BitProcess ? 256 : 512) : RegistryViewId) + "|" + RegistryPath + "|" + Location + "|" + Name;
    public bool CanManage => ManualReason.Length == 0 && (Source is "Registro utente" or "Cartella utente" or "Task Scheduler");
    public string Description => (IsEnabled ? "Attiva · " : "Disattivata · ") + Source + (Source.StartsWith("Registro") ? (RegistryViewId == 512 ? " · 32 bit" : " · 64 bit") + " · " + RegistryPath.Split('\\').Last() : "") + (CanManage ? "" : " · " + (ManualReason.Length > 0 ? ManualReason : "gestione manuale"));
    public string Details => Location.Length > 0 ? Location + "\n" + Command : RegistryPath + "\n" + Command;
}

public static class StartupCatalog
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static List<string> Warnings { get; } = [];
    public static List<StartupEntry> Read(bool includeDisabled = false)
    {
        var result = new List<StartupEntry>();
        Warnings.Clear();
        foreach (var machine in new[] { false, true })
        foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 })
        foreach (var path in new[] { RunKey, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run" })
        {
            try
            {
            using var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
            using var key = hive.OpenSubKey(path);
            using var approved = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + (view == RegistryView.Registry32 && machine ? "Run32" : "Run"));
            if (key == null) continue;
            foreach (var name in key.GetValueNames().Where(n => n != "AppAvvio"))
            {
                var enabled = !(path == RunKey && approved?.GetValue(name) is byte[] state && state.Length > 0 && state[0] is 3 or 7);
                if (!enabled && !includeDisabled) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                {
                    // Some registry keys are shared between the two WOW64 views.
                    if (!machine && result.Any(x => x.Source == "Registro utente" && x.RegistryPath == path && x.Name == name && x.Command == command)) continue;
                    result.Add(new() { Name = name, Command = command, Source = machine ? "Registro sistema" : "Registro utente", ValueKind = (int)key.GetValueKind(name), RegistryPath = path, RegistryViewId = (int)view, IsEnabled = enabled,
                        ManualReason = path.EndsWith("RunOnce") ? "esecuzione unica" : path.Contains("Policies") ? "gestita da policy" : machine ? "richiede gestione di sistema" : "" });
                }
            }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Warnings.Add(path + ": " + ex.Message); }
        }
        foreach (var common in new[] { false, true })
        {
            try
            {
            var folder = Environment.GetFolderPath(common ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
            if (!Directory.Exists(folder)) continue;
            using var approved = (common ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var enabled = !(approved?.GetValue(Path.GetFileName(file)) is byte[] state && state.Length > 0 && state[0] is 3 or 7);
                if (!enabled && !includeDisabled) continue;
                result.Add(new() { Name = Path.GetFileNameWithoutExtension(file), Location = file, Source = common ? "Cartella sistema" : "Cartella utente", IsEnabled = enabled });
            }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Warnings.Add("Cartella di avvio: " + ex.Message); }
        }
        try { result.AddRange(ScheduledStartup.Read(Warnings, includeDisabled)); }
        catch (Exception ex) { Warnings.Add("Task Scheduler: " + ex.Message); }
        return result.OrderBy(x => x.Name).ToList();
    }
    public static (string Path, string Arguments) ParseCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command).Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) throw new InvalidDataException("Comando di avvio non valido.");
            return (command[1..end], command[(end + 1)..].Trim());
        }
        // Preserve arguments exactly; prefer an existing executable when the path contains spaces.
        for (var i = 0; i < command.Length; i++)
            if (command[i] == ' ' && File.Exists(command[..i])) return (command[..i], command[(i + 1)..].Trim());
        if (File.Exists(command)) return (command, "");
        var split = command.IndexOf(' ');
        return split < 0 ? (command, "") : (command[..split], command[(split + 1)..]);
    }
    public static LaunchItem Prepare(StartupEntry entry)
    {
        if (!entry.CanManage) throw new InvalidOperationException("Questa voce riguarda tutti gli utenti: gestiscila dalle impostazioni di Windows.");
        var (path, args) = entry.Source == "Task Scheduler" || string.IsNullOrEmpty(entry.Location) ? ParseCommand(entry.Command) : (entry.Location, "");
        if (!File.Exists(path)) throw new FileNotFoundException("Percorso di avvio non trovato: " + path);
        if (entry.Source == "Cartella utente")
        {
            BackupShortcut(entry);
            path = entry.BackupPath;
        }
        return new() { Name = entry.Name, Path = path, Arguments = args, WorkingDirectory = entry.WorkingDirectory, OriginalStartup = entry };
    }
    public static void BackupShortcut(StartupEntry entry)
    {
        Directory.CreateDirectory(Path.Combine(SettingsStore.Folder, "startup-backups"));
        entry.BackupPath = Path.Combine(SettingsStore.Folder, "startup-backups", Guid.NewGuid() + Path.GetExtension(entry.Location));
        File.Copy(entry.Location, entry.BackupPath);
    }
    public static void Disable(StartupEntry entry, string registryPath = RunKey)
    {
        if (!entry.CanManage) throw new InvalidOperationException(entry.Description);
        if (entry.Source == "Task Scheduler") { ScheduledStartup.SetEnabled(entry, false); return; }
        if (entry.Source == "Registro utente")
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, (RegistryView)entry.RegistryViewId);
            using var key = hive.OpenSubKey(registryPath == RunKey ? entry.RegistryPath : registryPath, true) ?? throw new IOException("Voce non disponibile.");
            if (!Equals(key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), entry.Command)) throw new IOException("La voce è cambiata: aggiorna l’elenco e riprova.");
            key.DeleteValue(entry.Name);
        }
        else if (entry.Source == "Cartella utente")
        {
            if (!File.ReadAllBytes(entry.Location).SequenceEqual(File.ReadAllBytes(entry.BackupPath))) throw new IOException("Il collegamento è cambiato: aggiorna l’elenco.");
            File.Delete(entry.Location);
        }
        else throw new InvalidOperationException("Voce non gestibile automaticamente.");
    }
    public static void Restore(StartupEntry entry, string registryPath = RunKey)
    {
        if (!entry.CanManage) throw new InvalidOperationException(entry.Description);
        if (entry.Source == "Task Scheduler") { ScheduledStartup.SetEnabled(entry, true); return; }
        if (entry.Source == "Registro utente")
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, (RegistryView)entry.RegistryViewId);
            using var key = hive.CreateSubKey(registryPath == RunKey ? entry.RegistryPath : registryPath);
            var existing = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (existing != null && !Equals(existing, entry.Command)) throw new IOException("Esiste una nuova voce di avvio con lo stesso nome: non è stata sovrascritta.");
            key.SetValue(entry.Name, entry.Command, (RegistryValueKind)entry.ValueKind);
        }
        else if (entry.Source == "Cartella utente")
        {
            if (File.Exists(entry.Location))
            {
                if (!File.ReadAllBytes(entry.Location).SequenceEqual(File.ReadAllBytes(entry.BackupPath))) throw new IOException("Esiste un collegamento diverso nella posizione originale.");
            }
            else File.Copy(entry.BackupPath, entry.Location);
        }
        else throw new InvalidOperationException("Origine non supportata.");
    }
}
