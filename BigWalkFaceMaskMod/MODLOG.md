# MODLOG — Big Walk Face Mask Mod

## 2.0.0 (2026-10-10)

Stato: compila e deployata. **Non ancora provata in gioco.**

### Fatti verificati (asset del gioco + interop + log BepInEx)

- Unity 6000.3.17f1, IL2CPP, BepInEx 6 be.785 sotto Proton. Input tramite **Rewired**, non `UnityEngine.Input`.
- La visuale (mouse/stick) è letta in `PlayerHead.Update` (`pc.head`, campi `mouseSpinBuffer`, `headState`,
  `bypassUpdate`). `PlayerCameraMinder.Update` non legge l'input: saltarlo non ferma la visuale.
- Prefab `PlayerCharacter`: `.../head_bone/head_mesh` (contenitore, rot 90° Y, renderer disattivato) con figli
  `head_mesh_LOD0`, `head_mesh_LOD1` (MeshFilter + MeshRenderer, gestiti da un `LODGroup`) e `nose_mesh`
  a posizione locale `(-0.45, 0, 0)`.
  - Nello spazio locale della testa: **davanti = -X**, su = +Y, i lati sono ±Z.
  - Il submesh 1 della testa sono i **due occhi ai lati** (±Z), non un disco frontale.
  - Le UV del submesh 0 sono a pezzi sovrapposti (la testa è a tinta unita): inutilizzabili per una foto.
  - Mesh non leggibili (`isReadable = false`); `bounds` resta disponibile.
- Shader giocatore `househouse/PlayerCharacter`: nessuna texture, colore in `_Color0` via property block
  (non tocca un materiale URP/Lit).
- Ordine di caricamento: senza dipendenza dichiarata FaceMaskMod veniva caricata prima di BigWalkSoloMod,
  `SoloSpawnPatch.TargetMethod()` restituiva null e `PatchAll` lanciava un'eccezione.

### Come funziona ora

- Testa: il mesh di ogni renderer della testa viene sostituito con una sfera dello stesso raggio, con UV
  "azimutali" attorno all'asse frontale (trovato dalla posizione del naso). Submesh 1 vuoto, così il materiale
  degli occhi non disegna nulla. Il naso viene nascosto con `forceRenderingOff`.
- Foto: ricalcolata in una texture 768² nello stesso layout (`FaceManager.Bake`); zoom, spostamento e
  avvolgimento sono parametri del ricalcolo. Le aree trasparenti e il retro della testa prendono il colore
  dei capelli (media della fascia alta del ritaglio).
- Visuale: prefix Harmony su `PlayerHead.Update` per il giocatore locale + `head.bypassUpdate` mentre un
  menu è aperto.
- Impostazioni per faccia in `BepInEx/plugins/FaceMaskMod/settings/<nome>.cfg`.

### Da controllare al primo avvio (`BepInEx/LogOutput.log`)

- `[Patch] PlayerHeadUpdatePatch: OK` e `[Patch] SoloSpawnPatch: OK`
- `[FaceMaskMod] Head 'head_mesh_LOD0': ... front (-1.00, 0.00, 0.00) (from nose)`

### Non verificato

- Che `bypassUpdate`/il prefix fermino davvero ogni rotazione della visuale.
- Orientamento della faccia in gioco (in caso: pulsanti "Gira 90°" / "Ruota 90°" / "Specchia").
- "Rimani su Player 1 allo Spawn": nella 1.4.0 non è mai stato eseguito; ora è OFF di default.

## 2.1.0 (2026-10-10)

Stato: compila e deployata. La 2.0.0 è stata confermata funzionante in gioco dall'utente; **le novità 2.1.0 non sono ancora provate**.

- Dal log della 2.0.0: `looks.headRenderers` contiene sia la testa (`head_mesh`, `head_mesh_LOD0/1`) sia il naso
  (`animatingNose`, `nose_mesh_LOD0/1`). La 2.0.0 sostituiva per errore anche i mesh del naso (nascosti, quindi
  non si vedeva); ora i renderer "nose" sono trattati a parte.
- Naso per slot (scheda "Naso e testa"): visibile/nascosto, colore da ruota HSV. Il colore è scritto con un
  `MaterialPropertyBlock` sulla proprietà `PlayerLooks.lookColorPropertyName` (ripiego `_Color0`) e riapplicato
  ogni 20 frame; togliendolo si chiama `looks.SetColor(headRenderers, headColor)`.
- Grandezza testa per tutti: scala del contenitore `head_mesh` (LOD + naso) e spostamento in su di
  `(k-1) * raggio` lungo Y di `head_bone`, così la testa resta appoggiata al collo. Solo visivo.
- Lag delle regolazioni: il bake chiamava `Mathf.*` (chiamate interop il2cpp) ~6 volte per pixel e scriveva i
  pixel uno a uno nell'array il2cpp. Ora usa `MathF` su array gestiti, righe in parallelo, una sola copia in
  memoria; durante il trascinamento si calcola un'anteprima 256², la texture piena 768² arriva 0,25 s dopo.
- Impostazioni globali in `settings/_global.cfg`.

## 3.0.0 (2026-10-10)

Stato: compila e deployata, **non ancora provata in gioco**.

- La mod è diventata il modulo "Facce" di Big Walk Mod Menu (`../BigWalkModMenu/`): menu, blocco della visuale,
  integrazione Solo Mod e aiuti "Raduna" sono passati al menu base (scheda Generale).
- La faccia scelta per ogni giocatore ora viene ricordata (`SlotFace` in `settings/_global.cfg`).
- Nuova scheda "Busto": un'immagine della cartella `faces` avvolta sulla sfera del busto (`torso_mesh/BodyBall_LOD*`),
  con lo stesso sistema della testa. Il davanti viene preso dalla direzione della testa, appena guarda dritto.
- Colore di riempimento per faccia (`Fill=r,g,b` nel `.cfg`) e cinque facce di prova: aggiunti
  dopo la 2.1.0, anche questi non ancora confermati in gioco.
