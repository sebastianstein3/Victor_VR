# CLAUDE.md

Projekt właściciela: **Vision Pro DIY** – zestaw mixed reality z iPhone'a 16 Pro, gogli Shinecon G10
i AirPods Pro 2, na silniku [CardboardXR](https://github.com/brenpoly/CardboardXR) (Unity 6000.6.8f1,
Apache-2.0, w historii jako remote `upstream`). Użytkownik pisze po polsku; odpowiadaj po polsku.
Zacznij od przeczytania `AUDIT_PL.md` (stan prac, co naprawiono, co zostało) i `VisionPro/HANDOVER.md`
(opis funkcji i reguł). Instrukcje bazowego projektu: `README.md`, `ADDING_A_DEMO.md`, `KNOWN_ISSUES.md`.

## Co jest nasze, a co bazowe
- **Nasze:** `Assets/_Project/Demos/Workspace/` (demo Spatial Workspace: okna, klawiatura i gładzik biurkowy,
  telefon, dyktowanie, tryb kinowy, dźwięki), `Assets/_Project/Scripts/Hands/SpatialGestureDetector.cs`,
  `Assets/Plugins/iOS/CardboardXRSpeech.mm` (dyktowanie), `Assets/Editor/ProjectBootstrap/IosSpeech.cs`,
  `VisionPro/` (dokumentacja, profil QR Shinecon G10), `AUDIT_PL.md`.
- **Bazowe (nie ruszaj bez powodu, ułatwia to aktualizacje z `upstream`):** reszta repozytorium.
  Demo ma być samodzielne w swoim folderze i nie edytować wspólnych plików (poza `M3Icons.cs`, jeśli trzeba ikonę).

## Środowisko
- Praca w chmurze: edytujesz kod i pushujesz do `origin` (`sebastianstein3/Victor_VR`, gałąź `main`).
  **Nie ma tu Unity ani Xcode**: nic nie da się skompilować, uruchomić ani przetestować. Mów to uczciwie
  i sprawdzaj kod czytaniem względem API bazy (np. `M3Theme.Type` ma tylko: HeadlineSmall, TitleLarge,
  TitleMedium, BodyLarge, BodyMedium, LabelLarge; ikony tylko z `M3Icons.cs`).
- Budowanie i testy robi użytkownik na Macu mini (Unity + Xcode): `Tools/ios_build_and_run.sh`,
  albo GitHub Actions (`.github/workflows/ios-ipa.yml`).
- Repo trzyma **LF** (`.gitattributes`). Nie commituj `Library/`, `Builds/`, kluczy ani tokenów.
- Nowe pliki w Unity wymagają plików `.meta` wygenerowanych przez Unity i zacommitowanych. Sceny
  `Workspace.unity` jeszcze nie ma: powstaje z menu **CardboardXR > Make Missing Demo Scenes** na Macu.

## Reguły kodu demo Workspace
- UI buduj przez `WorldUi.Panel` i `M3.*`; klasy `VirtualScreen`, `SpatialPhoneScreen`, `DeskKeyboard`,
  `DeskTrackpad` montuj **na obiekcie panelu** statyczną metodą `Create()` (żeby `IsChildOf` działało).
- Jednostki UI to dp. Odległość dotyku blatu: `Mathf.Abs(local.z * lossyScale.x) < 0.035f` (metry).
- Przyciski mają mieć pełne 56 dp (`M3.k_ButtonHeight`), odstępy 8 dp; unikaj nakładania.
- Rozmieszczaj panele **kątowo** (1 dp ≈ 0,08° w każdej odległości): okno 520 dp ≈ 41°. Układ i stałe są
  w nagłówku `WorkspaceDemo.cs`; panele nie mogą na siebie nachodzić (bliższy zabiera wzrok).
- Kliknięcie przycisku ma w `PinchRouter` pierwszeństwo przed `IPinchHandler`, a wzrok trafia w dziecko
  „Container” przycisku – nie wykrywaj przycisków po nazwie w `TryBeginPinch`.
- Kamera widzi dłonie tylko ±21° w pionie od kierunku głowy (`KNOWN_ISSUES.md`).
- Panele w pokoju trzymają kotwice ARKit (`WorkspaceAnchors`): przed przesunięciem panelu z kodu wołaj
  `Release`, a przy wyjściu z dema `ReleaseAll` przed niszczeniem paneli (kotwice są w scenie Core).
