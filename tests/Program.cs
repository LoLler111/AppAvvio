using AppAvvio;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

var first = new LaunchItem { Path = @"C:\missing-appavvio-test\first.exe" };
var second = new LaunchItem { Path = @"C:\missing-appavvio-test\second.exe" };
var disabled = new LaunchItem { Enabled = false, Path = first.Path };
await StartupService.RunAsync([first, disabled, second], CancellationToken.None);
Check(first.Status == "File non trovato" && second.Status == "File non trovato", "Un file mancante non blocca le app successive");
Check(disabled.Status == "Da avviare", "Le app disabilitate sono saltate");
var waiting = new LaunchItem { Path = first.Path, DelaySeconds = 60 };
var later = new LaunchItem { Path = second.Path };
using var cancellation = new CancellationTokenSource(100);
try
{
    await StartupService.RunAsync([waiting, later], cancellation.Token);
    throw new Exception("La cancellazione non è stata propagata");
}
catch (OperationCanceledException) { }
Check(waiting.Status == "Annullato", "L'attesa può essere annullata");
Check(later.Status == "Da avviare", "Dopo l'interruzione non vengono avviate altre app");

var parsedCommand = StartupCatalog.ParseCommand("\"C:\\Users\\Test\\Discord\\Update.exe\" --processStart Discord.exe");
Check(parsedCommand.Path.EndsWith("Update.exe") && parsedCommand.Arguments == "--processStart Discord.exe", "Il launcher di Discord mantiene gli argomenti originali");
var testKey = @"Software\AppAvvioTests\" + Guid.NewGuid().ToString("N");
try
{
    var original = new StartupEntry { Name = "Test", Command = "\"%LOCALAPPDATA%\\Discord\\Update.exe\" --processStart Discord.exe", Source = "Registro utente", ValueKind = (int)Microsoft.Win32.RegistryValueKind.ExpandString };
    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testKey)) key.SetValue(original.Name, original.Command, Microsoft.Win32.RegistryValueKind.ExpandString);
    StartupCatalog.Disable(original, testKey);
    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey)) Check(key!.GetValue(original.Name) == null, "La presa in gestione disattiva la voce originale");
    StartupCatalog.Restore(original, testKey);
    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey)) Check(Equals(key!.GetValue(original.Name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames), original.Command) && key.GetValueKind(original.Name) == Microsoft.Win32.RegistryValueKind.ExpandString, "Ripristino esatto del comando e del tipo di registro");
    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey, true)) key!.SetValue(original.Name, "Changed");
    try { StartupCatalog.Disable(original, testKey); throw new Exception("Modifica concorrente ignorata"); } catch (IOException) { }
    try { StartupCatalog.Restore(original, testKey); throw new Exception("Voce diversa sovrascritta"); } catch (IOException) { }
    Check(true, "Le modifiche concorrenti non vengono cancellate o sovrascritte");
}
finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey, false); }

var fixtureSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
var taskXml = $"""
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
 <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{fixtureSid}</UserId></LogonTrigger></Triggers>
 <Principals><Principal id="Author"><UserId>{fixtureSid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
 <Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>
 <Actions Context="Author"><Exec><Command>C:\Windows\System32\notepad.exe</Command><Arguments>test.txt</Arguments><WorkingDirectory>C:\Windows</WorkingDirectory></Exec></Actions>
</Task>
""";
var scheduled = ScheduledStartup.Parse("Test", @"\AppAvvioTests\Test", taskXml, fixtureSid)!;
Check(scheduled.CanManage && scheduled.WorkingDirectory == @"C:\Windows" && scheduled.Command.Contains("test.txt"), "Attività utente importabile con argomenti e cartella di lavoro");
Check(!ScheduledStartup.Parse("Test", @"\Test", taskXml.Replace("LeastPrivilege", "HighestAvailable"), fixtureSid)!.CanManage, "Attività elevate non convertite in avvii ordinari");
Check(!ScheduledStartup.Parse("Test", @"\Test", taskXml.Replace("LogonTrigger", "BootTrigger"), fixtureSid)!.CanManage, "Attività prima del login solo consultabili");
Check(ScheduledStartup.Parse("Test", @"\Test", taskXml.Replace("LogonTrigger", "TimeTrigger"), fixtureSid) == null, "Le attività senza avvio o accesso sono escluse");
Check(!ScheduledStartup.Parse("Test", @"\Microsoft\Test", taskXml, fixtureSid)!.CanManage, "Attività di Windows solo consultabili");
Check(ScheduledStartup.SameDefinition(taskXml, taskXml.Replace("<Enabled>true</Enabled></Settings>", "<Enabled>false</Enabled></Settings>")), "Il confronto XML consente il ripristino di una attività disabilitata");
Check(!ScheduledStartup.SameDefinition(taskXml, taskXml.Replace("test.txt", "changed.txt")), "Una attività aggiornata non viene sovrascritta");
var registryEntry = new StartupEntry { Name = "32bit", Source = "Registro utente", RegistryViewId = 512 };
Check(registryEntry.Description.Contains("32 bit"), "Origine registro 32 bit visibile");

