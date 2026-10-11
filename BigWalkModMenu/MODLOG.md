# MODLOG — Big Walk Mod Menu

Giornale del progetto. I fatti sulla mod delle facce restano in `Faces/MODLOG.md` (fino alla 1.4.0 la cartella era
`../BigWalkFaceMaskMod/`: i percorsi nelle voci vecchie qui sotto sono quelli di allora).

## 1.0.0 (2026-10-10)

Stato: **tutto compila ed è installato; niente di questa versione è ancora stato provato in gioco.**
Provati fuori dal gioco: decodifica audio (WAV 8/16/24/float, OGG, MP3) e salvataggio/caricamento delle
registrazioni (anche file troncato e file non valido).

### Struttura

- `Core/` → `BigWalk.ModMenu.dll` (GUID `com.bigwalk.modmenu`): finestra a schede, `Ui`, `ModConfig`, `Hotkey`,
  `InputLock`, `CameraRig`, `Knob`, `Images`, integrazione Solo Mod (`Solo`), scheda "Generale".
- Un progetto per modulo, ognuno con il suo `BasePlugin` che chiama `ModMenuApi.Register(new XModule())`:
  `Character`, `Camera`, `Performance`, `World`, `Extras`, `Replay`. La mod delle facce (`BigWalk.FaceMaskMod`
  3.0.0) è il modulo `faces`.
- Installazione: `BepInEx/plugins/BigWalkModMenu/*.dll` (+ `StbImageSharp`, `NVorbis`, `NLayer`);
  le facce restano in `BepInEx/plugins/FaceMaskMod/`.
- Dati: `BepInEx/plugins/BigWalkModMenu/config/<modulo>.cfg` e una cartella per modulo
  (`camera/foto`, `extras/musica`, `extras/suoni`, `extras/adesivi`, `replay/registrazioni`, `replay/video`).
- Build: `DOTNET_ROOT=~/.dotnet ~/.dotnet/dotnet build` in ogni cartella; riferimenti comuni in `Directory.Build.props`.
- Un modulo che lancia eccezioni viene messo in pausa dopo 40 errori e si riattiva dalla sua scheda.

### Fatti verificati sugli asset e sull'interop (non in gioco)

- Prefab `PlayerCharacter`: `kernal/dummy` (Animator, `PlayerAnimatables`) → `Armature` (scala 0,162) → ossa.
  Il corpo **non è skinnato**: è fatto di sfere separate (`head_mesh`, `torso_mesh`, `bottom_mesh`,
  `left/right_hand_mesh`, `left/right_foot_mesh`, ognuna contenitore di `..._LOD0/1`), più arti disegnati da
  `LimbSpline`. Nel prefab non compare nessun componente `LimbSpline`: vanno cercati a runtime.
- `CameraPivot` (sotto `kernal/crouchTranslator/CameraHeightOffset/CameraUprighter`) porta un
  `CameraCheatMover` degli sviluppatori con `Detach()`/`Attach()` e `_lastParent`: la camera è figlia del pivot
  e staccarla è il metodo che usano anche loro. La camera non è nel prefab: `PlayerCameraMinder.TakeCamera()`.
- `PlayerLooks`: `headRenderers`, `torsoRenderers`, `legsRenderers`, `SetColor(...)`, `currentLookType`,
  `SetBodyToRemoteMode()` / `SetBodyToLocalMode()` (corpo intero come lo vedono gli altri).
- `PlayerCharacter`: `bypassUpdate`, `bypassLateUpdate`, `bypassFixedUpdate`, `myAnimator`, `rb`, `inputPlayer` (Rewired).
- `ControlsManager.Refresh()` ricostruisce le mappe Rewired; `inputPlayer.controllers.maps.SetAllMapsEnabled(false)` compila.
- Cielo: Enviro 3 (`EnviroManager.instance` con `Time`, `Lighting`, `Sky`, `Fog`...). Il gioco ha un suo
  interruttore locale: `SkyManager.SetFixedTime(float)`, `ClearFixedTime()`, `GetCurrentTime()`.
  `Fog.customFogDensityModifer`, `Lighting.Settings.directLightIntensityModifier`/`ambientIntensityModifier`,
  `Sky.Settings.intensity`/`sunScale`/`moonScale`.
- Qualità: livelli 0–5 (`LightQualityData.pc0..pc5`), un gestore per sistema con `ApplyQualityEffects(int)`:
  `TerrainQualityManager.instance`, `OceanQualityManager`, `SkyQualityManager`, `HouseLightManager`,
  `PostProcessingQualityManger.Instance` (con `baseVolumeProfile`), `CameraQualityManager.Instance`.
  `SettingsHelper.GetQualityLevel()`. `SettingsHelper.hudIsHidden` è in sola lettura.
- Il gioco ha già RenderScale, VSync, FrameRateLimit e Fov tra le sue impostazioni (`SettingsType`).
- `PlayerCameraMinder.universalRendererData.rendererFeatures`: lista delle feature URP (HBAO, Blur, Enviro,
  RenderUI...): i nomi veri vengono scritti nel log la prima volta che si nasconde l'interfaccia.
- `Prop.allProps` (lista statica), `Prop.rb`, `Prop.savablePropGuid`.
- Altra mod installata: `smolMods.Core` + `ColorSelf` (framework di terzi che inietta righe nel menu del gioco
  e ha un suo canale di rete). Non lo usiamo e non lo tocchiamo.

### Scelte

