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
