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
    public bool CanManage => Source is "Registro utente" or "Cartella utente";
    public string Description => CanManage ? Source : Source + " · gestione manuale";
}

public static class StartupCatalog
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static List<StartupEntry> Read()
    {
        var result = new List<StartupEntry>();
        foreach (var machine in new[] { false, true })
        {
            using var key = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(RunKey);
            using var approved = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            if (key == null) continue;
            foreach (var name in key.GetValueNames().Where(n => n != "AppAvvio"))
            {
                if (approved?.GetValue(name) is byte[] state && state.Length > 0 && state[0] is 3 or 7) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                    result.Add(new() { Name = name, Command = command, Source = machine ? "Registro sistema" : "Registro utente", ValueKind = (int)key.GetValueKind(name) });
            }
        }
        foreach (var common in new[] { false, true })
        {
            var folder = Environment.GetFolderPath(common ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
            if (!Directory.Exists(folder)) continue;
            using var approved = (common ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                if (approved?.GetValue(Path.GetFileName(file)) is byte[] state && state.Length > 0 && state[0] is 3 or 7) continue;
                result.Add(new() { Name = Path.GetFileNameWithoutExtension(file), Location = file, Source = common ? "Cartella sistema" : "Cartella utente" });
            }
        }
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
        var (path, args) = string.IsNullOrEmpty(entry.Location) ? ParseCommand(entry.Command) : (entry.Location, "");
        if (!File.Exists(path)) throw new FileNotFoundException("Percorso di avvio non trovato: " + path);
        if (entry.Source == "Cartella utente")
        {
            Directory.CreateDirectory(Path.Combine(SettingsStore.Folder, "startup-backups"));
            entry.BackupPath = Path.Combine(SettingsStore.Folder, "startup-backups", Guid.NewGuid() + Path.GetExtension(path));
            File.Copy(path, entry.BackupPath);
            path = entry.BackupPath;
        }
        return new() { Name = entry.Name, Path = path, Arguments = args, OriginalStartup = entry };
    }
    public static void Disable(StartupEntry entry, string registryPath = RunKey)
    {
        if (entry.Source == "Registro utente")
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, true) ?? throw new IOException("Voce non disponibile.");
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
        if (entry.Source == "Registro utente")
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
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
