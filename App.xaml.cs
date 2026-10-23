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
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private System.Windows.Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var key = @"Local\AppAvvio-" + Environment.UserName;
        instance = new Mutex(true, key, out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting(key + "-activate"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("AppAvvio si sta aprendo. Riprova tra qualche secondo."); }
            Shutdown();
            return;
        }
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-activate");
        if (StartupService.IsRegistered()) StartupService.Register(true);
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        void OpenWindow()
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
        activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke((Action)OpenWindow), null, Timeout.Infinite, false);
        try
        {
            using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/Branding/appavvio.ico")).Stream;
            trayIcon = new System.Drawing.Icon(stream);
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Apri AppAvvio", null, (_, _) => OpenWindow());
            menu.Items.Add("Interrompi la sequenza", null, (_, _) => window.StopSequence());
            menu.Items.Add("Esci", null, (_, _) => window.Close());
            tray = new System.Windows.Forms.NotifyIcon { Icon = trayIcon, Text = "AppAvvio · le tue app, i tuoi tempi", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += (_, _) => OpenWindow();
            tray.BalloonTipClicked += (_, _) => OpenWindow();
            window.NotifyProblem = message =>
            {
                if (!window.IsVisible) tray.ShowBalloonTip(6000, "AppAvvio · c’è qualcosa da controllare", message, System.Windows.Forms.ToolTipIcon.Warning);
            };
        }
        catch { tray?.Dispose(); tray = null; } // Always keep an accessible window if the tray is unavailable.
        if (e.Args.Contains("--startup"))
        {
            if (!window.QuietStartup || tray == null) window.Show();
            window.StartAtLogin();
        }
        else window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activationWait?.Unregister(null);
        activation?.Dispose();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
