## Novità
- Controllo della sequenza a ogni apertura e con Aggiorna elenco / Controlla conflitti.
- Controllo eseguito anche premendo Controlla aggiornamenti.
- Colonna Configurazione separata dallo stato di esecuzione.
- Segnalazioni per file mancanti, avvii Windows riattivati, comandi cambiati e attività originali eliminate.
- Rilevamento di possibili avvii equivalenti in altre origini supportate.
- Pulsanti Disattiva / Riattiva direttamente nell'elenco Windows, senza inserire una app nella sequenza.
- Backup delle voci disattivate e persistenza del loro ripristino dopo la riapertura.

## Comportamento
- I controlli non correggono automaticamente le configurazioni: segnalano i casi da verificare.
- Le voci disattivate da AppAvvio restano nell'elenco e possono essere riattivate.
- La disattivazione usa gli stessi limiti di compatibilità delle voci importabili.
- Le scansioni parziali sono indicate esplicitamente.
- File e collegamenti sono verificati per la presenza del percorso configurato; non si garantisce che un collegamento punti ancora a una destinazione valida.
- Servizi e StartupTask Store restano fuori dalla scansione.
