using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AppAvvio;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LaunchItem> items = [];
    private CancellationTokenSource? running;
    private Point dragOrigin;
    private bool loadFailed;
    public MainWindow()
    {
        InitializeComponent();
        AppsGrid.ItemsSource = items;
        try
        {
            var config = SettingsStore.Load();
            foreach (var item in config.Items) items.Add(item);
            RepositoryBox.Text = config.Repository;
            LoginCheck.IsChecked = StartupService.IsRegistered();
            var cache = Path.Combine(SettingsStore.Folder, "changelog.txt");
            if (File.Exists(cache)) Changelog.Text = File.ReadAllText(cache);
        }
        catch (Exception ex) { loadFailed = true; StatusText.Text = "Configurazione non caricata: " + ex.Message; }
        Closing += (_, _) => running?.Cancel();
    }
    private void AddApp(object sender, RoutedEventArgs e)
    {
        if (running != null) return;
        var dialog = new OpenFileDialog { Filter = "Applicazioni e collegamenti|*.exe;*.lnk;*.appref-ms", Multiselect = true, DereferenceLinks = false };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) items.Add(new LaunchItem { Name = Path.GetFileNameWithoutExtension(path), Path = path });
        StatusText.Text = "Imposta ordine e attese, poi salva. Per le app Store puoi aggiungere un collegamento.";
    }
    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int offset)
    {
        if (running != null) return;
        var index = AppsGrid.SelectedIndex;
        if (index >= 0 && index + offset >= 0 && index + offset < items.Count) items.Move(index, index + offset);
    }
    private void RemoveApp(object sender, RoutedEventArgs e) { if (running == null && AppsGrid.SelectedItem is LaunchItem item) items.Remove(item); }
    private bool SaveConfiguration()
    {
        if (loadFailed) { StatusText.Text = "Salvataggio bloccato per conservare la configurazione non leggibile. Controlla " + SettingsStore.FilePath; return false; }
        if (!AppsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !AppsGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        if (items.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Path) || x.DelaySeconds < 0 || x.DelaySeconds > 86400))
        { StatusText.Text = "Ogni app richiede nome, percorso e un’attesa fra 0 e 86400 secondi."; return false; }
        try { SettingsStore.Save(new Configuration { Items = items.ToList(), Repository = RepositoryBox.Text.Trim() }); StatusText.Text = "Configurazione salvata."; return true; }
        catch (Exception ex) { StatusText.Text = "Salvataggio fallito: " + ex.Message; return false; }
    }
    private void Save(object sender, RoutedEventArgs e) => SaveConfiguration();
    private async void Run(object sender, RoutedEventArgs e) => await RunSequence();
    private async Task RunSequence()
    {
        if (running != null || !SaveConfiguration()) return;
        running = new CancellationTokenSource();
        AppsGrid.IsEnabled = false;
        RunButton.IsEnabled = false;
        try { StatusText.Text = "Sequenza in esecuzione…"; await StartupService.RunAsync(items.ToList(), running.Token); StatusText.Text = "Sequenza terminata. Controlla lo stato di ogni applicazione."; }
        catch (OperationCanceledException) { StatusText.Text = "Sequenza interrotta. Le app già avviate restano aperte."; }
        finally { running.Dispose(); running = null; AppsGrid.IsEnabled = true; RunButton.IsEnabled = true; }
    }
    private void Stop(object sender, RoutedEventArgs e) => running?.Cancel();
    private void ToggleLogin(object sender, RoutedEventArgs e)
    {
        try { if (LoginCheck.IsChecked == true && !SaveConfiguration()) { LoginCheck.IsChecked = false; return; } StartupService.Register(LoginCheck.IsChecked == true); }
        catch (Exception ex) { StatusText.Text = "Impossibile modificare l’avvio: " + ex.Message; LoginCheck.IsChecked = StartupService.IsRegistered(); }
    }
    private void CheckConflicts(object sender, RoutedEventArgs e) => ShowConflicts();
    private bool ShowConflicts()
    {
        try
        {
            var conflicts = StartupService.FindConflicts(items);
            if (conflicts.Count == 0) { StatusText.Text = "Nessuna corrispondenza nelle voci Run e nelle cartelle di avvio. Attività pianificate e servizi non sono ancora controllati."; return false; }
            MessageBox.Show(this, "Possibili avvii autonomi da verificare nelle impostazioni Windows o nell’app:\n\n" + string.Join("\n", conflicts) + "\n\nIl rilevamento è basato sul nome: una voce potrebbe essere già disabilitata in Windows.", "Controllo avvio", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) { StatusText.Text = "Controllo non riuscito: " + ex.Message; return true; }
    }
    public async void StartAtLogin()
    {
        if (loadFailed) return;
        ShowConflicts();
        await RunSequence();
    }
    private void OpenStartupSettings(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void DragStart(object sender, MouseButtonEventArgs e) => dragOrigin = e.GetPosition(AppsGrid);
    private void DragMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(AppsGrid);
        if (running == null && e.LeftButton == MouseButtonState.Pressed && Math.Abs(point.Y - dragOrigin.Y) > SystemParameters.MinimumVerticalDragDistance && AppsGrid.SelectedItem is LaunchItem item)
            DragDrop.DoDragDrop(AppsGrid, item, DragDropEffects.Move);
    }
    private void DropItem(object sender, DragEventArgs e)
    {
        if (running != null || e.Data.GetData(typeof(LaunchItem)) is not LaunchItem item) return;
        var row = ItemsControl.ContainerFromElement(AppsGrid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row?.Item is LaunchItem target && target != item) items.Move(items.IndexOf(item), items.IndexOf(target));
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        var repo = RepositoryBox.Text.Trim();
        if (!Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) { StatusText.Text = "Inserisci il repository nel formato proprietario/nome."; return; }
        if (!SaveConfiguration()) return;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AppAvvio/0.1.0");
            var json = await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=20");
            using var document = JsonDocument.Parse(json);
            var notes = document.RootElement.EnumerateArray().Where(x => !x.GetProperty("draft").GetBoolean()).Select(x =>
                $"{x.GetProperty("tag_name").GetString()} — {x.GetProperty("published_at").GetString()}\n\n{x.GetProperty("body").GetString()}");
            var text = string.Join("\n\n────────────────────\n\n", notes);
            Changelog.Text = string.IsNullOrWhiteSpace(text) ? "Nessuna release pubblicata." : text;
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(Path.Combine(SettingsStore.Folder, "changelog.txt"), Changelog.Text);
            StatusText.Text = "Note delle release aggiornate. Installazione automatica non ancora disponibile in questa anteprima.";
        }
        catch (Exception ex) { StatusText.Text = "Impossibile recuperare le release. Lo storico precedente resta disponibile. " + ex.Message; }
    }
}
