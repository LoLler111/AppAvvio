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

var countdown = new LaunchItem { Path = first.Path, DelaySeconds = 2 };
var observed = new List<string>();
countdown.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LaunchItem.Status)) observed.Add(countdown.Status); };
await StartupService.RunAsync([countdown], CancellationToken.None);
Check(observed.Contains("Tra 2 s") && observed.Contains("Tra 1 s") && countdown.Status == "File non trovato", "Il conto alla rovescia si aggiorna e termina anche con un file mancante");
var storageFolder = Path.Combine(Path.GetTempPath(), "AppAvvioUxTest-" + Guid.NewGuid().ToString("N"));
var storagePath = Path.Combine(storageFolder, "settings.json");
try
{
    var config = new Configuration { QuietStartup = false, Items = [new LaunchItem { Name = "Test", Path = first.Path, DelaySeconds = 30 }] };
    SettingsStore.Save(config, storagePath);
    config.Items[0].DelaySeconds = 60;
    SettingsStore.Save(config, storagePath);
    Check(SettingsStore.Load(storagePath).Items[0].DelaySeconds == 60 && !SettingsStore.Load(storagePath).QuietStartup, "Attesa e preferenza di avvio discreto persistono dopo la riapertura");
    Check(SettingsStore.Load(storagePath + ".bak").Items[0].DelaySeconds == 30, "Il salvataggio mantiene una copia della configurazione precedente");
    config.Items[0].DelaySeconds = -1;
    try { SettingsStore.Save(config, storagePath); throw new Exception("Attesa negativa salvata"); } catch (InvalidDataException) { }
    Check(SettingsStore.Load(storagePath).Items[0].DelaySeconds == 60, "Una modifica non valida non sovrascrive i dati salvati");
    File.WriteAllText(storagePath, "{broken");
    try { SettingsStore.Load(storagePath); throw new Exception("Configurazione corrotta accettata"); } catch (System.Text.Json.JsonException) { }
    Check(SettingsStore.Load(storagePath + ".bak").Items.Count == 1, "La lettura di dati corrotti conserva il backup");
}
finally { foreach (var file in Directory.EnumerateFiles(storageFolder)) File.Delete(file); Directory.Delete(storageFolder); }

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
            var window = new MainWindow(preview: true);
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
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create("artifacts/screenshots/" + name + ".png");
                encoder.Save(file);
            }
            Render("sequence", 1180, 760);
            Render("sequence-small", 900, 580);
            var scroller = FindVisual<ScrollViewer>(grid);
            Check(scroller != null && scroller.ScrollableWidth == 0, "La sequenza non richiede scorrimento orizzontale alla larghezza minima");
            Check(grid.Columns[1].ActualWidth > 160, "Il nome dell’app mantiene spazio leggibile nella finestra compatta");
            var enabledBox = FindVisual<CheckBox>(grid)!;
            var toggled = (LaunchItem)enabledBox.DataContext;
            enabledBox.IsChecked = false;
            enabledBox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(!toggled.Enabled && toggled.HealthLabel == "In pausa", "L’interruttore mette in pausa l’app senza rimuoverla");
            enabledBox.IsChecked = true;
            enabledBox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            grid.SelectedIndex = 1;
            ((Button)window.FindName("UpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(previewItems[0].Name == "Steam" && previewItems[0].Order == 1 && previewItems[1].Order == 2, "Il riordino aggiorna ordine e selezione senza scrivere le impostazioni reali");
            ((RadioButton)window.FindName("WindowsNav")).IsChecked = true;
            var fixtures = new List<StartupEntry> {
                new() { Name = "Discord", Source = "Registro utente", IsEnabled = true },
                new() { Name = "Steam", Source = "Cartella utente", IsEnabled = true },
                new() { Name = "Attività di sistema", Source = "Task Scheduler", ManualReason = "attività di Windows" }
            };
            typeof(MainWindow).GetField("allWindowsItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, fixtures);
            var filter = (TextBox)window.FindName("WindowsSearch");
            filter.Text = "Discord";
            var windowsList = (ListBox)window.FindName("WindowsApps");
            Check(windowsList.Items.Count == 1, "La ricerca degli avvii filtra per nome");
            filter.Text = "";
            Check(windowsList.Items.Count == 2, "Le voci di sistema sono nascoste nella vista semplice");
            var systemCheck = (CheckBox)window.FindName("ShowSystemCheck");
            systemCheck.IsChecked = true;
            systemCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(windowsList.Items.Count == 3, "Le voci di sistema restano consultabili su richiesta");
            windowsList.SelectedIndex = 2;
            Check(!((Button)window.FindName("ImportButton")).IsEnabled && !((Button)window.FindName("ToggleStartupButton")).IsEnabled, "Le azioni non supportate sulle voci di sistema sono disabilitate");
            windowsList.SelectedIndex = 0;
            Render("windows-small", 900, 580);
            ((RadioButton)window.FindName("UpdatesNav")).IsChecked = true;
            Render("updates", 1180, 760);
            Check(((TextBlock)window.FindName("StatusText")).Visibility == Visibility.Collapsed, "Gli aggiornamenti non mostrano messaggi della sequenza");
            ((RadioButton)window.FindName("SequenceNav")).IsChecked = true;
            previewItems.Clear();
            Check(!((Button)window.FindName("RunButton")).IsEnabled, "Una sequenza vuota non propone un avvio senza effetto");
            Render("empty-small", 900, 580);
            var editor = new AppEditor(new LaunchItem { Name = "Discord", Path = first.Path, Health = "Configurazione OK", DelaySeconds = 30 }, _ => true);
            root = (FrameworkElement)editor.Content;
            Render("edit-app", 544, 600);
            FindVisual<Expander>(root)!.IsExpanded = true;
            Render("edit-advanced", 544, 600);
            editor.Close();
            var picker = new AppPicker([new AvailableApp("Discord", first.Path), new AvailableApp("Steam", second.Path)]);
            root = (FrameworkElement)picker.Content;
            Render("add-app", 544, 560);
            FindVisual<TextBox>(root)!.Text = "Steam";
            Check(FindVisual<ListBox>(root)!.Items.Count == 1, "Il selettore delle app consente la ricerca per nome");
            picker.Close();
            window.Close();
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw failure;
    Console.WriteLine("PASS: Rendering WPF delle tre pagine, stato vuoto e modifica app");
}

static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
{
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
    {
        var child = VisualTreeHelper.GetChild(root, i);
        if (child is T match) return match;
        if (FindVisual<T>(child) is T nested) return nested;
    }
    return null;
}