- Wykryte powierzchnie: warstwa 30, tylko poziome, collider z górną ścianą = blat.
- Czcionka UI (Roboto) nie ma emoji ani `⌫ ⇧ ↵ ─`: używaj zwykłego tekstu / ikon z `M3Icons`.
- Komponenty dodane na `gameObject` dema niszcz przez `Destroy(komponent)`, nie `.gameObject`.
- Nic w projekcie nie miało dźwięku: `WorkspaceDemo` dodaje `AudioListener` na głowie. Dźwięki przycisków
  przez `WorkspaceSounds.Attach`.
- Dyktowanie: `VoiceDictation` jest prawdziwe tylko na iOS; poza tym to atrapa oznaczona `[głos-demo]`.
- Messenger/TikTok/Pulpit PC w telefonie i oknie to nadal atrapy (do zrobienia: WebView, WebRTC/Moonlight).

## Po zmianie
Krótko zaktualizuj `AUDIT_PL.md` (co zrobiono / co zostało) i commituj po polsku lub angielsku, małymi
commitami. Przed `git push` upewnij się, że remote `origin` wskazuje na `Victor_VR`.

## Narzędzia z menu „Więcej” (dok w demo Workspace)
Nic z tego nie było kompilowane ani uruchamiane (patrz `AUDIT_PL.md`, sekcja „Narzędzia z menu Więcej”).
- **Skrypty** (`Assets/_Project/Demos/Workspace/Scripts/`): `LiveCaptions.cs` (napisy + tłumaczenie),
  `Widgets.cs` (Zegar, Minutnik, Notatka, Pogoda, Kalendarz; zapis w `workspace.json`), `PdfView.cs`,
  `EnvironmentSphere.cs` (środowiska 360), `ModelViewer.cs` (modele 3D), `SpatialMediaView.cs`
  (zdjęcia i filmy przestrzenne), `FilmPlayer.cs` (filmy, ekran kinowy), `FileImport.cs` (Pliki: import z
  iCloud / okna wyboru plików). Otwiera je przycisk „Więcej” w docku; głosem: napisy, zegar, minutnik,
  notatka, pogoda, kalendarz, pdf, środowisko / pokój, modele, zdjęcia 3d, filmy, pauza, pliki
  (`WorkspaceDemo.TryVoiceCommand`).
- **Wtyczki natywne** (`Assets/Plugins/iOS/`): `CardboardXRTranslate.swift` (Translation, iOS 26.4+, języki
  pobiera się w Ustawieniach iOS), `CardboardXRPdf.mm` (PDFKit), `CardboardXRCalendar.mm` (EventKit),
  `CardboardXRSpatial.swift` (HEIC przestrzenne i MV-HEVC, filmy od iOS 17), `CardboardXRFiles.mm`
  (UIDocumentPicker, zakładka połączonego folderu, kopiowanie do folderów wg rodzaju). Frameworki, uprawnienia
  (kalendarz, mowa, sieć lokalna) i `GltfShaders.cs` (shadery glTFast do builda) są w
  `Assets/Editor/ProjectBootstrap/`.
- **Foldery w Documents** (Finder: iPhone > Pliki > aplikacja, aplikacja Pliki albo „Więcej > Pliki”): `PDF`, `Filmy`, `Inne`, `Srodowiska`
  (equirect .jpg/.png lub 360 .mp4/.mov), `Modele` (.glb/.gltf), `Przestrzenne` (HEIC, MV-HEVC, `_sbs`),
  `pogoda.txt` (miasto, domyślnie Warszawa).
- **Pakiety opcjonalne** włączają kod przez `versionDefines` w asmdef dema: `CXR_GLTF`
  (`com.unity.cloud.gltfast`, dodaj po nazwie w Package Managerze; bez niego „Modele 3D” się nie zbudują
  w pełni) i `CXR_WEBRTC` (`com.unity.webrtc`, płynny pulpit PC).
- Ograniczenia: Translation tylko iOS 26.4+, filmy przestrzenne iOS 17+; pamiętaj o `Destroy(komponent)` i
  `WorkspaceAnchors` przy nowych panelach.
