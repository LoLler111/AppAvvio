using System.Windows;

namespace AppAvvio;

public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        Velopack.VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\AppAvvio-" + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("AppAvvio è già in esecuzione."); Shutdown(); return; }
        if (StartupService.IsRegistered()) StartupService.Register(true);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Contains("--startup")) window.StartAtLogin();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
