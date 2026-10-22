using AppAvvio;

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
