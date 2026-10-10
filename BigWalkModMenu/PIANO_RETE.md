# Piano — aspetto condiviso tra amici (rete)

Stato (2026-10-10): **tutti i passi sono scritti, compilati e installati (menu 1.1.0, facce 3.1.0). Niente della
parte di rete è stato provato in gioco**: serve una partita con almeno un amico, host con la mod.
Pacchetto: `../dist/BigWalkModMenu-1.3.0.zip` (senza foto).

Provato fuori dal gioco:
- `Wire`: andata e ritorno, sforamenti, testo non valido, 300.000 messaggi casuali → nessuna eccezione.
- `ChunkAssembler`: pezzi fuori ordine, pezzi sbagliati, file corrotto rifiutato dall'hash.
- Immagini: le 5 facce vere a 256 px pesano 8-9,4 KB a qualità 85.
- **Trovato e chiuso**: il decodificatore JPEG (StbImageSharp) gira all'infinito su un'intestazione con lunghezze
  inventate. Ora si accettano solo JPEG con l'intestazione identica a quella del nostro codificatore
  (`Images.IsOwnJpeg`), senza marcatori nei dati: 18.000 file danneggiati, 0 blocchi.
  Attenzione: sopra qualità 90 il codificatore cambia schema e il controllo rifiuta il file. Si usa ≤ 85.

Rilettura di sicurezza (metodo della skill: da dove entra il dato, dove finisce): trovati e corretti la raffica di
Hello che faceva rimandare tutto lo stato (ora al massimo ogni 10 s), il ritmo dei pezzi d'immagine che superava
il limite dell'host (ora ~15/s), i pezzi non rimandati quando una faccia riappare.

Da verificare in gioco: aggancio al gestore voce (da soli e con smolMods.Core), che la voce continui a funzionare,
il saluto, l'inoltro, `BufferLength` reale (scritto nel log come "Largest message"), ID di piattaforma valorizzato.

Scelte prese senza risposta esplicita dell'utente (si cambiano facilmente):
canale nostro indipendente; niente "l'host veste gli altri" per ora; emote e fumetti inclusi.

## Cosa passa in rete

| Dato | Peso | Quando |
|---|---|---|
| Aspetto (cappello, accessori, colori, proporzioni, naso, luce, scia) | ~80 byte | a ogni modifica, e ai nuovi arrivati |
| Faccia: regolazioni + immagine 256×256 JPEG | ~10-20 KB a pezzi | una volta per partita; poi resta salvata per ID |
| Emote e fumetti | pochi byte | al momento |

## Come viaggia

Tecnica di smolMods.Core/ColorSelf, verificata leggendo il loro codice: un pacchetto con l'ID Mirror della
chat vocale (8290, `DissonanceNetworkMessage` = `ushort lunghezza + byte`), riconosciuto da due byte di firma.
La nostra firma è diversa dalla loro (`B7 4D`, loro `B1 6D`).

- Client → host: `[firma][versione][tipo][corpo]`.
- Host → client: `[firma][versione][tipo][netId mittente][corpo]`. Il mittente lo scrive l'host dalla connessione.
- Saluto: il client manda Hello (max 4 tentativi); l'host risponde e da lì inoltra solo a chi ha salutato.
  Con un host senza mod non succede niente (la voce scarta il pacchetto).
- Messaggi "di stato" (aspetto, pezzi di faccia): l'host tiene l'ultimo per mittente e chiave e lo rimanda ai nuovi arrivati.

### Convivenza con smolMods.Core (rischio trovato in analisi)

Entrambe le mod sostituiscono `handlers[8290]` e la loro ricontrolla ogni secondo: se il gestore non è il suo, lo
ri-avvolge. Se noi avvolgiamo *sopra* il loro, loro ri-avvolgono sopra di noi e, avendo un solo campo statico per
"l'originale", nasce un ciclo loro → noi → loro: ricorsione infinita su ogni pacchetto voce.

Regola: non avvolgere mai un gestore che è il loro. Se il loro è già installato ci si inserisce *sotto* (nel loro
campo `_origServer`/`_origClient`, via reflection). Se la reflection non trova quei campi, il canale resta spento e
lo si dice all'utente. Senza smolMods.Core si sostituisce il gestore direttamente, una volta per sessione.

## Minacce e contromisure (lista di controllo da security-review: deserializzazione, file, autorizzazione)

| Minaccia | Contromisura |
|---|---|
| Dati malformati o enormi | Lettore con limiti: lunghezza massima per pacchetto, ogni campo controllato e riportato nei limiti, niente tipi dinamici |
| Bomba di immagini | Max 24 KB e 24 pezzi per faccia; decodifica solo PNG/JPEG in codice gestito; rifiuto oltre 512×512 |
| Raffica di messaggi | Sull'host, gettoni per connessione (30/s, raffica 60); chi sfora troppo viene ignorato per la sessione |
| Spacciarsi per un altro | netId messo dall'host; i client accettano solo ciò che arriva dalla connessione al server |
| Nomi di file dai dati | Mai: i file in cache si chiamano con l'ID di piattaforma (solo cifre) e l'hash calcolato da noi |
| Contenuto indesiderato | Interruttore "mostra l'aspetto degli amici" e blocco per giocatore |
| Rompere la voce | Tutto in try/catch: al primo dubbio il pacchetto passa all'originale; dopo 5 errori il canale si spegne |
| Versioni diverse | Versione nel pacchetto: se diversa, ignorato e avviso una volta |

Nessuna cifratura nostra: il trasporto Steam/Epic è già cifrato e autenticato; l'host è comunque fidato per forza.

## Passi

1. `Core/Wire.cs`: lettura/scrittura con limiti. Provato fuori dal gioco, anche con dati casuali.
2. `Core/Net.cs`: aggancio, saluto, inoltro, limiti, cache dell'host.
3. Identità: ID di piattaforma (`playerNetworking.userPlatformId`) e netId per ogni personaggio.
4. Personaggio: profilo "io" che segue il mio personaggio; profili ricevuti per gli altri. Con la Solo Mod restano gli slot.
5. Extra: emote e fumetti inviati a tutti.
6. Facce: la mia faccia (regolazioni + immagine ridotta) inviata a pezzi; cache su disco per ID.
7. Esporta/importa profilo su file, come riserva.
8. Rilettura di sicurezza con la skill sul codice che legge i pacchetti.

## Non provabile da solo

Con la Solo Mod i personaggi sono tutti locali: la rete non viene usata. Fuori dal gioco si provano il formato e
i limiti; l'aggancio e l'inoltro vanno provati con un amico vero.
