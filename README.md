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

La schermata mostra registro (Run, RunOnce e policy nelle viste 32/64 bit), cartelle di avvio e attività pianificate per accensione/accesso, anche nascoste e nelle sottocartelle. Le voci disabilitate riconosciute vengono escluse. RunOnce, policy e voci di sistema restano consultabili con gestione manuale.

Le attività utente con una sola azione eseguibile e un solo trigger di accesso, senza elevazione o condizioni particolari, possono essere trasferite: viene conservato l'XML nel backup della configurazione, l'attività viene disabilitata e il comando viene eseguito dalla sequenza con argomenti e cartella di lavoro. Il ripristino riabilita l'attività solo se la sua definizione è invariata. Attività di Windows, al boot, elevate o con temporizzazioni/condizioni particolari restano visibili con il motivo della gestione manuale. Gli errori di accesso vengono segnalati come scansione parziale.

Servizi e StartupTask delle app Store non sono ancora elencati. Il controllo dei processi già attivi funziona per eseguibili con percorso identico, non per collegamenti, launcher e app Store. La finestra è visibile anche all'accesso: modalità tray e notifiche sono da implementare.

Test della scansione e del ripristino reale del Task Scheduler: `dotnet run --project tests/AppAvvio.Tests.csproj -c Release -- --scheduler-integration`. Crea una cartella/attività temporanea, senza eseguire il comando, e la rimuove a fine prova.


## Controlli e disattivazione diretta (0.5.0)

All'apertura, con **Aggiorna elenco**, **Controlla conflitti** e **Controlla aggiornamenti**, la sequenza viene confrontata con le origini accessibili. La colonna Configurazione indica percorsi mancanti, voci originali riattivate o modificate, attività originali eliminate e possibili avvii equivalenti in un'altra origine. Una scansione incompleta non viene dichiarata valida. Lo stato di esecuzione rimane separato e non nasconde questi avvisi. Il controllo verifica la presenza del file o collegamento configurato, non la destinazione interna di un collegamento.

Selezionare una voce gestibile nell'elenco Windows e premere **Disattiva** per fermarne l'avvio automatico senza aggiungerla alla sequenza. Le voci disattivate da AppAvvio rimangono visibili e si possono ripristinare con **Riattiva**, anche dopo la riapertura. Comandi, collegamenti e definizioni delle attività restano nel backup della configurazione. Voci di sistema o con vincoli non convertibili richiedono gestione manuale. I controlli non applicano correzioni automatiche.

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
