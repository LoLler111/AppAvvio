using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace AppAvvio;

public static class ScheduledStartup
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    public static List<StartupEntry> Read(List<string> warnings, bool includeDisabled = false)
    {
        var result = new List<StartupEntry>();
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect();
            dynamic root = service.GetFolder("\\");
            try { Walk(root, result, warnings, includeDisabled); }
            finally { Release(root); }
        }
        finally { Release(service); }
        return result;
    }
    private static void Walk(dynamic folder, List<StartupEntry> entries, List<string> warnings, bool includeDisabled)
    {
        object? tasks = null;
        object? folders = null;
        try
        {
            tasks = folder.GetTasks(1); // Include hidden tasks.
            for (var i = 1; i <= ((dynamic)tasks).Count; i++)
            {
                dynamic task = ((dynamic)tasks)[i];
                try
                {
                    if (!(bool)task.Enabled && !includeDisabled) continue;
                    var entry = Parse((string)task.Name, (string)task.Path, (string)task.Xml, WindowsIdentity.GetCurrent().User!.Value);
                    if (entry != null) { entry.IsEnabled = (bool)task.Enabled; entries.Add(entry); }
                }
                catch (Exception ex) { warnings.Add("Attività non leggibile: " + ex.Message); }
                finally { Release(task); }
            }
            folders = folder.GetFolders(0);
            for (var i = 1; i <= ((dynamic)folders).Count; i++)
            {
                dynamic child = ((dynamic)folders)[i];
                try { Walk(child, entries, warnings, includeDisabled); }
                finally { Release(child); }
            }
        }
        catch (Exception ex) { warnings.Add("Cartella Task Scheduler non leggibile: " + ex.Message); }
        finally { Release(tasks); Release(folders); }
    }
    public static StartupEntry? Parse(string name, string path, string xml, string currentSid)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        var triggers = root.Element(Ns + "Triggers")?.Elements().Where(x => (string?)x.Element(Ns + "Enabled") != "false").ToList() ?? [];
        if (!triggers.Any(x => x.Name == Ns + "LogonTrigger" || x.Name == Ns + "BootTrigger")) return null;
        var actions = root.Element(Ns + "Actions")?.Elements().ToList() ?? [];
        var principal = root.Element(Ns + "Principals")?.Element(Ns + "Principal");
        var user = (string?)principal?.Element(Ns + "UserId") ?? "";
        bool CurrentUser(string identity)
        {
            if (identity.Equals(currentSid, StringComparison.OrdinalIgnoreCase)) return true;
            try { return ((SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier))).Value == currentSid; }
            catch (IdentityNotMappedException) { return false; }
            catch (ArgumentException) { return false; }
        }
        var reason = "";
        if (path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) reason = "attività di Windows";
        else if (triggers.Any(x => x.Name == Ns + "BootTrigger")) reason = "avvio prima dell’accesso";
        else if (triggers.Count != 1 || triggers[0].Name != Ns + "LogonTrigger") reason = "più condizioni di attivazione";
        else if (actions.Count != 1 || actions[0].Name != Ns + "Exec") reason = "azioni multiple o non eseguibili";
        else if (!CurrentUser(user) || (string?)principal?.Element(Ns + "LogonType") != "InteractiveToken") reason = "contesto utente differente";
        else if ((string?)principal?.Element(Ns + "RunLevel") == "HighestAvailable") reason = "richiede privilegi elevati";
        else if (triggers[0].Element(Ns + "UserId") is XElement triggerUser && !CurrentUser(triggerUser.Value)) reason = "accesso di un altro utente";
        else if (triggers[0].Elements().Any(x => x.Name.LocalName is "Repetition" or "StartBoundary" or "EndBoundary" or "Delay")) reason = "temporizzazione specifica";
        var settings = root.Element(Ns + "Settings");
        if (reason.Length == 0 && ((string?)settings?.Element(Ns + "ExecutionTimeLimit") ?? "PT72H") != "PT0S") reason = "limite di durata specifico";
        if (reason.Length == 0 && ((string?)settings?.Element(Ns + "DisallowStartIfOnBatteries") != "false" || (string?)settings?.Element(Ns + "StopIfGoingOnBatteries") != "false")) reason = "condizioni relative alla batteria";
        if (reason.Length == 0 && settings != null && settings.Elements().Any(x =>
            (x.Name.LocalName is "RunOnlyIfNetworkAvailable" or "RunOnlyIfIdle" or "DisallowStartIfOnBatteries" or "StopIfGoingOnBatteries" && x.Value == "true") ||
            x.Name.LocalName is "RestartOnFailure" or "MaintenanceSettings")) reason = "condizioni di esecuzione specifiche";
        var exec = actions.FirstOrDefault(x => x.Name == Ns + "Exec");
        var executable = (string?)exec?.Element(Ns + "Command") ?? "";
        var args = (string?)exec?.Element(Ns + "Arguments") ?? "";
        if (reason.Length == 0 && executable.Length == 0) reason = "comando non disponibile";
        return new() { Name = name, Location = path, Source = "Task Scheduler", TaskXml = xml,
            Command = executable.Length > 0 ? $"\"{executable.Trim('"')}\" {args}".TrimEnd() : "Azione non eseguibile",
            WorkingDirectory = (string?)exec?.Element(Ns + "WorkingDirectory") ?? "", ManualReason = reason };
    }
    private static string ComparableXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        doc.Root?.Element(Ns + "Settings")?.Element(Ns + "Enabled")?.Remove();
        return doc.ToString(SaveOptions.DisableFormatting);
    }
    public static bool SameDefinition(string original, string current) => ComparableXml(original) == ComparableXml(current);
    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? folder = null;
        object? task = null;
        try
        {
            service.Connect();
            folder = service.GetFolder("\\");
            task = ((dynamic)folder).GetTask(entry.Location);
            if (!SameDefinition(entry.TaskXml, (string)((dynamic)task).Xml)) throw new IOException("L’attività è stata modificata: non è stata sovrascritta. Controllala in Utilità di pianificazione.");
            ((dynamic)task).Enabled = enabled;
        }
        finally { Release(task); Release(folder); Release(service); }
    }
}