var auditOriginal = new StartupEntry { Name = "Chat", Source = "Registro utente", Command = "\"C:\\Chat\\Update.exe\" --start Chat.exe" };
var auditItem = new LaunchItem { Name = "Chat", Path = @"C:\Chat\Update.exe", Arguments = "--start Chat.exe", OriginalStartup = auditOriginal };
Check(SequenceAudit.Check(auditItem, [], false, _ => true) == "Configurazione OK", "Voce rimossa da Windows e sequenza presente sono una configurazione valida");
Check(SequenceAudit.Check(auditItem, [auditOriginal], false, _ => true).Contains("riattivato"), "Rilevato ritorno dell'avvio originale");
var changedOriginal = new StartupEntry { Name = "Chat", Source = "Registro utente", Command = "\"C:\\Chat\\New.exe\"" };
Check(SequenceAudit.Check(auditItem, [changedOriginal], false, _ => true).Contains("comando modificato"), "Rilevato comando cambiato dopo un aggiornamento");
var otherSource = new StartupEntry { Name = "NewChatLauncher", Source = "Task Scheduler", Command = auditOriginal.Command, Location = @"\NewChat" };
Check(SequenceAudit.Check(auditItem, [otherSource], false, _ => true).Contains("Possibile altro avvio"), "Rilevato avvio equivalente in una origine differente");
otherSource.IsEnabled = false;
Check(SequenceAudit.Check(auditItem, [otherSource], false, _ => true) == "Configurazione OK", "Gli avvii disabilitati non sono segnalati come conflitti");
Check(SequenceAudit.Check(auditItem, [], false, _ => false).Contains("File non trovato"), "Percorso della sequenza mancante segnalato");
Check(SequenceAudit.Check(auditItem, [], true, _ => true).Contains("parziale"), "Una scansione incompleta non viene dichiarata valida");
var taskItem = new LaunchItem { Name = scheduled.Name, Path = @"C:\Windows\System32\notepad.exe", OriginalStartup = scheduled };
var disabledScheduled = ScheduledStartup.Parse(scheduled.Name, scheduled.Location, scheduled.TaskXml, fixtureSid)!;
disabledScheduled.IsEnabled = false;
Check(SequenceAudit.Check(taskItem, [disabledScheduled], false, _ => true) == "Configurazione OK", "Attività originale disabilitata e invariata è attesa");
Check(SequenceAudit.Check(taskItem, [], false, _ => true).Contains("Attività originale non trovata"), "Attività di backup eliminata viene segnalata");
Check(auditItem.Path == @"C:\Chat\Update.exe" && auditOriginal.Command.Contains("--start Chat.exe"), "Il controllo non modifica percorso o backup");
var backupJson = System.Text.Json.JsonSerializer.Serialize(new Configuration { DisabledStartup = [scheduled, auditOriginal] });
var backupConfig = System.Text.Json.JsonSerializer.Deserialize<Configuration>(backupJson)!;
Check(backupConfig.DisabledStartup.Count == 2 && backupConfig.Items.Count == 0 && backupConfig.DisabledStartup[0].TaskXml == taskXml, "Disattivazione indipendente dalla sequenza conserva i backup dopo la riapertura");

