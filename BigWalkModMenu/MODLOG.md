# MODLOG — Big Walk Mod Menu

Giornale del progetto. I fatti sulla mod delle facce restano in `../BigWalkFaceMaskMod/MODLOG.md`.

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
  **Non provato: l'interfaccia inglese vista in gioco** (testi più lunghi potrebbero uscire tagliati) e gli script PowerShell.
