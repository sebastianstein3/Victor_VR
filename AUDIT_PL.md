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

## 5. Bezpieczeństwo (przegląd 2026-11-23)

**Sprawdzone zewnętrzne repozytoria (tylko czytane, nic nie uruchomiono):**
- `justinfincher/webviewtextureios` – **brak licencji** (wszelkie prawa zastrzeżone): kod NIE został
  użyty; przeglądarka (`CardboardXRBrowser.mm`) to własna implementacja.
- `google/window-mirror` – Apache-2.0, tylko Windows, ramki JPEG po TCP **bez uwierzytelnienia i
  szyfrowania**; nic nie przeniesiono (nasz serwer ma token i opcjonalne szyfrowane WebRTC).
- Moonlight (`moonlight-common-c`) – GPL-3.0, objęłoby licencją całą aplikację: pominięty.

**Serwer ekranu (`VisionPro/pc_stream/stream_server.py`):**
- Każde żądanie wymaga tokenu (`X-CXR-Token`, ~96 bitów, porównanie w stałym czasie, plik tokenu
  z uprawnieniami 0600). Przetestowane: bez tokenu 401.
- Sterowanie (`--allow-input`) domyślnie wyłączone; kliknięcia ograniczone do udostępnianego obszaru,
  klawisze do krótkiej listy, tekst do 2000 znaków, ciało żądania do 64 KB; aplikacja przywoływana
  tylko, jeśli ma widoczne okno, a jej nazwa idzie do `osascript` jako argument (brak wstrzyknięcia
  AppleScript); `pyautogui.FAILSAFE` (mysz w róg = stop).
- WebRTC: media szyfrowane DTLS-SRTP, `/offer` za tokenem, maks. 4 połączenia, tylko sieć lokalna.
- **Ryzyko pozostałe:** tryb JPEG i sam token idą po HTTP bez szyfrowania – w obcej/publicznej sieci
  Wi-Fi ktoś podsłuchujący może przechwycić obraz i token. Używać tylko w sieci domowej.

**Aplikacja na iPhonie:**
- HTTP dozwolone tylko w sieci lokalnej (ATS `NSAllowsLocalNetworking`, `NSAllowsArbitraryLoads` usuwane
  przy budowaniu); opis dostępu do sieci lokalnej, mikrofonu i rozpoznawania mowy w Info.plist.
- Przeglądarka: tylko http(s) (http i tak blokowane przez ATS poza siecią lokalną), blokada `file:`,
  `javascript:`, `data:`/`blob:` w głównej ramce, `tel:`/`sms:`/linków do aplikacji; brak wyskakujących
  okien, pobierania plików i mostu JavaScript→aplikacja; tekst do stron kodowany przez JSON; okienka
  `alert/confirm/prompt` odrzucane. Ciasteczka/logowania trwałe w osobnym magazynie aplikacji.
- Teksty wpisywane przez użytkownika wyświetlane z `<noparse>` (brak wstrzyknięcia znaczników TMP);
  JSON do serwera budowany z escapowaniem.
- **Do świadomości:** `Documents` aplikacji jest udostępniony w Finderze/Plikach (tak skonfigurował
  autor bazy) – leżą tam `pulpit.txt` (z tokenem), `workspace.json` (treść notatek), klipy. Ktoś z
  dostępem do odblokowanego iPhone'a lub Maca, z którym jest sparowany, może je odczytać.
- **Prywatność dyktowania:** domyślnie Apple może przetwarzać mowę na serwerach (lepsza jakość);
  `VoiceDictation.OnDeviceOnly = true` wymusza rozpoznawanie na telefonie (wymaga pobranego języka).
- Pobieranie z Google Drive po HTTPS, nazwy plików oczyszczane przez kod bazy (`DriveDownloader`).

