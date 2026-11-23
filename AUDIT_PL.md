# Audyt projektu Vision Pro (2026-11-23)

Zakres: moduł **Spatial Workspace** (`Assets/_Project/Demos/Workspace/`), `SpatialGestureDetector.cs`,
wpis w `DemoCatalog.asset`, skrypt profilu QR i dokumentacja (`VisionPro/`).
Reszta repozytorium to bazowy projekt [brenpoly/CardboardXR](https://github.com/brenpoly/CardboardXR)
(Apache-2.0), którego nie ruszałem.

> **Ważne ograniczenie:** audyt wykonano czytając kod i porównując go z API bazowego projektu.
> Nie miałem Unity, więc **nic nie zostało skompilowane ani uruchomione**. Poprawki są drobne i
> sprawdzone tylko czytaniem; pierwszy krok na Twoim komputerze to otwarcie projektu w Unity 6000.6.8f1
> i sprawdzenie konsoli.

## 1. Co naprawiłem

| # | Problem | Skutek | Plik |
|---|---|---|---|
| 1 | Użyte nieistniejące style tekstu `M3Theme.Type.LabelMedium / LabelSmall / BodySmall` (w bazie są tylko `HeadlineSmall, TitleLarge, TitleMedium, BodyLarge, BodyMedium, LabelLarge`) | **Błąd kompilacji** – projekt się nie zbudował (8 miejsc) | `VirtualScreen`, `DeskTrackpad`, `SpatialPhoneScreen`, `RemoteDesktopReceiver` |
| 2 | Asmdef testów nie odwoływał się do `CardboardXR.Hands`, a testy używają `HandGeometry` / `HandPose` | Błąd kompilacji testów | `Tests/…Workspace.Tests.asmdef` |
| 3 | `TogglePlayPause` ustawiał `Label.text` przycisku-ikony (`IconButton` nie ma etykiety → `null`) | `NullReferenceException` po kliknięciu pauzy w telefonie | `SpatialPhoneScreen` |
| 4 | `microphoneUsageDescription` puste w ProjectSettings | iOS **zamyka aplikację** przy pierwszym użyciu mikrofonu (przycisk „Głos”) | `ProjectSettings.asset` |
| 5 | `OnDemoExit` robił `Destroy(m_Cinema.gameObject)` i `Destroy(m_Voice.gameObject)`, a te komponenty siedzą na obiekcie samego dema | Niszczony cały korzeń dema zamiast komponentu; mikrofon mógł zostać włączony | `WorkspaceDemo`, `CinemaDimmer`, `VoiceDictation` |
| 6 | Klawiatura biurkowa pisała znak co 0,22 s, dopóki palec spoczywał na klawiszu (brak wykrywania „wejścia” w klawisz) | Jedno stuknięcie = „aaaaaa” | `DeskKeyboard` |
| 7 | Rzędy klawiszy co 50 dp, a przyciski mają 56 dp; pole dotyku 44 dp | Klawisze nachodziły na siebie, trafianie w złe klawisze | `DeskKeyboard` |
| 8 | Etykiety `⌫ ⇧ ↵` nie istnieją w czcionce Roboto (sprawdzone w pliku TTF) | Puste kwadraty zamiast klawiszy Del/Shift/Enter. Zamienione na tekst. Etykiety klawiszy teraz zmieniają wielkość liter z Shiftem (komentarz to obiecywał, kod nie robił) | `DeskKeyboard` |
| 9 | Tekst w notatniku i w czacie Messengera był obcinany wielokropkiem od końca | Po kilku liniach **nie widać tego, co się pisze** (kursor i szkic wiadomości na końcu znikały) | `VirtualScreen`, `SpatialPhoneScreen` |
| 10 | Emoji w tekstach (💬 🎤 ✔️ 🚀 itd.) i `───` – brak w Roboto | Puste kwadraty | `WorkspaceDemo`, `SpatialPhoneScreen` |
| 11 | `RawImage` pulpitu PC miał `raycastTarget=true` i leżał nad przyciskami | Zamknij / Przypnij / Wyczyść / Chwyć nie reagowały na ekranie „Pulpit PC” | `RemoteDesktopReceiver` |
| 12 | Aktywny ekran zmieniał się tylko przez pisanie do niego (czyli już aktywnego) albo machnięcie | Nie dało się „kliknąć” drugiego monitora, żeby pisać na nim | `WorkspaceDemo.TryBeginPinch` |
| 13 | Gest „otwarta dłoń” przełączał dock/klawiaturę przy **każdej** zmianie pozy; ThumbsUp dopisywał tekst przy każdym mignięciu | Dock znikał/pojawiał się losowo (poza trackera migocze) | `WorkspaceDemo` (teraz trzeba trzymać pozę 0,5 s, raz na trzymanie) |
| 14 | Machnięcia (Swipe) rozpoznawane zawsze, także podczas przeciągania okna (Pinch), pisania (Pointing) i pięści | Sięganie po biurko / przesuwanie okna przełączało ekrany i filmy | `SpatialGestureDetector` |
| 15 | Historia czatu Messenger ginęła po wyjściu z aplikacji (kopia struktury) | Rozmowa znikała po powrocie na pulpit | `SpatialPhoneScreen` |
| 16 | `Quaternion.LookRotation(zero)` przy patrzeniu prosto w górę/dół | Ostrzeżenie i losowa rotacja ekranów | `WorkspaceDemo` |
| 17 | Ekran „Pulpit PC” pisał na zielono „Połączono z PC, 60 FPS”, choć nic nie jest połączone | Wprowadzało w błąd – teraz „TRYB DEMO: brak strumienia” | `RemoteDesktopReceiver` |
| 18 | Dyktowanie wstawia gotowe zdania niezależnie od tego, co powiesz | Oznaczone `[głos-demo]` | `WorkspaceDemo` |
| 19 | `DemoCatalog.asset` zapisany z CRLF → diff całego pliku; w repo dodany `.gitattributes` (LF) | Konflikty końców linii między urządzeniami | repo |

## 1b. Druga tura (funkcje i układ)

| Co | Jak działa teraz |
|---|---|
| **Gładzik / mysz klikają** | Stuknięcie jednym palcem w gładzik (lub lewy przycisk myszy) klika przycisk pod kursorem na aktywnym oknie (Zamknij, Przypnij, Wyczyść, Chwyć). Stuknięcie dwoma palcami / prawy przycisk = przypnij/odepnij okno. Okno pod kursorem dostaje fokus. Kod: `VirtualScreen.ClickAtCursor / ContextClickAtCursor`. |
| **Układ okna** | Okno 520×440 dp, wszystkie przyciski mają pełne 56 dp, rzędy (nagłówek / treść / stopka / chwyt) nie nachodzą na siebie; obraz pulpitu PC wypełnia obszar treści. |
| **Układ telefonu** | Przyciski ≥48–56 dp, siatka aplikacji i sterowanie odtwarzaniem bez nakładania, uchwyt „Przenieś” mieści się w obudowie. |
| **Dock** | Szerokość przycisków liczona z etykiet (`M3.ButtonWidth`), odstępy 8 dp, panel dopasowany do przycisków; bez ikon, żeby był wąski. |
| **Chiralność gestów** | Kierunek dłoni liczony przez `PalmGesture.Facing` z bazowego projektu (obraz z kamery + lewa/prawa dłoń, sprawdzone tam na telefonie). Rezerwowo: 3D z poprawnym znakiem dla każdej dłoni. 3 nowe testy jednostkowe (sprawdzone symulacją w Pythonie). |
| **Prawdziwe dyktowanie (iOS)** | `Plugins/iOS/CardboardXRSpeech.mm`: `SFSpeechRecognizer` + `AVAudioEngine`, język `pl-PL`, mikrofon iPhone'a lub AirPods; wynik trafia do aktywnego okna albo czatu. `Editor/ProjectBootstrap/IosSpeech.cs` dodaje `NSSpeechRecognitionUsageDescription` do Info.plist i dołącza `Speech.framework`. Poza iOS (edytor, Android) zostaje atrapa z oznaczeniem `[głos-demo]`. Błędy (brak zgody, brak internetu) pokazują się w oknie. |

**Czego dyktowanie nie sprawdzono:** kodu Objective-C nie kompilowałem ani nie uruchamiałem (brak Xcode/iPhone'a).
Przy pierwszym użyciu iOS zapyta o zgodę na mikrofon i rozpoznawanie mowy. W trakcie nagrywania AirPods
przechodzą na jakość „rozmowy" (tak jest w każdej aplikacji, która nagrywa). Tryb offline: ustaw
`VoiceDictation.OnDeviceOnly = true` (wymaga pobranego języka dyktowania w iOS).

## 1c. Trzecia tura: przegląd trzema niezależnymi recenzentami (2026-11-23)

| Problem | Skutek | Naprawa |
|---|---|---|
| Dock (pitch -22°, 1,2 m) leżał kątowo **za klawiaturą** | Przycisków docka nie dało się wskazać wzrokiem (bliższa klawiatura zabierała trafienie) | Nowy układ wg kątów (poniżej) |
| Gładzik w 0,28 m na prawo nachodził na klawiaturę (ta ma 0,33 m pół-szerokości) | Stuknięcie w L/Enter klikało też gładzik | Gładzik obok klawiatury (44° yaw ≈ 34° łuku) |
| Okna co 28°, a okno ma ~41° szerokości; trzecie pod telefonem | Okna zasłaniały sobie przyciski | Sloty co 46°: 0°, -46°, -92°, +86°, +132°, potem rząd wyżej; telefon +40° |
| Przeciąganie uszczypnięciem nigdy nie startowało (wzrok trafia w „Container” przycisku, a przyciski mają pierwszeństwo w `PinchRouter`), a okno w trakcie przeciągania centrowało się na wzroku, uciekając z uchwytem | **Nie dało się upuścić okna** | Okno niesione w tym miejscu widoku, gdzie było; uszczypnięcie gdziekolwiek je upuszcza |
| Gest STOP chował też dock | Po przypadkowym geście brak sterowania | STOP chowa tylko klawiaturę i gładzik; „Reset biurka” je przywraca |
| Machnięcia liczone w przestrzeni świata | **Obrót głowy przełączał ekrany** | Prędkość nadgarstka względem głowy, minimalna droga 10 cm, cisza 1 s po stuknięciu w biurko, ignorowane z ręką nisko |
| Strona dłoni z surowego odczytu klatka po klatce | Mignięcia zamieniały „dłoń do mnie/od siebie” | Wygładzanie jak w `HandSubsystemFeed` |
| Pięść wymagała schowanego kciuka | Prawdziwa pięść rozpoznawana jako nic/kciuk w górę | Kciuk bez znaczenia (po teście kciuka w górę); nowy test |
| Uszczypnięcie bez histerezy i z „zamrożonym” `PinchDistance` | Migotanie / zatrzaśnięty pinch | Puszczenie dopiero > 4 cm, `PinchDistance` tylko gdy `ShapeFits` |
| Dyktowanie: stary 4-sekundowy strażnik i spóźnione callbacki | Mogły przerwać **następne** dyktowanie i pokazać fałszywy błąd | Licznik generacji w pluginie |
| Metody odbierające wynik z iOS mogły zostać wycięte (stripping level 4, IL2CPP) | Dyktowanie „nic nie robi” | `[Preserve]` |
| Połączenie / Siri / wyjęcie AirPods w trakcie | Wieczne „nagrywanie” | Obsługa przerwań i zmiany trasy audio |
| Zamknięcie dema w trakcie pytania o zgodę | Dyktowanie już nigdy nie startowało | Reset stanu zgody |
| Tekst na ekranie „Pulpit PC” trafiał do ukrytego pola | Pisanie i dyktowanie „znikały” | Idą do odbiornika PC (w trybie demo widać je na pasku stanu) |
| Wpisany/podyktowany „<” psuł znaczniki TMP | Rozjechany tekst | `<noparse>` |
| Czat zapisywany w statycznej tablicy | Wiadomości wracały po ponownym wejściu do dema | Kopia na instancję telefonu |
| Utrata śledzenia dłoni zostawiała stan dotyku | Skok kursora / klawisz „zablokowany” | Reset stanu |
| Demo prosiło o skan powierzchni, z którego nic nie korzysta | Koszt CPU i pauza śledzenia dłoni | `ScanSurfaces = false` (włączyć przy „klawiaturze na prawdziwym blacie”) |

**Ograniczenie fizyczne (z `KNOWN_ISSUES.md` bazy):** kamera widzi dłonie tylko ±21° w pionie od kierunku
głowy, a autor bazy celowo zrezygnował ze „stukania palcem”. Klawiatura palcem działa więc, **gdy patrzysz
na klawiaturę**; do pisania bez patrzenia używaj klawiatury Bluetooth. Przy ~15 cm od kamery model dłoni się sypie.

## 2. Czego NIE naprawiłem (wymaga Twojej decyzji lub urządzenia)

1. **Brak plików `.meta` dla nowych plików** (`Demos/Workspace/**`, `SpatialGestureDetector.cs`) i brak sceny
   `Workspace.unity`. Otwórz projekt w Unity, wybierz **CardboardXR > Make Missing Demo Scenes**
   i **zacommituj wszystkie wygenerowane `.meta` oraz `Workspace.unity`**. Bez tego na każdym
   urządzeniu Unity wygeneruje nowe GUID-y, a referencje się rozjadą.
2. ~~Chiralność gestów~~ – naprawione w drugiej turze (1b); **sprawdź na iPhonie** obie dłonie.
3. ~~Trackpad nic nie klikał~~ – naprawione (1b). Klika tylko przyciski okien; w oknach nie ma jeszcze
   innych elementów do klikania.
4. **Funkcje-makiety** (HANDOVER sam to przyznaje w roadmapie, ale opis w części 3 brzmi jak gotowe):
   Messenger / TikTok / Shorts / Reels / Twitch / Notatki to **teksty-atrapy**, nie prawdziwe aplikacje;
   Zdalny pulpit rysuje sztuczną teksturę. Prawdziwe wersje wymagają WebView i WebRTC/Moonlight.
   (Dyktowanie jest już prawdziwe na iOS, patrz 1b.)
5. **Dokładność pisania w powietrzu**: próg 3,5 cm od płaszczyzny klawiatury przy głębi szacowanej
   z jednej kamery (błąd rzędu centymetrów) – w praktyce będzie nierówno. Bluetooth-klawiatura działa lepiej.
6. ~~Układ okien, telefonu i docka~~ – przeprojektowane w drugiej turze (1b); **oceń w goglach** czy rozmiary pasują.
9. **Profil optyczny Shinecon G10 jest zgadywany**, nie zmierzony: skrypt ma 44 mm / 63 mm / FOV 90° /
   k1=0,34 k2=0,12, a dokumentacja (PLAN, HANDOVER, karta HTML) 42 mm / 62 mm / FOV 95° / k1=0,32 k2=0,22.
   Wartości się nie zgadzają, a HANDOVER nazywa profil „oficjalnym” – nie jest. Pola protobuf w kodzie QR
   zgadzają się z dekoderem aplikacji (`ViewerParamsCodec`). Zmierz soczewki (rozstaw, odległość od ekranu)
   i dopasuj dystorsję „na oko” (siatka), potem zaktualizuj skrypt i dokumentację.
10. **Sprzęt**: założenia o Shinecon G10 (zdejmowana pokrywa odsłaniająca aparaty i LiDAR, miejsce na cooler)
    pochodzą z dokumentacji – nie mogłem ich zweryfikować. Linki do ofert to wyszukiwania.
11. **WebView/wideo, logowanie do kont**: w dokumentacji jest „po kliknięciu otwiera platformę” – obecnie
    nic nie otwiera. Pamiętaj też o regulaminach serwisów, jeśli dodasz osadzanie.

## 3. Praca na wielu urządzeniach (GitHub)

- Repo to klon `brenpoly/CardboardXR` z historią; zdalne `origin` zmieniono na `upstream`, żeby
  można było pobierać aktualizacje autora (`git fetch upstream`). Licencja Apache-2.0 zostaje
  (plik `LICENSE`); Twoje zmiany są w osobnych commitach.
- Twoje pliki z folderu nadrzędnego (HANDOVER, PLAN, QR, skrypt) są w `VisionPro/`, a linki w nich
  zamienione z `file:///d:/…` na względne.
- `.gitattributes`: repo trzyma LF na wszystkich systemach (Twoje Windowsowe CRLF nie zrobi już
  szumu w diffach, a skrypty `.sh` zadziałają na Macu).
- Dla iPhone'a: `Tools/ios_build_and_run.sh` wymaga Maca z Xcode/Unity; z Windowsa/telefonu można
  zbudować `.ipa` przez GitHub Actions (`.github/workflows/ios-ipa.yml`, sekrety `UNITY_LICENSE`,
  `UNITY_EMAIL`, `UNITY_PASSWORD`; plik buduje niepodpisane `.ipa` po wypchnięciu tagu `vX.Y.Z`).

## 4. Możliwości rozbudowy (priorytety)

**Wysoki – realna wartość, w zasięgu:**
1. ~~**Klawiatura na prawdziwym blacie**~~ **zrobione**: przycisk „Na blat” w docku (albo komenda
   „na blat” wypowiedziana patrząc na stół), potem uszczypnięcie z wzrokiem na stole: klawiatura i
   gładzik kładą się płasko na wykrytym blacie (15–95 cm pod oczami), więc palec stuka w prawdziwy
   stół. Skan pokoju (`ScanSurfaces`) znów włączony; stół musi zostać wykryty (rozejrzyj się po nim).
   Pierwotny opis: Bazowy `RoomBoundary` (+ `ScanSurfaces`) wykrywa stoły,
   `AnchorPlacer` przypina obiekty do pokoju, a `DepthSampler` (LiDAR) mierzy głębię pod palcem. Położenie
   klawiatury płasko na wykrytym blacie dałoby prawdziwy „opór” stołu i lepsze wykrywanie stuknięć.
2. ~~**Okna przypięte do pokoju**~~ **zrobione (w trakcie działania)**: `WorkspaceAnchors` – każde
   nieruchome okno, telefon, klawiatura i gładzik ma kotwicę ARKit; przenoszone puszczają ją i dostają
   nową po odłożeniu. **Zapis między uruchomieniami: zrobione częściowo** – `Documents/workspace.json` pamięta okna
   (tytuł, notatki, PC, slot, rozmiar, przypięcie), telefon i język dyktowania; po ponownym wejściu
   wracają wokół użytkownika (sloty, nie dokładne miejsca w pokoju – to wymaga ARWorldMap). Pierwotny opis: (kotwice ARKit przez `AnchorPlacer`) zamiast względem głowy przy starcie;
   później zapis układu między uruchomieniami (dziś baza nie ma trwałości kotwic: PlayerPrefs + ARWorldMap).
3. ~~**Prawdziwe wideo w telefonie**~~ **zrobione (telefon)**: filmy z folderu `Documents/Klipy` na iPhonie
   (kopiuj przez Finder lub aplikację Pliki; .mp4/.mov/.m4v, najlepiej pionowe) grają w TikTok/Shorts/
   Reels/Twitch; machnięcie w górę/dół zmienia klip, dźwięk z przestrzennego `AudioSource` telefonu.
   Bez plików zostają teksty-atrapy. ~~Linki Google Drive~~ **zrobione**: `Documents/Klipy/linki.txt`
   (link w linii, opcjonalnie nazwa; plik powstaje z instrukcją), pobierane raz przez `DriveDownloader`
   z dema Video (Workspace odwołuje się do jego assembly). ~~Wideo w dużym oknie~~ **zrobione**: w trybie „Kino” klip grający w telefonie
   leci też na dużym ekranie 16:9 (~57°, 1,3 m) przed użytkownikiem, pokój przyciemniony. Pierwotny opis:: demo Video ma gotowe `VideoLibrary`, `DriveLink`,
   `DriveDownloader` i `VideoPlayer` (pliki z telefonu i linki Google Drive). Zamiast atrap TikTok/Shorts:
   własne klipy pionowe.
4. ~~Ostrość dłoni przy pisaniu~~ – **już działa**: `PassthroughHandFocus` jest w scenie Core bazy i
   działa w każdym demie.
5. ~~Podświetlenie klawisza pod palcem~~ – **zrobione**: klawisz pod palcem świeci się od 8 cm nad
   płaszczyzną klawiatury, zanim stuknięcie go wciśnie.

**Średni:**
6. ~~Rozmiar okien~~ **zrobione**: przyciski „-” / „+” w stopce okna (0,75–1,25×, więcej by zasłaniało
   sąsiednie okno). ~~Wyśrodkowanie~~ **zrobione**: przycisk „Wyśrodkuj” w docku (i komenda głosowa
   „wyśrodkuj”) ustawia okna, telefon, dock i biurko przed użytkownikiem. Zostało: przełącznik układów
   (`M3.Segments`), dock jako `M3Toolbar`.
7. ~~Zdalny pulpit~~ **zrobione (podstawowo)**: `VisionPro/pc_stream/stream_server.py` na komputerze +
   `RemoteDesktopReceiver` (klatki JPEG po HTTP w sieci lokalnej, 10–20 kl/s; klik, prawy klik, pisanie
   i dyktowanie z `--allow-input`). Adres w `Documents/pulpit.txt`. HTTP w iOS ograniczony do sieci
   lokalnej (ATS `NSAllowsLocalNetworking`). Zostało: Moonlight/WebRTC dla płynności. Pierwotnie: Moonlight/Sunshine (niskie opóźnienie) lub WebRTC (`com.unity.webrtc`);
   input z gładzika/klawiatury jest już kierowany do `RemoteDesktopReceiver`.
8. **Aplikacje jako osobne ekrany – zrobione**: dock „Aplikacje” → lista okien z komputera
   (`stream_server.py /windows`) → każda wybrana aplikacja to osobny ekran (`?app=`), zapamiętywany w
   `workspace.json`. Aplikacji z iPhone'a w goglach nie da się pokazać (ograniczenie iOS). Dalej:
   WebView (np. Vuplex 3D WebView) dla Messengera itp. – płatna wtyczka; uwaga na regulaminy serwisów.
9. Dźwięk przestrzenny okien: `AudioSource` są, ale nic nie gra – podpiąć wideo/pulpit.
10. Dyktowanie: ~~podgląd na żywo~~ **zrobione** (przycisk „Głos” pokazuje koniec usłyszanego tekstu);
    ~~wybór języka~~ i ~~komendy głosowe~~ **zrobione**: przycisk PL/EN w docku; samo zdanie-komenda
    (bez innych słów) wykonuje akcję zamiast się wpisać: „nowy ekran”, „wyczyść”, „kino”, „telefon”,
    „reset biurka”, „następny/poprzedni ekran” (i odpowiedniki EN). Tylko na iOS (nie w atrapie).

**Niski / eksperymenty:**
11. Gesty: ~~pięść = przenieś wszystkie okna~~ **zrobione** (trzymaj pięść 0,5 s, obracaj głową, otwórz
    dłoń, aby odłożyć; przypięte okna zostają). Menu na nadgarstku **odrzucone**: gest „dłoń do siebie + uszczypnięcie” otwiera już ustawienia bazy
    (`SettingsSummoner`), kolidowałoby.
12. `PassthroughLighting` dla oświetlenia okien jak w pokoju; `ScreenFade` przy otwieraniu/zamykaniu.
13. Zmierzony profil optyczny Shinecon G10 (patrz pkt 9 w sekcji 2).
