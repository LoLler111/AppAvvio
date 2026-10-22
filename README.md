# AppAvvio

Anteprima desktop Windows in C# / .NET 10 / WPF.

## Avvio di sviluppo

`dotnet run --project AppAvvio.csproj`

## Funzioni della prima versione

- Elenco di eseguibili e collegamenti, ordine tramite trascinamento o frecce, ritardi relativi, argomenti e abilitazione individuale.
- Salvataggio atomico con copia della configurazione precedente in `%LOCALAPPDATA%\AppAvvio\Data`.
- Esecuzione cancellabile; gli errori di un'app non bloccano le successive.
- Registrazione volontaria nell'avvio dell'utente e controllo indicativo delle voci Run/cartelle Startup.
- Interfaccia con navigazione laterale e pagina dedicata agli aggiornamenti.
- Lettura delle note delle release stabili di LoLler111/AppAvvio e cache locale.
- Installer Windows x64 con .NET incluso e aggiornamenti tramite Velopack.

## Limiti dell'anteprima

La schermata mostra le voci Run e i collegamenti nelle cartelle di avvio, filtrando gli stati disabilitati riconosciuti in StartupApproved. Trascinare una voce utente nella sequenza conserva il comando originale e disattiva la voce di Windows. Riportarla a Windows (o rimuoverla dalla sequenza) ripristina la voce originale. Le voci di sistema sono visibili ma richiedono gestione manuale. Attività pianificate, servizi e StartupTask delle app Store non sono ancora elencati. Il rilevamento dei conflitti resta basato sul nome. Il controllo dei processi già attivi funziona per eseguibili con percorso identico, non per collegamenti, launcher e app Store. La finestra è visibile anche all'accesso: modalità tray e notifiche sono da implementare.

## Installazione e aggiornamenti

Scaricare `AppAvvio-win-Setup.exe` dalle [release](https://github.com/LoLler111/AppAvvio/releases/latest). Dopo la prima installazione, aprire **Aggiornamenti → Controlla aggiornamenti → Scarica e installa**. L'app scarica e verifica il pacchetto, poi si riavvia. Non è consentito aggiornare durante una sequenza attiva. Impostazioni e backup restano nella cartella dati separata dai binari. Le build lanciate con `dotnet run` leggono il changelog ma non si aggiornano.

I pacchetti non sono attualmente firmati con un certificato Authenticode. La verifica di integrità è gestita da Velopack e la distribuzione avviene tramite HTTPS da questo repository.

## Pubblicare una versione

Aggiornare `Version` in `AppAvvio.csproj` e `release-notes.md`, poi creare e pubblicare un tag `vX.Y.Z`. Il workflow `.github/workflows/release.yml` esegue i test, compila il pacchetto self-contained e pubblica installer, feed e pacchetto nella stessa GitHub Release. Non eliminare feed e nupkg dalle release: servono al client di aggiornamento. È disponibile anche l'avvio manuale del workflow con numero di versione.

Per creare i pacchetti localmente: `./scripts/release.ps1 -Version 0.2.0`. Il tool vpk e il pacchetto Velopack sono fissati alla stessa versione.

Test: `dotnet run --project tests/AppAvvio.Tests.csproj -c Release`. Aggiungere `-- --render` per generare le anteprime WPF in `artifacts/screenshots`.

## Verifica manuale

1. Aggiungere due app innocue e impostare ritardi differenti; salvare e riaprire.
2. Riordinare con frecce e trascinamento; verificare la persistenza.
3. Provare la sequenza e interromperla durante un'attesa.
4. Provare un percorso inesistente: l'app successiva deve partire.
5. Controllare che un eseguibile già attivo non venga lanciato di nuovo.
6. Verificare il changelog con un repository pubblico con release.

Non sono state apportate modifiche all'avvio di Windows durante lo sviluppo.