### Przeglądarka – druga wersja (wzorem obecnych przeglądarek)
- Klatki zapisywane wprost do tekstury Metal (bez kopiowania przez C#), przechwytywanie synchroniczne
  z zapasowym `takeSnapshot`; ~20 kl/s, gdy patrzysz na stronę, 2 kl/s, gdy nie (jak karty w tle).
  Z `webviewtextureios` wzięta tylko technika; jego błędy (przejęcie i zwalnianie tekstury Unity,
  wycieki kontekstu, zapis pikseli w adres zmiennej w OpenGL) – unikane.
- Pasek postępu, prawdziwa domena + „Szyfrowane/NIESZYFROWANE” pod stroną (ochrona przed podróbkami),
  blokowanie trackerów i reklam (reguły treści WebKit, lista ~24 domen), tryb ciemny stron,
  powiększenie tekstu (- / + w oknie przeglądarki), okno prywatne (nic nie zostaje po zamknięciu,
  nie trafia do `workspace.json`), odmowa dostępu stron do kamery/mikrofonu.
- Pozostałe ograniczenie: pola wyboru pliku na stronach otwierają natywny wybór pliku, niewidoczny
  w goglach (iOS nie daje tego zablokować w WKWebView); `drawViewHierarchy` dla widoku schowanego za
  Unity trzeba sprawdzić na urządzeniu (jest zapas `takeSnapshot`).

### Poprawki po drugim przeglądzie kodu
- Obraz strony był odwrócony do góry nogami (kolejność wierszy) – naprawione.
- Serwer: nieudane oferty WebRTC nie blokują już miejsc (test: 5 błędnych → 0 zajętych), `mss` tworzony
  na każde przechwycenie / jeden wątek na ścieżkę WebRTC (bez wycieków, poprawne na Windows/X11),
  odporność na NaN, ujemny `Content-Length`, nazwy aplikacji z polskimi znakami (czyste 404),
  plik tokenu zawsze 0600. Samouczek w aplikacji opisuje token.

### Rozciąganie okien (szerokość i wysokość osobno)
- Elementy okna są zakotwiczone do krawędzi (nagłówek – góra, stopka i uchwyty – dół, treść – środek),
  więc zmiana rozmiaru w dp układa okno od nowa. Uchwyt w prawym dolnym rogu: róg podąża za wzrokiem
  (symetrycznie, okno zostaje na miejscu), koniec uszczypnięciem lub ponownym stuknięciem; głosem „rozciągnij”.
- Zakres: od domyślnych 520 × 440 dp do 1200 × 900 dp (~95° × 72°); do tego skala -/+ 0,6–2×.
- Przeglądarka po zakończeniu zmiany rozmiaru przebudowuje stronę (nowy rozmiar WKWebView i tekstury),
  pulpit PC dopasowuje proporcje obrazu. Rozmiar zapisywany w `workspace.json`.
- Do sprawdzenia na urządzeniu: płynność zmiany rozmiaru i przebudowa tekstury przeglądarki.

## 6. Narzędzia z menu „Więcej” (napisy, widgety, PDF, środowiska, modele 3D, zdjęcia 3D)
Dodane: napisy na żywo z tłumaczeniem (`LiveCaptions.cs`, `CardboardXRTranslate.swift`, framework Translation,
pl↔en wg języka przycisku „PL”), widgety Zegar / Minutnik / Notatka / Pogoda (Open-Meteo, miasto w
`Documents/pogoda.txt`) / Kalendarz (EventKit), czytnik PDF (`PdfView.cs`, `CardboardXRPdf.mm`; strony
przyciskami, gestem góra/dół, głosem), środowiska 360 (`EnvironmentSphere.cs`: Zmierzch, Noc, Ocean, Kosmos
i własne pliki z `Documents/Srodowiska`; passthrough wyłączony na czas pokazu), modele 3D (`ModelViewer.cs`,
glTFast, `Documents/Modele`, ustawianie na powierzchni, panel -30°/+30°/-/+), zdjęcia i filmy przestrzenne
(`SpatialMediaView.cs`, `CardboardXRSpatial.swift`: HEIC, MV-HEVC, `_sbs`; każde oko własny obraz). Wszystko z
docka przez „Więcej”; komendy głosowe w `WorkspaceDemo.TryVoiceCommand`. Pakiet `com.unity.cloud.gltfast`
jest opcjonalny (`CXR_GLTF`).

**Nic z tego nie było kompilowane ani uruchamiane na urządzeniu.** Do sprawdzenia na Macu i iPhonie:
- **Translation:** dostępność API (iOS 26.4+), pobieranie języków w Ustawieniach iOS, zachowanie bez
  pobranych modeli; mostek Swift w Unity (kolejność kompilacji, nagłówek `UnityFramework-Swift.h`).
- **MV-HEVC:** wydajność dekodowania na głównym wątku (możliwe przycinanie), pamięć przy długich filmach,
  poprawny podział na lewe/prawe oko.
- **glTFast:** brakujące shadery w buildzie (`GltfShaders.cs`), rozmiar builda, modele z teksturami
  rozszerzonymi; bez pakietu funkcja jest wyłączona.
- **EventKit:** uprawnienie do kalendarza (`NSCalendarsFullAccessUsageDescription`), zachowanie po odmowie.
- Ogólnie: pogoda wymaga internetu, PDF i środowiska wideo – pamięć i płynność, passthrough po wyjściu ze
  środowiska, zapis widgetów w `workspace.json`.
