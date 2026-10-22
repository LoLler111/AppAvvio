# AppAvvio

Anteprima desktop Windows in C# / .NET 10 / WPF.

## Avvio di sviluppo

`dotnet run --project AppAvvio.csproj`

## Funzioni della prima versione

- Elenco di eseguibili e collegamenti, ordine tramite trascinamento o frecce, ritardi relativi, argomenti e abilitazione individuale.
- Salvataggio atomico con copia della configurazione precedente in `%LOCALAPPDATA%\AppAvvio\Data`.
- Esecuzione cancellabile; gli errori di un'app non bloccano le successive.
- Registrazione volontaria nell'avvio dell'utente e controllo indicativo delle voci Run/cartelle Startup.
- Lettura delle note di GitHub Releases per repository pubblici e cache locale.

## Limiti dell'anteprima

Il rilevamento dei conflitti usa il nome e può includere voci disabilitate da Windows. Non gestisce ancora StartupApproved, attività pianificate, servizi o ripristino delle voci originali. Non disabilita automaticamente altre applicazioni. Il controllo dei processi già attivi funziona per eseguibili con percorso identico, non per collegamenti, launcher e app Store. La finestra è visibile anche all'accesso: modalità tray e notifiche sono da implementare.

Installer, aggiornamenti installabili e pipeline GitHub saranno aggiunti dopo aver definito il repository e il canale di distribuzione. Non attivare l'avvio automatico dalla cartella di build se prevedi di spostarla.

## Verifica manuale

1. Aggiungere due app innocue e impostare ritardi differenti; salvare e riaprire.
2. Riordinare con frecce e trascinamento; verificare la persistenza.
3. Provare la sequenza e interromperla durante un'attesa.
4. Provare un percorso inesistente: l'app successiva deve partire.
5. Controllare che un eseguibile già attivo non venga lanciato di nuovo.
6. Verificare il changelog con un repository pubblico con release.

Non sono state apportate modifiche all'avvio di Windows durante lo sviluppo.
