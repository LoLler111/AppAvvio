# AppAvvio

App desktop per Windows in C# / .NET 10 / WPF.

## Uso quotidiano

1. Premi **Aggiungi app** e scegli i collegamenti del menu Start, cercandoli per nome. Puoi anche usare **Scegli un file** per aggiungere eseguibili e collegamenti.
2. Riordina le app trascinandole o usando le frecce.
3. Premi **Modifica** per scegliere un’attesa: subito, 10 secondi, 30 secondi, un minuto o un valore personalizzato. L’attesa parte dopo il precedente avvio; per la prima app parte all’inizio della sequenza. Non attende il caricamento completo del programma precedente.
4. Attiva **Apri la mia sequenza quando accedo a Windows**. Da **Opzioni** puoi scegliere se mostrare la finestra all’accesso.
5. Usa **Avvia adesso** per provare. **Interrompi** ferma le attese e gli avvii successivi; le app già aperte rimangono aperte.

Le modifiche vengono salvate automaticamente. L’interruttore di una riga mette in pausa quell’app. Percorso, opzioni di avvio e cartella di lavoro si trovano in **Modifica → Dettagli avanzati**.

## Avvio di Windows

La pagina **Avvio di Windows** mostra prima le voci gestibili. Ricerca e opzione per mostrare le voci di sistema permettono di consultare anche gli altri avvii.

**Sposta nella sequenza** conserva l’avvio originale, disattiva la voce Windows e attiva AppAvvio all’accesso. Una conferma spiega l’effetto prima della modifica. Per queste app, **Torna all’avvio di Windows** ripristina la voce originale e rimuove la riga dalla sequenza. Per le app aggiunte manualmente, **Togli dalla sequenza** non disinstalla il programma.

**Disattiva avvio** e **Riattiva avvio** agiscono sull’avvio Windows senza aggiungere l’app alla sequenza. I dati di ripristino sono conservati anche dopo la chiusura.

La ricerca viene eseguita in background. Gli avvisi rimangono separati dallo stato di esecuzione e non vengono sovrascritti dalla scansione. I controlli segnalano file mancanti, avvii originali riattivati o modificati e possibili duplicati; non applicano correzioni automatiche. Una scansione incompleta viene segnalata.

## Avvio discreto

Con l’opzione per non mostrare la finestra, all’accesso AppAvvio resta nell’area di notifica. Il menu dell’icona permette di aprire la finestra, interrompere la sequenza o uscire. Riaprendo AppAvvio dal menu Start viene mostrata l’istanza già attiva. Gli errori di avvio delle app producono una notifica quando la finestra è nascosta. Chiudere la finestra termina AppAvvio e interrompe le attese ancora in corso.

## Dati e limiti

Impostazioni e copia precedente si trovano in `%LOCALAPPDATA%\AppAvvio\Data`. Il salvataggio valida i dati prima di sostituire il file. Se il caricamento fallisce, l’app conserva i dati e blocca le modifiche.

La scansione include registro Run/RunOnce/policy nelle viste 32/64 bit, cartelle di avvio e attività pianificate per accensione/accesso. Le attività utente compatibili conservano l’XML originale e possono essere ripristinate se la definizione non è cambiata. Attività di Windows, elevate, al boot o con condizioni particolari restano consultabili con gestione manuale.

Servizi e StartupTask delle app Store non sono elencati. Il selettore cerca i collegamenti del menu Start, non costituisce un inventario completo di tutte le app installate. Per altre app si può scegliere un collegamento. Il controllo dei processi già attivi funziona per eseguibili con percorso identico, non per collegamenti, launcher e app Store. La presenza di un collegamento non garantisce che la sua destinazione sia ancora valida.

## Installazione e aggiornamenti

Scaricare `AppAvvio-win-Setup.exe` dalle [release](https://github.com/LoLler111/AppAvvio/releases/latest). Nell’app usare **Aggiornamenti → Controlla aggiornamenti → Scarica e installa**. Dopo download e verifica del pacchetto l’app si riavvia. Impostazioni e backup restano separati dai binari. Le build di sviluppo leggono le novità ma non si aggiornano automaticamente.

I pacchetti non hanno una firma Authenticode; Velopack gestisce la verifica di integrità.

## Sviluppo e verifica

- Avvio: `dotnet run --project AppAvvio.csproj`.
- Test: `dotnet run --project tests/AppAvvio.Tests.csproj -c Release`.
- Test e anteprime WPF: aggiungere `-- --render`. Le immagini sono in `artifacts/screenshots`; le anteprime usano dati di prova senza salvare impostazioni personali.
- Integrazione Task Scheduler: aggiungere `-- --scheduler-integration`. Crea e rimuove un’attività temporanea, senza eseguirne il comando.
- Pacchetto locale: `./scripts/release.ps1 -Version 0.6.0`.
- Pubblicazione: aggiornare versione e note, quindi pubblicare il tag `vX.Y.Z`. Il workflow crea installer e feed. Conservare feed e pacchetti nupkg nelle release.

Verifiche manuali per una versione installata: aggiunta e riapertura, salvataggio in caso di errore del disco, avvio al login con e senza finestra, riapertura dall’icona e dal menu Start, interruzione durante l’attesa, aggiornamento e riavvio, ridimensionamento e navigazione da tastiera.
