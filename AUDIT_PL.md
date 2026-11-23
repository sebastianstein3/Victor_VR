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
