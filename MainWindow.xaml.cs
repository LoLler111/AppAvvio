using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Velopack;
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
    private readonly UpdateService updates = new();
    private UpdateInfo? availableUpdate;
    private bool updateBusy;
    private Point windowsDragOrigin;
    private readonly ObservableCollection<StartupEntry> windowsItems = [];
    public MainWindow()
    {
        InitializeComponent();
        AppsGrid.ItemsSource = items;
        WindowsApps.ItemsSource = windowsItems;
        try
        {
            var config = SettingsStore.Load();
            foreach (var item in config.Items) items.Add(item);
            VersionText.Text = "Versione installata: " + UpdateService.CurrentVersion;
            SidebarVersion.Text = "Versione " + UpdateService.CurrentVersion;
            LoginCheck.IsChecked = StartupService.IsRegistered();
            var cache = Path.Combine(SettingsStore.Folder, "changelog.txt");
            if (File.Exists(cache)) Changelog.Text = File.ReadAllText(cache);
        }
        catch (Exception ex) { loadFailed = true; StatusText.Text = "Configurazione non caricata: " + ex.Message; }
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        items.CollectionChanged += (_, _) => EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshWindows(this, new RoutedEventArgs());
        Closing += (_, e) => { if (updateBusy) { e.Cancel = true; StatusText.Text = "Attendi il completamento dell’operazione."; } else running?.Cancel(); };
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
    private void RemoveApp(object sender, RoutedEventArgs e)
    {
        if (running != null || updateBusy || AppsGrid.SelectedItem is not LaunchItem item) return;
        ReturnItem(item);
    }
    private void RefreshWindows(object sender, RoutedEventArgs e)
    {
        try { windowsItems.Clear(); foreach (var entry in StartupCatalog.Read()) windowsItems.Add(entry); }
        catch (Exception ex) { StatusText.Text = "Elenco incompleto: " + ex.Message; }
    }
    private void ImportSelected(object sender, RoutedEventArgs e)
    {
        if (WindowsApps.SelectedItem is StartupEntry entry) ImportEntry(entry);
    }
    private void ImportEntry(StartupEntry entry)
    {
        if (running != null || updateBusy || !SaveConfiguration()) return;
        LaunchItem? item = null;
        try
        {
            if (items.Any(x => x.OriginalStartup?.Name == entry.Name && x.OriginalStartup.Source == entry.Source))
                throw new InvalidOperationException("App già nella sequenza. La voce originale è ricomparsa: controlla la configurazione prima di importarla nuovamente.");
            item = StartupCatalog.Prepare(entry);
            items.Add(item);
            if (!SaveConfiguration()) { items.Remove(item); return; }
            StartupService.Register(true);
            LoginCheck.IsChecked = true;
            StartupCatalog.Disable(entry);
            StatusText.Text = entry.Name + " ora è nella tua sequenza. L’avvio originale è conservato per il ripristino.";
        }
        catch (Exception ex)
        {
            if (item != null) { items.Remove(item); SaveConfiguration(); }
            StatusText.Text = "Spostamento non completato: " + ex.Message;
        }
        RefreshWindows(this, new RoutedEventArgs());
    }
    private void ReturnItem(LaunchItem item)
    {
        if (!SaveConfiguration()) return;
        try
        {
            if (item.OriginalStartup != null) StartupCatalog.Restore(item.OriginalStartup);
            items.Remove(item);
            if (!SaveConfiguration()) { items.Add(item); return; }
            RefreshWindows(this, new RoutedEventArgs());
            StatusText.Text = item.OriginalStartup == null ? "App rimossa dalla sequenza." : "Avvio originale ripristinato per " + item.Name;
        }
        catch (Exception ex) { StatusText.Text = "Ripristino non completato: " + ex.Message; }
    }
    private void WindowsDragStart(object sender, MouseButtonEventArgs e) => windowsDragOrigin = e.GetPosition(WindowsApps);
    private void WindowsDragMove(object sender, MouseEventArgs e)
    {
        if (running == null && !updateBusy && e.LeftButton == MouseButtonState.Pressed && Math.Abs(e.GetPosition(WindowsApps).Y - windowsDragOrigin.Y) > SystemParameters.MinimumVerticalDragDistance && WindowsApps.SelectedItem is StartupEntry entry)
            DragDrop.DoDragDrop(WindowsApps, entry, DragDropEffects.Move);
    }
    private void ReturnToWindows(object sender, DragEventArgs e)
    {
        if (running == null && !updateBusy && e.Data.GetData(typeof(LaunchItem)) is LaunchItem item && item.OriginalStartup != null) ReturnItem(item);
        e.Handled = true;
    }
    private bool SaveConfiguration()
    {
        if (loadFailed) { StatusText.Text = "Salvataggio bloccato per conservare la configurazione non leggibile. Controlla " + SettingsStore.FilePath; return false; }
        if (!AppsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !AppsGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        if (items.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Path) || x.DelaySeconds < 0 || x.DelaySeconds > 86400))
        { StatusText.Text = "Ogni app richiede nome, percorso e un’attesa fra 0 e 86400 secondi."; return false; }
        try { SettingsStore.Save(new Configuration { Items = items.ToList(), Repository = UpdateService.Repository }); StatusText.Text = "Configurazione salvata."; return true; }
        catch (Exception ex) { StatusText.Text = "Salvataggio fallito: " + ex.Message; return false; }
    }
    private void Save(object sender, RoutedEventArgs e) => SaveConfiguration();
    private async void Run(object sender, RoutedEventArgs e) => await RunSequence();
    private async Task RunSequence()
    {
        if (running != null || updateBusy || !SaveConfiguration()) return;
        running = new CancellationTokenSource();
        AppsGrid.IsEnabled = false;
        RunButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        try { StatusText.Text = "Sequenza in esecuzione…"; await StartupService.RunAsync(items.ToList(), running.Token); StatusText.Text = "Sequenza terminata. Controlla lo stato di ogni applicazione."; }
        catch (OperationCanceledException) { StatusText.Text = "Sequenza interrotta. Le app già avviate restano aperte."; }
        finally { running.Dispose(); running = null; AppsGrid.IsEnabled = true; RunButton.IsEnabled = true; InstallUpdateButton.IsEnabled = availableUpdate != null && !updateBusy; }
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
        if (e.Data.GetData(typeof(StartupEntry)) is StartupEntry entry) { ImportEntry(entry); e.Handled = true; return; }
        if (running != null || e.Data.GetData(typeof(LaunchItem)) is not LaunchItem item) return;
        var row = ItemsControl.ContainerFromElement(AppsGrid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row?.Item is LaunchItem target && target != item) items.Move(items.IndexOf(item), items.IndexOf(target));
    }
    private void ShowSequence(object sender, RoutedEventArgs e)
    {
        if (SequencePage == null) return;
        SequencePage.Visibility = Visibility.Visible;
        UpdatesPage.Visibility = Visibility.Collapsed;
    }
    private void ShowUpdates(object sender, RoutedEventArgs e)
    {
        if (UpdatesPage == null) return;
        SequencePage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Visible;
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        if (updateBusy) return;
        updateBusy = true;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        availableUpdate = null;
        UpdateStatus.Text = "Controllo delle release di AppAvvio…";
        try
        {
            try { Changelog.Text = await updates.LoadChangelogAsync(); }
            catch (Exception ex) { StatusText.Text = "Storico non aggiornato: " + ex.Message; }
            if (!updates.IsInstalled)
            {
                UpdateStatus.Text = "Versione di sviluppo: installa AppAvvio con il Setup della release per abilitare gli aggiornamenti.";
                return;
            }
            availableUpdate = await updates.CheckAsync();
            UpdateStatus.Text = availableUpdate == null ? "AppAvvio è aggiornato."
                : $"Disponibile la versione {availableUpdate.TargetFullRelease.Version}. Leggi le novità, poi scarica e installa.";
        }
        catch (Exception ex) { UpdateStatus.Text = "Controllo non riuscito. Riprova più tardi. " + ex.Message; }
        finally
        {
            updateBusy = false;
            CheckUpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = availableUpdate != null && running == null;
        }
    }
    private async void InstallUpdate(object sender, RoutedEventArgs e)
    {
        if (updateBusy || running != null || availableUpdate == null || !SaveConfiguration()) return;
        updateBusy = true;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        SequencePage.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<int>(value =>
        {
            DownloadProgress.Value = value;
            UpdateStatus.Text = $"Download e verifica dell’aggiornamento… {value}%";
        });
        try
        {
            await updates.DownloadAsync(availableUpdate, value => ((IProgress<int>)progress).Report(value));
            UpdateStatus.Text = "Installazione e riavvio in corso…";
            updateBusy = false;
            updates.ApplyAndRestart(availableUpdate);
        }
        catch (Exception ex) { UpdateStatus.Text = "Aggiornamento non completato. Puoi riprovare. " + ex.Message; }
        finally
        {
            updateBusy = false;
            CheckUpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = availableUpdate != null;
            RunButton.IsEnabled = true;
            SequencePage.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }
}