- Niente rete: tutto è locale (deciso dall'utente).
- IMGUI solo con `GUI.*` a rettangoli fissi, niente `GUILayout`.
- Blocco input: visuale con il prefix su `PlayerHead.Update` (già confermato in gioco nella 2.0.0);
  tutto il resto spegnendo le mappe Rewired del giocatore a ogni frame e chiamando `ControlsManager.Refresh()` alla fine.
- Camera libera: tasto destro + trascinamento con il cursore libero (`Input.mousePosition`), per non dipendere
  dagli assi del vecchio Input Manager che il gioco potrebbe non avere.
- Filtri: un `Volume` globale nostro con solo effetti URP standard. Niente shader nostri (servirebbe un
  AssetBundle fatto con l'editor Unity): quindi niente filtro "fumetto".
- Replay: si pilotano i personaggi veri (animator spento, `bypass*`, rigidbody cinematico) invece di clonarli.
- Video: sequenza PNG + script `crea_video.sh` con ffmpeg lato Linux. Lanciare ffmpeg da dentro Proton non è affidabile.

### Da verificare in gioco, in ordine

1. Log: `Module registered: ...` per 8 moduli, `[Patch] PlayerHeadUpdatePatch: OK`, nessuna eccezione di `MenuHost`.
2. Menu (F8 / Insert, o F con la Solo Mod): schede, scorrimento, grandezza; scheda Facce identica a prima.
3. Personaggio: `[Character] ... N scalable parts, M limb splines` (M = 0 → "spessore arti" non fa nulla);
   colori che non sfarfallano; cappelli dritti e nel posto giusto (`Head anchor ... radius`); luce e scia.
4. Telecamera: `[CameraRig] Camera '...' detached from '...'`; selfie/terza persona senza tremolii (se la
   camera scatta, il gioco la riscrive dopo di noi); corpo intero visibile; foto salvate; interfaccia nascosta.
5. Prestazioni: tempi CPU/GPU (possono non esserci); scala di rendering; verso dei livelli 0–5 (0 è il migliore o il peggiore?).
6. Mondo: unità di `SkyManager.SetFixedTime` (ore 0–24?), filtri di colore; grana/aberrazione/distorsione
   possono mancare se gli shader sono stati tolti dalla build.
7. Extra: audio (`AudioClip.SetData` con copia in blocco), fumetti, emote sul `dummy`.
8. Replay: i personaggi si muovono come registrato; alla chiusura tutto torna a posto (posizione, animazioni,
   comandi); oggetti; esportazione video e `crea_video.sh`.
9. Stampa sul busto (scheda Facce → Busto): orientamento dell'immagine.

### Ripristino

Versione precedente della mod delle facce: `../BigWalkFaceMaskMod/BigWalk.FaceMaskMod-2.1.0.dll.bak` e
`Plugin-2.1.0.cs.bak`. Per tornare indietro: cancellare `BepInEx/plugins/BigWalkModMenu/` e rimettere quella DLL
in `BepInEx/plugins/FaceMaskMod/`.

## Prova in gioco della 1.0.0 fatta dall'utente (2026-10-10, 13:38–13:54) — letta dal log e dalle sue foto

Confermato dal log (`BepInEx/LogOutput.log`) e da tre foto scattate con la mod:

- 8 moduli registrati, `PlayerHeadUpdatePatch` e `SoloSpawnPatch` OK, nessuna eccezione di `MenuHost`.
- Personaggio: `10 scalable parts, 8 limb splines` per personaggio. Cappello (cilindro) dritto e al posto giusto,
  occhiali sul viso, naso allungato: visti in foto.
- Stampa sul busto: funziona (faccia sulla sfera del busto, visibile in foto).
- CameraRig: `Camera 'PlayerCamera' detached from 'CameraPivot'`. Foto salvate, anche a 4x (7680×4320, 20 MB).
- Feature URP reali: `EnviroURPRenderFeature`, `BlurRendererFeature`, `GrabGlassRendererFeature`,
  `FullScreenPassRendererFeature`, `HBAO`, `WorldUI`, `MaskUI`, `TextChatUI`, `SkyboxToCubemapRenderFeatureRenderGraph`.
  L'interfaccia è nelle tre che finiscono in `UI`.
- Replay: `45 bones recorded per player`; `Playing 'replay': 1800 frames, 60.0 s, 4 characters` senza errori; una clip salvata (388 KB per 60 s, 4 personaggi).
- Mondo: `Filter volume created on layer 12`.

Problemi trovati:

1. `[world] DrawOverlay: ObjectCollectedException: Object was garbage collected in IL2CPP domain` sulla texture
   delle righe TV (filtro VHS): il modulo andava in pausa a ripetizione. Causa non chiarita: Il2CppInterop tiene
   handle forti, eppure l'handle di una `Texture2D` tenuta in un campo è morto dopo pochi secondi.
   Rimedio (1.1.0): `Game.Alive(obj)` su ogni oggetto del gioco tenuto tra un frame e l'altro, che viene ricostruito
   se non c'è più; in più `MenuModule.OnObjectsLost()` chiamato dal host a ogni `ObjectCollectedException`.
2. `xdg-open` non esiste dentro Proton (`File not found`): "Apri cartella" non faceva nulla. Ora prova
   `explorer.exe` e mostra comunque il percorso. Non verificato.
3. Le foto includono il pannello della Solo Mod se è aperto (non è nostro, non possiamo nasconderlo).

Non risulta dal log (da chiedere all'utente): tremolio della camera, colori del corpo, luce/scia, prestazioni,
ora del giorno, audio, emote, fumetti, ripristino dopo il replay, esportazione video.

### Tasti mentre si scrive in chat (2026-10-10)

Segnalato dall'utente: scrivendo una "f" nella chat (Invio) si apriva il pannello. Il tasto F è della Solo Mod, che
legge i suoi tasti (F, R, +, 1-4) senza guardare la chat. Rimedio: `Game.IsTyping`
(`ControlsManager.textInputModeActive` oppure `playerNetworking.isTextChatting`), usato da tutti i nostri `Hotkey`,
dalla camera libera e da un prefix Harmony su `SoloPlayerController.Tick` che lo salta mentre si scrive.
Non verificato in gioco: non so quale dei due segnali il gioco alzi davvero aprendo la chat.

## 1.2.0 (2026-10-10) — schede Voce e Reshade, opzioni grafiche di Big Visuals

Tre mod di terzi lette (decompilate con ilspycmd, fuori dal repo) e rifatte da zero dentro il menu. Nessun loro
codice è stato copiato: Big Voice 1.0.2, Big Visuals 1.0.0 (glarmer), preset ReShade "Cinematic Reshade".
Decompilato nativo del gioco in `~/bigwalk-decomp` (Cpp2IL 2022.1.0-pre-release.21, formati `diffable-cs` e `isil`).

### Voce (`Voice/`, id `voice`)

Fatti letti nel nativo (gioco v1.6.0, Unity 6000.3.17f1):
- `PlayerVoicePlaybackControl.Update` chiama ogni frame `VoiceMakeupGain.Evaluate(nome, arv, staParlando, dt)` e
  scrive il risultato in `dissonanceSampleProvider.MakeupGain` (offset 0x9C). La chiamata è condizionale.
- `SamplePlaybackComponent.ProcessSamples` (thread audio): campione × guadagno (rampa da `_currentGain`) →
  `VoiceCompressor.Process` → `SoftClip` (ginocchio 0,85, tetto 0,95). Il risultato va sia nel buffer `data` sia nel
  buffer circolare `CachedVoiceData`, che è quello che il gioco suona davvero (`RebuildCachedClip` → `AudioManager.Play`).
- Big Voice moltiplica `data` in un postfix di `ProcessSamples`: non tocca il buffer circolare. Non so se con questa
  versione del gioco si senta ancora; noi non usiamo quel punto.
- `VoiceMakeupGain.Volume` (0–1) è il volume voce delle impostazioni del gioco (diventa `TargetARV` 0,01–0,4).

Come facciamo: prefix/postfix su `PlayerVoicePlaybackControl.Update` + postfix su `VoiceMakeupGain.Evaluate` che
moltiplica il risultato. Tutto sul thread principale; il boost passa dal compressore e dal soft clip del gioco.
Se in un frame `Evaluate` non viene chiamata, il postfix di `Update` scrive lui `MakeupGain`.
Identità: `Identity.Key` (id piattaforma). Impostazioni in `config/voice.cfg` (`Vol.<id>`, `Name.<id>`).

Provato in gioco: le due patch si applicano; `Evaluate` chiamata da noi passa dalla patch (1,0396 → 3,1188 con ×3).
**Non provato: una voce vera.** In partita da soli (Solo Mod) `PlayerVoicePlaybackControl.controls` è vuota. Serve
una partita con un amico che parla: volume, muto, misuratore, nome e id ricordati.

### Reshade (`Reshade/`, id `reshade`)

Il preset attiva: AmbientLight, CAS, BaBa_VividTone, Bloom+lens dirt, Blooming_HDR, UnrealLens, PD80 Filmic.
Rifatto con un `Volume` globale nostro (priorità 900, sotto il filtro di Mondo che è a 1000): `Bloom` con
`dirtTexture` disegnata in codice, `ScreenSpaceLensFlare`, `Tonemapping`, `ColorAdjustments`,
`ShadowsMidtonesHighlights`. Nitidezza: l'RCAS che URP esegue dopo il TAA (`m_TaaSettings.m_ContrastAdaptiveSharpening`),
quindi la scheda porta la camera a TAA finché la nitidezza è accesa (`GraphicsShare.AntialiasingOwner`).

Renderer del gioco (dal log): HDR sì a 32 bit, grading LDR, AA della camera SMAA, MSAA 1, nessuna camera impilata.

Provato in gioco con screenshot a scena ferma: acceso/spento cambia la luminosità media (46 → 65 su 255 con i valori
dell'utente) e torna identico (RMSE 0,0002); nitidezza: energia dei bordi +46%, camera in TAA con CAS 0,8, e al
rilascio torna SMAA. Stile "Cinematic" visto una volta al chiuso (più contrasto e saturazione), poi ammorbidito
(esposizione 0,5, contrasto 8, saturazione 10, ombre 0,15): i valori finali non sono stati rivisti in gioco.
Non verificati a occhio: lente sporca e riflessi (servono luci forti in scena), gli altri stili.

### Prestazioni: opzioni di Big Visuals

Nuovi `Knob`: antialiasing della camera (nessuno/FXAA/SMAA/TAA), FOV 45–150 (`PlayerCameraMinder.fovFromSettings`),
filtro di ridimensionamento, cascate (`m_ShadowCascadeCount`), risoluzione ombre (`m_MainLightShadowmapResolution`),
ombre morbide, anisotropico; scala di rendering 0,1–2 e LOD fino a 10x; profilo "Nitido" (la ricetta di Big Visuals).
- FOV: `PlayerCameraMinder.Update` lo copia sulla camera ogni frame, ma non per un personaggio della Solo Mod
  (lì la camera restava ferma): il knob scrive anche `camera.fieldOfView` quando cambia. Provato: 80 → 120 → 80.
- Antialiasing provato con l'energia dei bordi: nessuno 142, SMAA 117, FXAA 115, TAA 94; spento torna a 117.
- Cascate, risoluzione ombre, anisotropico, filtro: applicati senza errori, effetto non misurato.

### Strumenti di prova

`Core/Dev.cs` (solo con `dotnet build -p:Dev=1`, e solo se esiste `BepInEx/plugins/BigWalkModMenu/dev/`): comandi
in `dev/in.txt`, risposte in `dev/out.txt` — `state`, `open <scheda>`, `close`, `shot <nome>`,
`set <modulo> <chiave> <valore>`, `find <Tipo>`, `call <Tipo> <Metodo>`, `mod <modulo> ...`, `wait <frame>`.
La build installata e quella nel pacchetto sono senza `Dev`. La Solo Mod entra in partita da sola all'avvio.
Avvio in finestra: `steam -applaunch 1478500 -screen-fullscreen 0 -screen-width 1600 -screen-height 900`.

## 1.3.0 (2026-10-10) — scheda Solo, cartella delle facce, menu trascinabile, pacchetto a mod separate

### Solo (`Solo/`, id `solo`, DLL a sé)

Rifatta dentro il menu la mod "Big Walk Solo Player 0.4.2" (letta decompilata; stesse chiamate al gioco, codice nostro).
`Core.Solo` ora è solo il registro dei quattro slot (`Solo.SlotIds`), usato da Facce/Personaggio/Extra; senza la
scheda uno slot è l'n-esimo giocatore. Tolti dal Core: patch sulla mod originale, pannello accanto, "Rimani su Player 1".
Se la mod originale è caricata la scheda resta ferma e lo dice. Spenta di default (`solo.cfg` `Enabled`).

Fatti trovati in gioco:
- **Togliere il personaggio in uso nello stesso frame in cui il controllo è passato a un altro ha chiuso il gioco**
  (log: `OnChangeOwner: Could not find object with netId`, poi processo sparito, nessun rapporto). Ora la rimozione
  aspetta 45 frame dopo il cambio; riprovato: nessun errore.
- **Il gioco mette ogni personaggio nuovo al centro della mappa (0, 0)** per qualche istante dopo la nascita, anche
  già durante `ReplacePlayerForConnection` (la posizione letta subito dopo è già sbagliata). La posizione voluta si
  prende prima di `Instantiate` e il personaggio viene riportato lì finché, entro 900 frame, ricompare al centro.
  Provato: tre personaggi su tre restano accanto al giocatore.
- **Errori dei passi**: ogni personaggio ha un `FootstepSoundJobScheduler` che ogni frame chiama
  `FootstepSound.ScheduleJobs`, e per chi non è il giocatore locale il gioco scrive un errore (185.000 righe in una
  sessione con la mod originale). Lo spegniamo sui personaggi non in uso: 0 errori.
- Provati dal ponte di prova: aggiungi ×3, cambia, segui (si mettono in formazione), raduna, togli (in uso e non).
  Non provati: i tasti (+, 1-4, R) e i pulsanti cliccati a mano, "Dopo un'aggiunta resto dove sono", "Alla partenza".

### Altro

- Menu trascinabile dalla barra del titolo (`general.cfg` `WindowX/Y`, in unità del menu). Provata la posizione
  impostata dal ponte; il trascinamento col mouse no.
- Facce 3.2.0: cartella delle foto a scelta (`faces.cfg` `Folder`), con `Core/FolderPicker.cs` (elenco di cartelle a
  pulsanti + incolla percorso). Sotto Proton `Z:\` è il disco Linux: provato con `Z:\home\...\Pictures\BigWalk Facce`
  (5 facce lette). Il selettore non è stato cliccato in gioco.
- Sul PC dell'autore: foto spostate in `~/Pictures/BigWalk Facce`, mod Solo originale spostata in
  `~/Desktop/Backup_Salvataggio_BigWalk/BigWalkSoloMod-originale`.
- Installer: scelta delle mod (`--mod a,b` / `-Mod a,b`, `--tutte`), rilanciabile: aggiorna le presenti, aggiunge le
  scelte, non toglie. Quello Linux provato su una cartella finta; **quello PowerShell non è stato eseguito** (qui non c'è).
- Pacchetti: `dist/BigWalkModMenu-1.3.0.zip` (tutto + installer) e `dist/singole/` (menu base + una mod per zip).
  Controllo prima della pubblicazione: nessuna immagine, nessun percorso personale nelle DLL, nessun file del gioco.
- Ponte di prova: aggiunti `window x y` e `mod solo spawn|select n|delete n|follow|gather|state`. Script client in
  `~/bigwalk-decomp/work/dev.sh`.

## 1.4.0 (2026-10-10) — tutto in inglese, repository git

- Interfaccia, installer, disinstallatori, README e changelog tradotti in inglese; `LEGGIMI.txt` tolto.
  Script rinominati `install-*` / `uninstall-*`, opzioni `--yes --dry-run --all-mods --mod` (`-Yes -DryRun -AllMods -Mod`),
  nomi delle mod per `--mod`: `faces solo character camera performance world reshade voice extras replay`.
- Cartelle e file dati con nome italiano rinominati al primo uso con `ModMenuApi.Renamed` (il contenuto resta):
  `foto→photos`, `musica→music`, `suoni→sounds`, `adesivi→stickers`, `registrazioni→recordings`, `profili→profiles`,
  `amici→friends`, `fumetti.txt→bubbles.txt`, `diario.csv→diary.csv`; `crea_video.sh→make_video.sh` per i nuovi video.
  Le chiavi dei file `.cfg` erano già in inglese: le impostazioni restano valide.
- Repository git in `~/Documents/antigravity/amazing-volta` (ramo `master`): commit di partenza `v1.3.0`, poi un commit
  per passo. Fuori dal repository: `bin/obj`, DLL e zip del pacchetto, immagini, `facce_rimosse/`, file `.bak`.
- Provato: compilazione di tutti i progetti; installer e disinstallatore Linux in inglese su una cartella finta.
  Interfaccia inglese vista in gioco scheda per scheda (un solo testo tagliato, accorciato); le cartelle dati si sono rinominate da sole. **Non provati: gli script PowerShell.**

## 1.5.0 (2026-10-10) — la mod delle facce diventa una scheda come le altre, repository su GitHub

- Sorgente: `BigWalkFaceMaskMod/` → `BigWalkModMenu/Faces/` (`BigWalk.ModMenu.Faces.csproj`, namespace
  `BigWalk.ModMenu.Faces`, riferimenti presi da `Directory.Build.props` come gli altri progetti).
  DLL `BigWalk.ModMenu.Faces.dll`, GUID `com.bigwalk.modmenu.faces` (prima `com.antigravity.facemaskmod` 3.3.0),
  versione uguale a quella del menu. Il GUID è cambiato apposta: con lo stesso GUID BepInEx avrebbe tenuto la
  DLL vecchia, che ha il numero di versione più alto.
- Installazione: `BepInEx/plugins/BigWalkModMenu/BigWalk.ModMenu.Faces.dll`; dati in `BigWalkModMenu/faces/`
  (`photos`, `settings`, `friends`). Passaggio dalla vecchia `BepInEx/plugins/FaceMaskMod/`:
  - gli installer tolgono `BigWalk.FaceMaskMod.dll` e il suo `StbImageSharp.dll` e contano la vecchia DLL come
    "Faces installata", quindi la scheda viene aggiornata anche se non la si sceglie;
  - `Core` (`RemoveLegacyFaces`) cancella la vecchia DLL all'avvio se c'è quella nuova, per chi copia a mano;
  - la scheda (`MoveLegacyData`) sposta `faces→photos`, `settings`, `amici`/`friends` file per file senza
    sovrascrivere, e toglie la cartella vecchia se resta vuota. Una cartella foto salvata che puntava a
    `FaceMaskMod/faces` torna a essere "quella della mod".
- `build.sh` nella radice: compila tutto, riempie `dist/pacchetto` e crea `dist/BigWalkModMenu-<versione>.zip` e
  `dist/single/`. Prima si faceva a mano.
- Repository pubblico: https://github.com/Giorey01/BigWalkModMenu. Dentro ci sono anche le skill usate per il
  lavoro (`.claude/skills`, da universal-modder, licenza MIT) e `CLAUDE.md` con build e ciclo di prova.
- Provato: compilazione degli 11 progetti; installer Linux su una cartella finta con la vecchia mod delle facce
  (prova a secco e vera: DLL vecchie tolte, foto e impostazioni lasciate al loro posto), disinstallatore a secco.
  **Non provati (al momento del rilascio): l'avvio in gioco e gli script PowerShell.**

### Prova in gioco della 1.5.0 (2026-10-10, 16:47–16:55) — dal log e dal ponte di test

Installazione "a mano" dello zip scaricato dalla release (cartella `BepInEx` copiata sopra la 1.4.0, senza
installer), cioè il caso peggiore: la vecchia DLL delle facce era ancora al suo posto.

- Primo avvio: `Removed the old faces plugin file ...BigWalk.FaceMaskMod.dll` e `...StbImageSharp.dll` da `Core`,
  poi `[Faces] Moved ...FaceMaskMod\faces to ...BigWalkModMenu\faces\photos` e lo stesso per `settings`
  (i 6 file `.cfg` sono arrivati), cartella `FaceMaskMod` sparita, `5 faces` lette dalla cartella scelta dall'utente.
  11 moduli registrati, tutte le patch `OK`.
- In quel primo avvio BepInEx scrive **una volta** `Error loading [Big Walk Face Mask Mod 3.3.0]: FileNotFoundException`:
  aveva già visto la vecchia DLL prima che `Core` la cancellasse. È atteso e non si ripete (secondo avvio: nessun errore).
  Con l'installer non succede: la DLL viene tolta prima.
- Secondo avvio con `Core` in versione `Dev`: `state` → in partita, 11 moduli, `faults=0` per tutti; ogni scheda
  aperta e fotografata (`open <scheda>` + `shot`), nessuna eccezione nel log dopo. Da quelle foto vengono le
  immagini di `docs/screenshots/` (ritagliate sul menu; nella scheda Faces la riga col percorso della cartella
  foto è coperta perché contiene il nome utente).
- Dopo la prova: gioco chiuso, DLL di `Core` del pacchetto rimessa, cartella `dev` tolta. Copia della 1.4.0
  installata in `~/bigwalk-decomp/work/backup-install-1.4.0-before-1.5.0`.
- **Non provati:** mettere una foto su una testa con la 1.5.0 (le schede si aprono, la logica delle facce non è
  cambiata, ma nessuna faccia è stata applicata in questa prova); partita con un amico; script PowerShell.

### Installer da una riga (2026-10-10, dopo il rilascio della 1.5.0)

- `dist/online/install.ps1` e `install.sh`: scaricano lo zip della loro versione, ne controllano lo SHA-256 e
  lanciano l'installer che c'è dentro. `build.sh` li genera in `dist/` mettendoci versione e impronta dello zip
  appena creato; vanno caricati nella release insieme allo zip (stessa esecuzione di `build.sh`, altrimenti
  l'impronta non torna). Comandi: `irm .../releases/latest/download/install.ps1 | iex` e
  `bash <(curl -fsSL .../releases/latest/download/install.sh)`.
- Windows: l'installer parte in un processo `powershell -ExecutionPolicy Bypass` (nella finestra dell'utente i
  file `.ps1` sono bloccati di default); i permessi di amministratore vengono chiesti solo se l'installer fallisce.
  Niente `exit` nello script: gira dentro la finestra dell'utente.
- Linux: con `curl | bash` le domande dell'installer leggono da `/dev/tty`; senza terminale si passa `--yes`.
- Gli asset della release 1.5.0 sono stati ricaricati dalla stessa build (DLL identiche byte per byte a quelle
  provate in gioco; cambia solo lo zip, quindi l'impronta).
- Provato: su Linux a secco, `bash <(curl ...)`, `curl ... | bash -s -- --mod voice` e senza terminale; scaricamento
  e controllo dell'impronta dalla release vera. **Non provato: `install.ps1` (nessun PowerShell su questa macchina);
  lo prova l'utente su Windows.**

### Fumetti rimasti in italiano (2026-10-10, dopo la 1.5.0)

- Causa: `extras/bubbles.txt` viene scritto solo se manca. Chi aveva già usato una versione italiana (fino alla
  1.3.0) si è tenuto `fumetti.txt` rinominato, quindi i pulsanti dei fumetti sono rimasti `Ciao!`, `Aspettatemi`,
  `Seguitemi`, `Di qua!`, `Bravo!`, `Ops`, `Foto!`. La 1.4.0 aveva tradotto solo il testo di partenza nel codice.
- Rimedio (`Extras`, `TranslateOldBubbles`): al primo avvio le righe uguali a uno dei sette testi italiani di
  partenza diventano quelle inglesi; le righe scritte o cambiate dal giocatore restano come sono. Si fa una volta
  sola (`Bubble.English` in `config/extras.cfg`), così chi poi riscrive `Ciao!` a mano se lo tiene.
- Provato: compilazione di `Extras`. **Non provato in gioco al momento del commit.**

### Testa a forma di D20 (2026-10-10, dopo la 1.5.0) — richiesta di un utente

- Scheda Character, nuova sezione **Head → Shape**: `Normal` / `D20 die` (`Look.Head`, chiave `P<n>.HeadShape`).
- Come è fatto: non si tocca la testa del gioco. Il dado è un pezzo del "vestito" (`bwmm_outfit/head`) attaccato
  all'ancora della testa come cappelli e accessori, quindi segue già visibilità (in prima persona sparisce con la
  testa), ombre e livello. Raggio dei vertici 1,27 volte il raggio della testa: le facce di un icosaedro stanno a
  0,795 del raggio dei vertici, quindi sfiorano la testa (1,009) e la coprono tutta; il naso esce dalla faccia
  davanti. Scelta fatta così perché la scheda Faces sostituisce già la mesh della testa con la sua sfera: due
  schede sullo stesso `MeshFilter` si sarebbero pestate i piedi. Conseguenza: **il dado copre la foto della faccia**
  (scritto nella scheda).
- `Shapes.D20` / `Shapes.D20Numbers` (`Character/Shapes.cs`): icosaedro a facce piatte, una faccia verso -X (il
  viso) e quella sopra, inclinata come una fronte, è il 20; facce opposte a somma 21; numeri a sette segmenti
  costruiti come geometria (nessuna texture), 6 e 9 sottolineati. Colore del dado = colore della testa (quello
  scelto nella scheda, altrimenti `looks.headColor.color` del gioco); numeri scuri o chiari secondo la luminosità.
- Con il dado: cappello alzato di 0,3 (`DieHatLift`), accessori spostati in avanti di 0,08 (`DieAccessoryShift`).
- Rete: un byte in più **in fondo** al messaggio dell'aspetto (16), senza cambiare `WireVersion`: la 1.5.0 smette di
  leggere prima e mostra la testa normale; questa versione accetta anche i messaggi senza quel byte.
- Ponte di prova: `call` ora trova anche i metodi privati e passa un argomento (int, bool, float, enum);
  `mod camera selfie|third|free|off`. Per entrare in partita da soli: `call MicCheckMenu ActionContinue`, (aspettare
  che `TitleMenu` sia attivo), `call TitleMenu GoToHostMenu`, `host 0`, `call HostMenuConfirm ActionStart`,
  `call PlayerCountMenu SetPlayerCount 2`, `call PlayerCountMenu GoToLoadingMenu`, `call NetworkMinder StartHost`.
  (`ActionProceed` non fa niente finché il numero di giocatori è `NotSet`.)
- Numero di versione non toccato (resta 1.5.0): niente rilascio in questo passaggio; il changelog ha "Unreleased".

Provato fuori dal gioco: `Shapes.cs` compilato con finti tipi Unity, triangoli tutti rivolti all'esterno, le 20
facce disegnate una per una (numeri giusti, non specchiati, dentro la faccia, opposte a somma 21).

Provato in gioco (23:40–23:50, partita da solo avviata dal ponte, foto in `~/bigwalk-decomp/work/d20-shots`):
- dado visibile in selfie e in terza persona con il colore scelto per la testa, 20 sulla fronte, naso che esce dalla
  faccia davanti, 6 sottolineato dietro; cilindro appoggiato sopra; occhiali e baffi sulla faccia davanti;
  in prima persona non si vede; tornando a `Normal` sparisce. Nessuna eccezione nel log, `faults=0` per tutti.
- fumetti (commit precedente): al primo avvio `bubbles.txt: 7 Italian texts ... translated`, file poi in inglese.
- Dopo la prova: gioco chiuso, DLL e `config/` rimesse dalla copia `~/bigwalk-decomp/work/backup-install-1.5.0-before-d20`
  (installazione di nuovo 1.5.0), cartella `dev` tolta. `bubbles.txt` è rimasto tradotto. Il gioco ha salvato la
  partita del primo slot durante la prova.

**Non provati:** dado visto da un amico in rete (e da un amico con la 1.5.0); dado col colore di testa del gioco
(la foto è venuta sfocata: il gioco aveva oscurato lo schermo); dado insieme a una foto della faccia e alla testa
ingrandita da Faces; gli altri cappelli oltre al cilindro, orecchie e corna; il menu a tendina cliccato a mano.

### Filtri che fanno "crashare" il gioco (2026-10-11, dopo la 1.5.0) — segnalato dall'utente

Segnalazione: attivando alcuni filtri della scheda World, come **VHS**, "il gioco sembra crashare". L'utente ha
chiesto di **non aprire il gioco**: tutto quello che segue è fatto a gioco chiuso.

Cosa si è potuto guardare:
- Log: quelli rimasti (`Player.log`, `Player-prev.log`, `bigwalk-1..5.log`, `LogOutput.log`) sono di sessioni
  successive, chiuse normalmente e senza filtri; quella del problema è già stata sovrascritta. Nessun dump di
  Unity nel prefisso Proton, quindi non risulta un crash nativo: più probabile un blocco dell'immagine o del gioco.
- Asset del gioco (letti con UnityPy da `data.unity3d`, sola lettura): lo shader `UberPost` ha tutte le varianti
  `_FILM_GRAIN`, `_CHROMATIC_ABERRATION`, `_DISTORTION` (720 varianti del frammento) e `FinalPost` ha `_FILM_GRAIN`;
  le dieci texture della grana (`Thin01`…`Large02`) ci sono. Quindi **grana, aberrazione e distorsione non sono
  state tolte dalla build**: la vecchia ipotesi del diario (punto 6 della lista di prova) è da scartare.
- `Il2CppInterop` di questa installazione tiene handle forti (`il2cpp_gchandle_new`): resta senza spiegazione
  l'handle morto della texture delle righe TV visto nella 1.1.0.

**La causa non è stata trovata con certezza.** VHS è l'unico preset che accende insieme grana, aberrazione,
distorsione e righe TV; le righe TV erano già l'unico pezzo con un difetto noto. Cambiato quindi (`World`):

1. Righe TV senza oggetti nostri: disegnate con `Texture2D.whiteTexture` (al massimo ~270 strisce, solo
   nell'evento `Repaint`), non più con una `Texture2D` creata dalla mod e tenuta in un campo.
2. Volume del filtro: prima di crearne uno nuovo si cercano per nome e si tolgono quelli vecchi (`DestroyVolume`).
   Prima, se l'handle di `bwmm_filter_volume` moriva, l'oggetto restava nel gioco con il filtro acceso per sempre e
   se ne aggiungeva un altro. `VolumeAlive` controlla anche i sette componenti; un `ObjectCollectedException` mentre
   si scrivono i valori fa ricostruire il volume invece di contare un errore del modulo; dopo 5 ricostruzioni in
   un minuto il filtro si ferma con un errore nel log invece di rifarlo a ogni frame.
3. Prova degli effetti uno alla volta. Il filtro è fatto di otto effetti (`FilterSettings.EffectKeys`: Colour,
   Balance, Sepia, Vignette, Grain, Aberration, Distortion, Scanlines). Un effetto mai provato con questa versione
   del gioco e della mod viene acceso da solo: prima si scrive `world/filter-check.txt` con il suo nome, dopo 2 s
   e 90 frame il file viene tolto e l'effetto segnato come buono (`Filter.Checked`, `Filter.CheckedOn` in
   `config/world.cfg`). Se all'avvio il file c'è ancora, il gioco si è fermato durante la prova: quell'effetto
   finisce in `Filter.Blocked`, resta spento, il log lo dice e la scheda mostra una riga rossa con il pulsante
   "Try … again". Conseguenza visibile: la prima volta un preset si accende un effetto ogni 2 secondi circa.
   Uscire dal mondo a metà prova annulla la prova; chiudere il gioco con Alt+F4 in quei 2 secondi può bloccare un
   effetto per errore (si rimedia con il pulsante).

Provato: compilazione di `World`. **Non provato in gioco** (richiesta dell'utente). Da guardare alla prima prova:
se VHS ferma ancora il gioco, al riavvio `LogOutput.log` e la scheda World dicono quale effetto era in prova;
le righe del log sono `[World] Filter: trying '…'` e `… runs fine`.

### Altri cappelli, baffi regolabili, posizione del naso (2026-10-11, dopo la 1.5.0) — richiesta dell'utente

- **Cappelli** (`Wardrobe.Hat`, indici 9–18): Cowboy hat, Chef hat, Beanie, Propeller cap, Sombrero, Fez, Viking
  helmet, Santa hat, Traffic cone, Mushroom. Fatti con le stesse forme di prima (cilindro/cono, sfera, mezza
  sfera, toro), stessi due colori (cappello e dettagli).
- **Baffi**: cursori sotto l'interruttore Moustache (solo se acceso): Size, Width, Height, In / out, "Ends down /
  up" (gradi: positivo punte in giù, negativo all'insù; 18 è la forma di prima) e "Original moustache".
  I baffi stanno ora in un oggetto loro (`bwmm_outfit/accessories/moustache`) che `ApplyOutfit` sposta a ogni
  frame come fa col cappello; le due metà vengono da `Wardrobe.MoustacheHalf`.
- **Naso**: tre cursori in Proportions: "Nose height" e "Nose sideways" (gradi, ±70) e "Nose in / out" (raggi di
  testa). `PlaceNose` ruota il supporto `nose_mesh` attorno al centro della testa (l'ancora dei cappelli), così il
  naso resta sulla superficie e continua a puntare in fuori. Finché i tre valori sono a zero la posizione del
  gioco non viene toccata. "Original proportions" li azzera.
- Rete: in fondo al messaggio dell'aspetto, dopo il byte della testa, 9 byte in più (cappello vero, 3 del naso,
  5 dei baffi), `WireVersion` invariato. Il byte del cappello al vecchio posto vale 0 per i cappelli nuovi: le
  versioni fino alla 1.5.0 rifiutano l'intero aspetto se il numero del cappello non lo conoscono
  (`WireReader.Int`), così invece vedono il resto dell'aspetto senza cappello. Il messaggio è di 57 byte.

Provato fuori dal gioco (programma di prova con finti tipi Unity, non nel repository): andata e ritorno del
messaggio e del file di configurazione; un aspetto come lo manda la 1.5.0 e uno come lo manda la build col D20
vengono letti bene; i valori di partenza tornano dentro la tolleranza di `PlaceNose` (0,275° su 0,5°); i dieci
cappelli hanno misure sensate (base tra 0,23 e 0,72, cima sotto 2,3 raggi), la pallina del cappello di Babbo
Natale è sulla punta del cono, il cordino del fez arriva alla nappa, i baffi con i valori di partenza sono
identici a quelli di prima.

**Non provato in gioco** (l'utente ha chiesto di non aprirlo): l'aspetto vero dei cappelli nuovi; i cursori dei
baffi; il naso — in particolare il verso di "sideways" (da che parte va il segno +), se il gioco riscrive la
posizione di `nose_mesh` da solo, e l'insieme con il dado D20 e con la testa ingrandita da Faces; tutto visto
da un amico in rete.

### Webcam dal vivo sulla testa (2026-10-11, dopo la 1.5.0) — richiesta dell'utente

Richiesta: accendere la webcam e vederla sulla testa a sfera al posto della foto, dal vivo, **visibile a tutti**
(amici su Windows), **seguendo il viso**; qualità "meh" accettata. Scelta concordata con l'utente: la webcam la
legge una pagina nel browser, non un programma nostro.

Perché così:
- Il gioco non ha `WebCamTexture`: non c'è nei metadati IL2CPP né nell'interop (controllato). Sotto Proton la
  cattura di Windows non vedrebbe comunque la camera.
- Il browser c'è già su Windows e Linux, chiede lui il permesso e ha il riconoscimento del viso pronto.

Come è fatto (tutto nella scheda Faces):
- `WebcamServer.cs` (nessun tipo Unity, thread suoi): server HTTP minimo su `127.0.0.1:37845`. `GET /` la pagina,
  `GET /status` cosa vuole la mod, `POST /frame` un fotogramma RGBA lato×lato. `/status` e `/frame` vogliono la
  chiave (`Webcam.Key` in `config/faces.cfg`, fatta una volta, sta nell'indirizzo della pagina); `Host` deve essere
  il nostro e un `Origin` di un altro sito viene rifiutato: una pagina web qualsiasi aperta nel browser non può
  mandare immagini sulla testa. Dimensione controllata prima di leggere il corpo (32–256 px, quadrato).
- `webcam.html` (risorsa incorporata nella DLL): camera, MediaPipe Face Detector (BlazeFace short range, caricato
  da jsdelivr/googleapis, versione fissata 0.10.14), riquadro ammorbidito attorno al viso, occhi tenuti in
  orizzontale, 128×128 a 15 al secondo. Il ritmo lo dà un Worker (i timer delle pagine non in primo piano vengono
  rallentati, quelli dei Worker no). Se la mod dice "spento" la pagina rilascia la camera; se il rilevatore non si
  carica usa il centro dell'immagine.
- `Webcam.cs`: interruttore **mai salvato** (spento a ogni avvio), camera richiesta solo in partita
  (`Game.InWorld`), striscia in alto finché è acceso. Il fotogramma diventa una `FaceEntry` "viva": si cuoce solo
  la texture piccola (256, senza mip) a ogni fotogramma; `ApplyFaceToPlayer` la usa per il giocatore locale al
  posto della foto finché arrivano fotogrammi (dopo 3 s senza, torna la foto).
- Rete (`WebcamCodec.cs`, messaggio 22, **non conservato**): JPEG del nostro codificatore senza i 607 byte di
  intestazione (chi riceve la rimette e passa dal solito `DecodeOwnJpeg`), al massimo 3 messaggi per fotogramma,
  qualità che si adatta per stare in 2. 8 fotogrammi al secondo = 16 messaggi/s, sotto il limite dell'host (30/s).
  Niente code: un fotogramma incompleto viene buttato all'arrivo del successivo. Zoom, avvolgimento e specchio
  viaggiano in ogni messaggio (3 byte).
- Versioni vecchie: un host o un amico senza questo tipo di messaggio lo ignora e basta (controllato in
  `Net.ServerReceive`: il tipo sconosciuto esce prima del limite di ritmo). **Correzione** a quanto detto
  all'utente a voce: un host vecchio non "silenzia" chi accende la webcam; semplicemente non la inoltra.

Provato fuori dal gioco:
- Pagina + server veri in Firefox senza finestra, con una finta camera (un ritratto di pubblico dominio che
  scorre e si inclina): 15 fotogrammi/s ricevuti, viso trovato e tenuto centrato e dritto, "nessun viso" quando
  sparisce, stop e ripartenza quando la mod spegne e riaccende.
- Server: senza chiave, chiave sbagliata, `Origin` di un altro sito, `Host` diverso → 403; dimensioni non valide →
  400; dati a caso al posto di HTTP → connessione chiusa e il server continua a rispondere; ascolta solo su 127.0.0.1.
- Codec con il codificatore vero: quei fotogrammi a 128×128 vengono qualità 55, ~1,8 KB, 2 messaggi; a 8/s sono
  ~14 KB/s; codifica 0,4 ms, decodifica 0,6 ms. 200.000 messaggi danneggiati: nessuna eccezione, nessun blocco.

**Non provato** (l'utente non vuole il gioco aperto adesso):
- tutto quello che è dentro il gioco: la texture sulla testa, il ritmo, la striscia, i pulsanti "Open the page" /
  "Copy its address" (sotto Proton l'apertura del browser può non funzionare: l'indirizzo è scritto nella scheda);
- se il server risponde dal gioco sotto Proton (socket .NET dentro Wine) — è il primo punto da guardare;
- la rete con un amico vero, il canale mai misurato, Windows;
- **la pagina con la finestra coperta dal gioco a schermo intero** (il rallentamento dei browser): la prova senza
  finestra non lo dice. Se il ritmo crolla, in gioco si legge nella scheda ("Live: N pictures a second");
- Chrome/Edge (provato solo Firefox), una webcam vera.


### Prima prova su Windows di script, filtri, cappelli, baffi, naso e webcam (2026-10-11, 10:40–11:30)

PC di prova: portatile Lenovo, Ryzen 7 4700U con grafica integrata Radeon, 16 GB (circa 1 GB libero durante la
prova), Windows 11, .NET SDK 10, Git Bash. Gioco 1.6.0 (`Application.version` `1.6.0 2609301522`) mai avviato prima
su questo PC, senza BepInEx. Finestra 1600×900: 6–12 fps appena entrati, 10–29 fps dopo che l'utente ha messo
"potato" nella scheda Performance. Sono numeri di questo PC, non della mod.

**Script per Windows**
- `dist/online/install.ps1` (quello della release 1.5.0, `irm … | iex`) sul gioco vero: trova il gioco, scarica e
  controlla il pacchetto, installa BepInEx be.785 e le dieci mod. Il modello non compilato si rifiuta di partire.
- Il primo avvio da Steam ha chiesto un UAC (script di primo avvio del gioco, non nostro); poi BepInEx ha creato
  `interop` (158 DLL) in meno di 4 minuti.
- `install-windows.ps1` / `.bat` su una finta cartella (percorso con spazi): `-DryRun`, `-Mod faces,world`, `-Yes`,
  scelta `m` dal `.bat` con la cartella passata come argomento, cartella sbagliata, mod sconosciuta: tutto giusto.
- `uninstall-windows.ps1` / `.bat`: `-Yes`, risposte a mano dal `.bat`, `-Yes -BepInEx` (i file personali finiscono
  in `BigWalkModMenu-my-files`), `-All -BepInEx`, niente installato: tutto giusto.
- Corretto: `-Game "…\Big Walk\"` (barra finale prima delle virgolette) arrivava con le virgolette e le altre
  opzioni dentro il percorso e lo script moriva con un'eccezione di `Path.Combine`; ora un errore chiaro, in tutti e
  due gli script. `uninstall … -DryRun` senza `-Yes` chiedeva conferma e, senza risposta, si annullava: ora non
  chiede, come l'installer.
- Corretto: clonando su Windows con `autocrlf` tutti gli `.sh` uscivano CRLF (quindi `install-linux.sh` e
  `install.sh` impacchettati da Windows non sarebbero partiti su Linux), e i `.bat` della 1.5.0 fatti su Linux
  avevano LF. Aggiunto `.gitattributes`.
- `bash build.sh --no-zip` e `bash build.sh` da Git Bash funzionavano già con `GAME_DIR` (circa 75 s; gli zip li ha
  fatti `python3`). Aggiunti: cartella di Steam di Windows come ripiego, controllo che `python3` parta davvero,
  `tar.exe` di Windows come ultimo modo di fare gli zip (provato a mano: zip valido). README aggiornato.
- `tools/dev.ps1`: gemello di `dev.sh`, usato per tutta la prova. `dev.sh` su Windows non è stato provato.

**Ponte di prova**
- Su questo PC il gioco è entrato nel mondo da solo dopo l'avvio, senza i comandi di menu della voce sul D20
  (`MicCheckMenu`/`TitleMenu` non erano attivi). Non è chiaro se sia il gioco al primo avvio o l'utente al mouse.
- Le DLL delle schede con `DevCommand` (Camera, Solo, Voice, ora Faces) vanno compilate anche loro con `-p:Dev=1`,
  altrimenti `mod camera selfie` risponde `not handled`. Le chiavi dell'aspetto sono `P1.`…`P4.`, non `P0.`.
- Nuovo `awake`: il personaggio fermo si addormenta (`PlayerSleeper`) e il gioco sfoca e scurisce l'immagine; è
  lo "schermo oscurato" della prova del D20. `awake` mette `preventSleeping`.
- Nuovo `mod faces webcam on|off`: accende l'interruttore come il menu e dice server, fotogrammi, ritmo.
- Trovato con il ponte, non dal menu: rileggere le impostazioni a filtro acceso (`set world Filter.…`) non
  aggiornava l'immagine se nessun effetto si accendeva o spegneva (Warm → Cool restava Warm), e durante la prima
  prova di un effetto la nota su disco veniva presa per quella di un blocco. Corretti tutti e due in `World`.

**1. Filtri (World)** — tutti e nove i preset, due giri (il secondo dopo le correzioni, col personaggio sveglio).
- Log: `trying` / `runs fine` per tutti e otto gli effetti, nell'ordine Colour, Sepia, Vignette, White balance,
  Grain, Chromatic aberration, Lens distortion, Scanlines; `Filter.Blocked` vuoto, `Filter.Checked` con tutti e otto,
  `Filter.CheckedOn=1.6.0 2609301522/1.5.0`. Un solo `Filter volume created on layer 12`, nessuna ricostruzione.
- **VHS e Vintage non hanno bloccato né chiuso il gioco**, né alla prima accensione né riaccesi; fps invariati
  (19–27 con VHS). Nelle foto: righe TV e frange di colore visibili con VHS, vignetta e toni caldi con Vintage,
  bianco e nero, seppia, Dream, Warm, Cool e Vivid diversi tra loro, `None` torna all'immagine normale.
- **La causa del blocco segnalato dall'utente non è stata trovata**: su questo PC non si riproduce. Resta aperta
  l'ipotesi che dipenda dal PC o dal sistema su cui era stato visto.
- A 10 fps la prima accensione di un preset dura più dei "2 secondi a effetto": la prova aspetta anche 90 frame
  (VHS, cinque effetti, circa un minuto).
- Non provati: i pulsanti dei preset cliccati a mano, il pulsante "Try … again", un blocco vero a metà prova.

**2. Character** — selfie da davanti, uno scatto per cappello.
- I dieci cappelli nuovi (indici 9–18) compaiono e hanno la forma attesa: cowboy con tesa larga, cuoco, berretto
  con pompon, berretto con elica, sombrero, fez con cordino, elmo vichingo con corna, Babbo Natale con pompon in
  punta, cono stradale con fascia, fungo con macchie. Colori di partenza nero e rosso.
- Baffi: Size 180 % più grandi, Width 170 % più larghi, Height ±0,35 su e giù, In / out +0,4 in fuori e −0,2
  dentro la testa (spariscono, come atteso), Ends +50° punte in giù e −40° all'insù, 18° uguale a prima.
  **Difetto non corretto:** sotto circa il 70 % di Size i baffi spariscono dietro il naso (a 50 % non si vedono
  da davanti), perché rimpiccioliscono verso il loro centro che sta sotto il naso.
- Naso: Height +40° sale e −40° scende; In / out +0,6 esce, −0,3 rientra. **Il segno + di "Nose sideways" porta il
  naso verso la destra del personaggio** (a sinistra nell'immagine guardandolo in faccia), il − verso la sua
  sinistra. Rimettendo i tre valori a zero il naso torna al suo posto, anche dopo il massimo (70°, 70°, 1);
  il gioco non ha riscritto la posizione nei 20 frame tra un comando e lo scatto.
- Con il dado D20: cappello di Babbo Natale alzato sopra il dado, baffi sulla faccia davanti, naso spostato
  (side 30°, up 20°) e rimesso a posto. `faults=0` per tutte le schede, nessuna eccezione nel log.
- Non provati: i cursori mossi a mano nel menu, "Original moustache" / "Original proportions", la testa
  ingrandita da Faces, tutto visto da un amico in rete.

**3. Webcam (Faces)** — prova interrotta: l'utente ha chiuso il gioco per continuare su un altro PC.
- A interruttore spento la porta 37845 è chiusa. Acceso: `[Webcam] Page ready on 127.0.0.1:37845`, il gioco ascolta
  solo su 127.0.0.1; `GET /` 200 (13.740 byte), `/status` senza chiave 403, con la chiave
  `{"on":true,"side":128,"fps":15}`, con `Origin` o `Host` di un altro sito 403.
- Pagina aperta in **Chrome** (`chrome.exe --new-window`, non con il pulsante "Open the page"): i fotogrammi sono
  arrivati entro 8 secondi, viso trovato (`face=True`). Il video è sulla testa in selfie; in terza persona si vede
  la nuca col colore del bordo; striscia "WEBCAM LIVE — friends see you" in alto.
- Ritmo: **6,4 fotogrammi al secondo ricevuti** in 25 s (la scheda segnava 5,8; verso gli amici 3,4–5,8), con il
  gioco sceso a **6 fps** mentre girano insieme gioco, Chrome e riconoscimento del viso. Il server risponde subito,
  quindi è la pagina a mandarne meno dei 15 chiesti; la spiegazione più probabile è che su questo PC la grafica
  integrata sia già tutta del gioco, ma non è stato misurato quanto pesa il riconoscimento del viso e quanto il resto.
- **Non provati:** il ritmo con la finestra del browser coperta dal gioco e con il gioco a schermo intero (la
  misura era partita quando il gioco è stato chiuso); spegnere e riaccendere l'interruttore; uscire dalla partita
  e vedere la camera fermarsi; Edge; i pulsanti "Open the page" / "Copy its address"; i cursori Size / Wrap /
  Mirror; **la rete con un secondo giocatore** (serve un amico: non provata).

Alla fine: build normale rifatta con `bash build.sh` e installata con `install-windows.ps1 -Yes` (nessuna stringa
del ponte nella DLL di Core, DLL installate identiche a quelle del pacchetto), cartella `dev` tolta. Prima della
prova non c'era nessuna cartella `BepInEx/plugins/BigWalkModMenu`: nessuna copia di sicurezza da fare. Il numero di
versione resta 1.5.0.

## 1.6.0 (2026-10-11) — rilascio di tutto quello che è venuto dopo la 1.5.0

Richiesta dell'utente: una release nuova con tutti i cambiamenti. Nessun codice nuovo in questo passaggio: solo
il numero di versione (`Directory.Build.props`, `ModMenuApi.Version`, titolo del README del pacchetto) e il
changelog, dove "Unreleased" diventa "1.6.0" con due righe in più per le correzioni fatte su Windows (filtri di
World riletti mentre sono accesi; installer di Windows).

Dentro ci sono: webcam dal vivo (Faces), dieci cappelli, baffi regolabili, posizione del naso, testa a D20
(Character), la correzione dei filtri che fermavano il gioco (World), i fumetti tradotti (Extras), gli installer
da una riga, `build.sh` e `tools/dev.ps1` su Windows.

Visto in gioco prima del rilascio (2026-10-11, 11:36–11:43, Linux/Proton, build normale dello stesso sorgente con
ancora il numero 1.5.0, partita ospitata dall'utente): le 11 schede si registrano, tutte le patch "OK", nel log
nessun errore o avviso della mod; il server della webcam parte dentro Proton
(`[Webcam] Page ready on 127.0.0.1:37845`), che era il primo punto dubbio della voce sulla webcam. Gli unici
errori nel log sono quelli del gioco alla chiusura (Rewired, Steamworks, lobby).

**Non provato:** la build 1.6.0 in sé non è stata avviata (cambia solo il numero rispetto a quella provata).
Cappelli, baffi, naso, filtri e webcam sono stati provati su Windows (voce qui sopra, arrivata nel repository
mentre si preparava questo rilascio): valgono i suoi "non provati" e i suoi difetti aperti — i baffi che
spariscono dietro il naso sotto circa il 70 % di Size, la causa del blocco dei filtri non trovata, la webcam a
circa 6 fotogrammi al secondo su quel PC. Mai provata la rete con un amico, né con questa versione né con la 1.5.0.

Nota sul tag: `v1.6.0` punta al commit del rilascio fatto prima di unire la voce su Windows (solo diario); il
codice è lo stesso.
