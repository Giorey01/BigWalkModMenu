# Piano operativo — Big Walk Mod Menu

Stato (2026-10-10): **tutte le fasi sono scritte, compilate e installate; nessuna è ancora provata in gioco.**
Il codice è in `../` (cartella `BigWalkModMenu/`), il giornale con i fatti verificati e la lista delle prove in `../MODLOG.md`.

Differenze rispetto al piano: niente filtro "fumetto" (servono shader nostri); le texture sul corpo sono diventate
"stampa sul busto" nella scheda Facce; luce e scia sono nel modulo Personaggio.

Deciso finora (2026-10-10):
- Un menu base modulare; ogni mod è una DLL a parte che si registra nel menu.
- Niente rete tra mod (tutto è locale: lo vede solo chi ha la mod).
- Replay e registrazione della partita: sì.
- Niente cheat.

## Regole di lavoro

- Ogni fase finisce con una prova in gioco dell'utente; non si passa alla successiva prima della conferma.
- Backup di DLL e sorgenti prima di ogni fase.
- Ogni modulo si può togliere cancellando la sua DLL, senza rompere gli altri.
- Ogni fatto scoperto sul gioco va in `MODLOG.md`.

## Fase 0 — Indagine sul gioco (sola lettura)

Serve a trasformare le stime in certezze. Non cambia niente nel gioco.

- Grafica: impostazioni di qualità, asset URP (scala di rendering, ombre, antialiasing), effetti a schermo.
- Telecamera: chi la muove, come si può staccare dal giocatore.
- Personaggio: elenco ossa, renderer del corpo, come vengono applicati i colori, chi sovrascrive le scale.
- Mondo: sole, cielo, nebbia, eventuale ciclo giorno/notte.
- Replay: cosa si muove nel mondo oltre ai giocatori (oggetti, porte, puzzle) e come è sincronizzato.
- Interfaccia: come nasconderla.

Risultato: una sezione "Fatti verificati" in `MODLOG.md` e la conferma (o correzione) delle difficoltà sotto.

## Fase 1 — Menu base

Plugin `BigWalk.ModMenu`.

- Tasto F, blocco di visuale e movimento, cursore.
- Finestra con una scheda per modulo; registrazione dei moduli all'avvio.
- Controlli pronti: slider, interruttore, tendina, ruota dei colori, tasto rapido configurabile.
- Salvataggio impostazioni per modulo.
- Integrazione con la Solo Mod (scelta dello slot) spostata qui.
- La mod delle facce diventa il primo modulo, senza perdere facce e regolazioni esistenti.

Prova: il menu fa tutto quello che fa oggi.

## Fase 2 — Personaggio

- Colori personalizzati di corpo, mani, piedi (ruota HSV).
- Proporzioni: testa, mani, piedi, gambe, corpo.
- Forme del naso.
- Cappelli e accessori: prima PNG piatti (occhiali, baffi), poi forme 3D semplici.

## Fase 3 — Telecamera

- Selfie su un tasto.
- Modalità foto: camera libera, campo visivo, inclinazione, interfaccia nascosta, PNG ad alta risoluzione.
- Pulizia schermo: nascondi interfaccia, bande nere.
- Album delle foto scattate.

## Fase 4 — Prestazioni

- Diagnostica: FPS, grafico del tempo per frame, CPU o GPU, memoria.
- Regolazioni: scala di rendering, ombre, distanza dei dettagli, distanza visiva, antialiasing, effetti a schermo, limite FPS.
- Profili: Qualità, Bilanciato, Patata, Personale.

## Fase 5 — Mondo e filtri

- Ora del giorno, nebbia, colore del cielo.
- Filtri semplici (bianco e nero, seppia, saturazione), poi quelli con shader nostri (fumetto, VHS).

## Fase 6 — Extra locali

- Fumetti/emoji sopra la testa, emote, soundboard (solo locali).
- Diario di viaggio: passi, distanza, tempo, percorso.
- Lettore musicale.
- Scia/impronte, testa luminosa.

## Fase 7 — Replay e registrazione

- Registratore: posizione e posa di giocatori e oggetti a ogni frame, in un buffer.
- Replay istantaneo: ultimi N secondi rivisti con la camera libera della fase 3.
- Registrazione partita: lo stesso dato salvato su file per tutta la sessione, da riguardare dopo.
- Esportazione video: il replay viene ridisegnato un frame alla volta e passato a ffmpeg.

Dipende dalle fasi 0 (cosa va registrato) e 3 (camera libera).

## Fase 8 — Texture sul corpo

Ultima, è la più incerta.

## Domande aperte

- Registrazione: file di replay, video, o tutti e due? Con o senza audio/voci?
- Cappelli: bastano PNG e forme semplici o servono modelli 3D veri?
- Ordine delle fasi 2–6.