var notes = UpdateService.FormatChangelog("""
[
 {"draft":false,"prerelease":false,"tag_name":"v0.2.0","published_at":"2026-10-22T12:00:00Z","body":"Interfaccia e aggiornamenti"},
 {"draft":true,"prerelease":false,"tag_name":"draft","published_at":null,"body":"Nascosto"},
 {"draft":false,"prerelease":true,"tag_name":"beta","published_at":null,"body":"Nascosto"}
]
""");
Check(notes.Contains("v0.2.0") && notes.Contains("22/10/2026") && !notes.Contains("Nascosto"), "Il changelog include solo release pubbliche stabili");
Check(UpdateService.FormatChangelog("[]").Contains("Nessuna"), "Gestione repository senza release");
Velopack.VelopackApp.Build().Run();
Check(!new UpdateService().IsInstalled, "La build di sviluppo non tenta di aggiornarsi come installazione");

if (args.Contains("--github"))
{
    var cache = Path.GetFullPath("artifacts/update-test");
    Directory.CreateDirectory(cache);
    var locator = new Velopack.Locators.TestVelopackLocator("AppAvvio", "0.1.0", cache);
    var updater = new UpdateService(locator);
    var update = await updater.CheckAsync();
    Check(update != null && update.TargetFullRelease.Version.ToString() == UpdateService.CurrentVersion, "GitHub propone la release pubblicata a un client precedente");
    await updater.DownloadAsync(update!, _ => { });
    Check(File.Exists(Path.Combine(cache, update!.TargetFullRelease.FileName)), "Download e verifica integrità del pacchetto GitHub completati");
    var current = new UpdateService(new Velopack.Locators.TestVelopackLocator("AppAvvio", UpdateService.CurrentVersion, cache));
    Check(await current.CheckAsync() == null, "La versione corrente non propone reinstallazioni");
}

if (args.Contains("--scheduler-integration"))
{
    dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
    service.Connect();
    dynamic rootFolder = service.GetFolder("\\");
    var folderName = "AppAvvioTest-" + Guid.NewGuid().ToString("N");
    dynamic folder = rootFolder.CreateFolder(folderName);
    try
    {
        dynamic registered = folder.RegisterTask("Test", taskXml, 2, fixtureSid, null, 3);
        var entry = ScheduledStartup.Parse("Test", "\\" + folderName + "\\Test", (string)registered.Xml, fixtureSid)!;
        Check(entry.CanManage, "Attività reale riconosciuta come compatibile");
        Check(StartupCatalog.Read().Any(x => x.Location == entry.Location), "Scansione ricorsiva trova una attività nella sottocartella di prova");
        StartupCatalog.Disable(entry);
        Check(!(bool)folder.GetTask("Test").Enabled, "Disattivazione reale della sola attività di prova");
        StartupCatalog.Restore(entry);
        Check((bool)folder.GetTask("Test").Enabled, "Ripristino reale dello stato originale");
        registered = folder.RegisterTask("Test", taskXml.Replace("test.txt", "changed.txt"), 6, fixtureSid, null, 3);
        try { StartupCatalog.Disable(entry); throw new Exception("Attività modificata disabilitata"); } catch (IOException) { }
        Check((bool)folder.GetTask("Test").Enabled, "Attività modificata conservata senza interventi");
    }
    finally
    {
        try { folder.DeleteTask("Test", 0); }
        finally { rootFolder.DeleteFolder(folderName, 0); }
    }
}

if (args.Contains("--render"))
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            var grid = (DataGrid)window.FindName("AppsGrid");
            var previewItems = (System.Collections.ObjectModel.ObservableCollection<LaunchItem>)grid.ItemsSource;
            if (previewItems.Count == 0)
            {
                previewItems.Add(new LaunchItem { Name = "Discord", Path = @"C:\Discord\Update.exe", Health = "Avvio Windows riattivato", DelaySeconds = 0 });
                previewItems.Add(new LaunchItem { Name = "Steam", Path = @"C:\Steam\Steam.exe", Health = "Configurazione OK", DelaySeconds = 90 });
            }
            var root = (FrameworkElement)window.Content;
            Directory.CreateDirectory("artifacts/screenshots");
            void Render(string name, int width, int height)
            {
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create("artifacts/screenshots/" + name + ".png");
                encoder.Save(file);
            }
            Render("sequence", 1220, 910);
            Render("sequence-small", 1020, 810);
            ((RadioButton)window.FindName("UpdatesNav")).IsChecked = true;
            Render("updates", 1220, 780);
            window.Close();
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw failure;
    Console.WriteLine("PASS: Rendering WPF delle due pagine e della dimensione minima");
}

