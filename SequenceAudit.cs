using System.IO;

namespace AppAvvio;

public static class SequenceAudit
{
    public static string Check(LaunchItem item, IReadOnlyList<StartupEntry> snapshot, bool partial, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var issues = new List<string>();
        if (!fileExists(Environment.ExpandEnvironmentVariables(item.Path))) issues.Add("File non trovato");
        if (item.WorkingDirectory.Length > 0 && !Directory.Exists(Environment.ExpandEnvironmentVariables(item.WorkingDirectory))) issues.Add("Cartella di lavoro non trovata");
        var original = item.OriginalStartup;
        var exact = original == null ? null : snapshot.FirstOrDefault(x => x.Identity == original.Identity);
        if (exact?.IsEnabled == true)
            issues.Add("Avvio Windows riattivato" + (Changed(original!, exact) ? " · comando modificato" : ""));
        else if (exact != null && original != null && Changed(original, exact)) issues.Add("Configurazione originale modificata");
        else if (original?.Source == "Task Scheduler" && exact == null && !partial) issues.Add("Attività originale non trovata");
        var candidates = snapshot.Where(x => x.IsEnabled && x.Identity != original?.Identity && Matches(item, x)).ToList();
        if (candidates.Count > 0) issues.Add("Possibile altro avvio Windows: " + string.Join(", ", candidates.Select(x => x.Name).Distinct()));
        if (partial) issues.Add("Verifica parziale");
        return issues.Count == 0 ? "Configurazione OK" : string.Join("; ", issues);
    }
    private static bool Changed(StartupEntry original, StartupEntry current) => original.Source == "Task Scheduler"
        ? !ScheduledStartup.SameDefinition(original.TaskXml, current.TaskXml)
        : !string.Equals(original.Command, current.Command, StringComparison.Ordinal);

    private static bool Matches(LaunchItem item, StartupEntry entry)
    {
        // A name match is deliberately reported as possible, never corrected automatically.
        if (item.Name.Length > 2 && string.Equals(item.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Command.Length == 0) return false;
        try
        {
            var (path, args) = StartupCatalog.ParseCommand(entry.Command);
            return string.Equals(Environment.ExpandEnvironmentVariables(item.Path), path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Environment.ExpandEnvironmentVariables(item.Arguments).Trim(), args.Trim(), StringComparison.Ordinal);
        }
        catch (InvalidDataException) { return false; }
    }
}
