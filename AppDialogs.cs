using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AppAvvio;

public sealed class AppEditor : Window
{
    public AppEditor(LaunchItem item, Func<LaunchItem, bool> save)
    {
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
        Title = "Modifica " + item.Name;
        Width = 560; Height = 640; MinWidth = 460; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24), Background = System.Windows.Media.Brushes.White };
        Content = new Border { Background = System.Windows.Media.Brushes.White, Child = root };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var cancel = new Button { Content = "Annulla", IsCancel = true };
        var apply = new Button { Content = "Salva modifiche", IsDefault = true, Style = (Style)FindResource("Primary") };
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "Come vuoi avviare questa app?", FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        TextBox Field(Panel parent, string label, string value)
        {
            parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,16,0,6) });
            var box = new TextBox { Text = value }; System.Windows.Automation.AutomationProperties.SetName(box, label); parent.Children.Add(box); return box;
        }
        var name = Field(panel, "Nome dell’app", item.Name);
        panel.Children.Add(new TextBlock { Text = "Quanto deve aspettare?", Margin = new Thickness(0,16,0,6) });
        var choices = new[] { 0, 10, 30, 60, -1 };
        var delay = new ComboBox { ItemsSource = new[] { "Subito", "10 secondi", "30 secondi", "1 minuto", "Scegli un’altra attesa" }, Padding = new Thickness(8) };
        System.Windows.Automation.AutomationProperties.SetName(delay, "Attesa prima dell’avvio");
        delay.SelectedIndex = Array.IndexOf(choices, item.DelaySeconds);
        if (delay.SelectedIndex < 0) delay.SelectedIndex = 4;
        panel.Children.Add(delay);
        var customPanel = new StackPanel(); panel.Children.Add(customPanel);
        var seconds = Field(customPanel, "Attesa in secondi (da 0 a 86400)", item.DelaySeconds.ToString());
        void ShowCustom() => customPanel.Visibility = delay.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        delay.SelectionChanged += (_, _) => ShowCustom(); ShowCustom();
        panel.Children.Add(new TextBlock { Text = "L’attesa parte dopo l’avvio dell’app precedente. Per la prima app, parte all’inizio della sequenza.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        if (item.Health != "Configurazione OK") panel.Children.Add(new TextBlock { Text = "Da controllare: " + item.Health, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,0) });
        var advanced = new StackPanel();
        panel.Children.Add(new Expander { Header = "Dettagli avanzati", Content = advanced, Margin = new Thickness(0,20,0,0) });
        var path = Field(advanced, "File o collegamento da aprire", item.Path);
        var browse = new Button { Content = "Scegli un file…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
        browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "App e collegamenti|*.exe;*.lnk;*.appref-ms", DereferenceLinks = false }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; };
        advanced.Children.Add(browse);
        var arguments = Field(advanced, "Opzioni di avvio (facoltative)", item.Arguments);
        var directory = Field(advanced, "Cartella di lavoro (facoltativa)", item.WorkingDirectory);
        if (item.OriginalStartup != null) advanced.Children.Add(new TextBlock { Text = "Avvio originale conservato: " + item.OriginalStartup.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0,12,0,0) }; panel.Children.Add(error);
        apply.Click += (_, _) =>
        {
            var value = choices[delay.SelectedIndex];
            if (value < 0 && !int.TryParse(seconds.Text, out value) || value < 0 || value > 86400) { error.Text = "Inserisci un’attesa da 0 a 86400 secondi."; return; }
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(path.Text)) { error.Text = "Inserisci il nome e scegli il file dell’app."; return; }
            var edited = new LaunchItem { Name = name.Text.Trim(), Path = path.Text.Trim(), DelaySeconds = value, Arguments = arguments.Text, WorkingDirectory = directory.Text.Trim(), Enabled = item.Enabled, OriginalStartup = item.OriginalStartup };
            if (save(edited)) DialogResult = true;
            else error.Text = "Non riesco a salvare. Le modifiche precedenti sono state conservate. Riprova o annulla.";
        };
    }
}

public sealed record AvailableApp(string Name, string Path)
{
    public System.Windows.Media.ImageSource? Icon => AppIcons.Get(Path);
}

public static class AppDiscovery
{
    public static List<AvailableApp> Read()
    {
        var apps = new List<AvailableApp>();
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root)) continue;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var path in Directory.EnumerateFiles(root, "*", options).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".lnk" or ".appref-ms"))
                    apps.Add(new AvailableApp(Path.GetFileNameWithoutExtension(path), path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return apps.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public sealed class AppPicker : Window
{
    public List<AvailableApp> Selected { get; private set; } = [];
    public AppPicker(IEnumerable<AvailableApp>? initialApps = null)
    {
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
        Title = "Aggiungi le tue app"; Width = 560; Height = 600; MinWidth = 440; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24), Background = System.Windows.Media.Brushes.White }; Content = new Border { Background = System.Windows.Media.Brushes.White, Child = root };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Quali app usi ogni giorno?", FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Scegli tra i collegamenti del menu Start oppure cerca un file.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,16) });
        heading.Children.Add(new TextBlock { Text = "Cerca per nome", Margin = new Thickness(0,0,0,6) });
        var search = new TextBox(); heading.Children.Add(search); System.Windows.Automation.AutomationProperties.SetName(search, "Cerca un’app");
        var status = new TextBlock { Text = "Sto cercando le app…", Margin = new Thickness(0,10,0,10), TextWrapping = TextWrapping.Wrap }; heading.Children.Add(status);
        var buttons = new WrapPanel { Margin = new Thickness(0,16,0,0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var browse = new Button { Content = "Scegli un file…" }; var cancel = new Button { Content = "Annulla", IsCancel = true }; var add = new Button { Content = "Aggiungi", IsEnabled = false, Style = (Style)FindResource("Primary") };
        buttons.Children.Add(browse); buttons.Children.Add(cancel); buttons.Children.Add(add);
        var list = new ListBox { ItemTemplate = (DataTemplate)FindResource("AvailableAppTemplate"), SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0) }; root.Children.Add(list);
        List<AvailableApp> all = initialApps?.ToList() ?? [];
        void Filter() { var filtered = all.Where(x => x.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList(); list.ItemsSource = filtered; status.Text = filtered.Count == 0 ? "Nessuna app trovata. Puoi usare Scegli un file." : $"{filtered.Count} app · usa Ctrl per sceglierne più di una"; }
        search.TextChanged += (_, _) => Filter();
        if (initialApps != null) Filter();
        else Loaded += async (_, _) => { try { all = await Task.Run(AppDiscovery.Read); Filter(); search.Focus(); } catch { status.Text = "Non riesco a leggere il menu Start. Usa Scegli un file."; } };
        list.SelectionChanged += (_, _) => add.IsEnabled = list.SelectedItems.Count > 0;
        void Accept() { if (list.SelectedItems.Count == 0) return; Selected = list.SelectedItems.Cast<AvailableApp>().ToList(); DialogResult = true; }
        add.Click += (_, _) => Accept(); list.MouseDoubleClick += (_, e) => { if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem) Accept(); };
        browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "App e collegamenti|*.exe;*.lnk;*.appref-ms", Multiselect = true, DereferenceLinks = false }; if (dialog.ShowDialog(this) != true) return; Selected = dialog.FileNames.Select(p => new AvailableApp(Path.GetFileNameWithoutExtension(p), p)).ToList(); DialogResult = true; };
    }
}
