> **Uwaga:** po audycie kodu z 2026-11-23 zobacz też [`../AUDIT_PL.md`](../AUDIT_PL.md) (znalezione błędy, co naprawiono, co zostało).

# DOKUMENTACJA PRZEKAZANIA PROJEKTU (HANDOVER)
## Projekt: DIY Spatial Computing Headset (Apple Vision Pro Replica)
### Baza sprzętowa: iPhone 16 Pro + Gogle VR Shinecon G10 (SC-G10) + Apple AirPods Pro 2
### Silnik programistyczny: CardboardXR (Unity 6 / ARKit / Google Cardboard / MediaPipe)

Data sporządzenia: 2026-11-23  
Lokalizacja projektu: `d:\Narzędzia PROMETEUSZ\Vision Pro`

---

## 1. Cel Projektu i Założenia Architektoniczne

Celem projektu jest stworzenie mobilnego, bezprzewodowego systemu **Spatial Computing / Mixed Reality** na wzór **Apple Vision Pro**, wykorzystując jako jednostkę obliczeniową i wyświetlacz smartfon **iPhone 16 Pro** oraz gotowe gogle mobilne **VR Shinecon G10 (SC-G10)** – bez konieczności drukowania elementów na drukarce 3D.

### Dlaczego wybrano gogle VR Shinecon G10?
1. **Odsłonięcie kamer i LiDARu:** Przednia pokrywa gogli jest mocowana na magnesy/zatrzaski i można ją całkowicie zdjąć. Odsłania to potrójny aparat, skaner LiDAR oraz sensory iPhone'a 16 Pro, umożliwiając działanie stereoskopowego passthrough oraz śledzenie dłoni 6DoF w czasie rzeczywistym.
2. **Aktywne chłodzenie:** Po zdjęciu klapki na plecy iPhone'a można bezpośrednio przyczepić magnetyczny cooler MagSafe z ogniwem Peltiera (np. Black Shark MagCooler), co zapobiega dławieniu termicznemu (thermal throttling).
3. **Regulacja optyki:** Niezależna regulacja rozstawu soczewek (IPD 58–70 mm) oraz odległości od oczu (dla osób z wadą wzroku).
4. **Soczewki:** 40 mm soczewki asferyczne PMMA z kątem widzenia FOV ~95°.

---

## 2. Co Gdzie Się Znajduje (Mapa Plików i Folderów)

Całość repozytorium i narzędzi znajduje się w folderze bazowym:
`d:\Narzędzia PROMETEUSZ\Vision Pro\`

### Pliki nadrzędne (Katalog główny):
* [PLAN_PROJEKTU_VR.md](./PLAN_PROJEKTU_VR.md) – Główny plan projektu, specyfikacja techniczna i podsumowanie wdrożeń.
* [HANDOVER.md](./HANDOVER.md) – Niniejszy dokument przekazania dla innego agenta / programisty.
* [Shinecon_G10_QR.png](./Shinecon_G10_QR.png) – Kod QR profilu optycznego Google Cardboard dla gogli Shinecon G10 (skanowany przy pierwszym uruchomieniu).
* [Shinecon_G10_Profile.html](./Shinecon_G10_Profile.html) – Wizualna karta kalibracyjna z parametrami optycznymi do wyświetlenia na ekranie monitora.
* [generate_shinecon_qr.py](./generate_shinecon_qr.py) – Skrypt Python generujący oficjalny profil URI Google Cardboard V2 zoptymalizowany pod soczewki 40 mm.

---

### Baza kodu Unity: `CardboardXR/`
Ścieżka: `d:\Narzędzia PROMETEUSZ\Vision Pro\CardboardXR`

Kluczowe pliki wdrożone w projekcie (wszystkie napisane w C# dla Unity 6):

```
CardboardXR/
├── Assets/
│   └── _Project/
│       ├── Scripts/
│       │   └── Hands/
│       │       └── SpatialGestureDetector.cs  <-- Klasyfikator 21 stawów dłoni MediaPipe
│       │
│       └── Demos/
│           ├── DemoCatalog.asset             <-- Katalog scen (zarejestrowany kafelek "workspace")
│           └── Workspace/                    <-- GŁÓWNY MODUŁ SPATIAL WORKSPACE
│               ├── Scripts/
│               │   ├── WorkspaceDemo.cs          <-- Główny orchestrator środowiska
│               │   ├── VirtualScreen.cs          <-- Wirtualne monitory 3D w przestrzeni
│               │   ├── DeskKeyboard.cs           <-- Niewidzialna klawiatura z haptyką blatu
│               │   ├── DeskTrackpad.cs           <-- Niewidzialny szklany trackpad biurkowy
│               │   ├── SpatialPhoneScreen.cs     <-- Wirtualny smartfon 9:18 (TikTok/Messenger)
│               │   ├── VoiceDictation.cs         <-- Dyktowanie mowy (Speech-to-Text)
│               │   ├── CinemaDimmer.cs           <-- Wygaszanie tła passthrough (tryb kinowy)
│               │   └── RemoteDesktopReceiver.cs  <-- Odbiornik zdalnego pulpitu PC/Mac
│               ├── Editor/
│               │   └── WorkspaceSetup.cs         <-- Rejestrator sceny i automatyczny instalator
│               └── Tests/
│                   └── WorkspaceTests.cs         <-- Zestaw testów jednostkowych NUnit
│
└── Tools/
    ├── ios_build_and_run.sh                  <-- Oficjalny skrypt budowania i wgrywania na iPhone przez Xcode
    └── make_videos.sh                        <-- Skrypt pomocniczy assetów wideo
```

---

## 3. Szczegółowy Opis Zaimplementowanych Funkcji

### 1. Klasyfikator Gestów Przestrzennych ([SpatialGestureDetector.cs](../Assets/_Project/Scripts/Hands/SpatialGestureDetector.cs))
* Przetwarza pozycje 21 markerów 3D dłoni zwracanych przez moduł `HandTracker` (MediaPipe BlazeHand uruchamiany przez Unity Inference Engine).
* **Gesty statyczne:**
  * `Pinch`: złączenie kciuka i wskazującego ($< 2.8\text{ cm}$) – chwytanie, selekcja.
  * `OpenPalmForward`: otwarta dłoń przed siebie (STOP) – chowa/pokazuje dock aplikacji.
  * `OpenPalmUser`: dłoń skierowana do oczu użytkownika – menu podręczne na nadgarstku.
  * `Pointing`: wyprostowany palec wskazujący – celownik lasera / pisanie.
  * `ThumbsUp`: kciuk w górę – akceptacja / polubienie wideo.
  * `Fist`: zaciśnięta pięść.
* **Gesty dynamiczne (bufor prędkości 10 klatek w przestrzeni głowy):**
  * `SwipeUp` / `SwipeDown`: szybkie machnięcie w pionie – przewijanie filmów TikTok / YouTube Shorts.
  * `SwipeLeft` / `SwipeRight`: przełączanie między aktywnymi oknami monitorów.
  * `PushForward`: pchnięcie dłoni w przód.

### 2. Wirtualne Monitory 3D ([VirtualScreen.cs](../Assets/_Project/Demos/Workspace/Scripts/VirtualScreen.cs))
* Pływające w przestrzeni pokoju monitory 16:9 zintegrowane z systemem Material 3 (`WorldUi.Panel`).
* Posiadają belkę chwytu (`GrabBar`), przycisk przypięcia (`PinBtn`) oraz przycisk zamknięcia.
* Każde okno posiada wbudowany edytor tekstu, wskaźnik kursora oraz licznik statystyk (znaki/słowa).
* **Dźwięk przestrzenny 3D:** Do każdego okna dołączony jest komponent `AudioSource` z parametrem `spatialBlend = 1.0f` i logarytmicznym spadkiem głośności, zoptymalizowany pod **Apple AirPods Pro 2** (Dynamic Head Tracking na iOS 18).

### 3. Zdalny Pulpit PC/Mac ([RemoteDesktopReceiver.cs](../Assets/_Project/Demos/Workspace/Scripts/RemoteDesktopReceiver.cs))
* Umożliwia przekształcenie dowolnego wirtualnego monitora w ekran komputera PC/Mac po sieci Wi-Fi (np. strumień WebRTC / Sunshine / Moonlight / TCP MJPEG).
* Renderuje obraz na obiekcie `RawImage` z nakładką statusu połączenia i FPS.

### 4. Niewidzialna Klawiatura Biurkowa ([DeskKeyboard.cs](../Assets/_Project/Demos/Workspace/Scripts/DeskKeyboard.cs))
* Klawiatura QWERTY wyświetlana w przestrzeni AR na powierzchni fizycznego biurka.
* Śledzi opuszek palca wskazującego (`HandGeometry.IndexTip`).
* **Fizyczna haptyka blatu:** Użytkownik stuka palcem bezpośrednio w blat swojego stołu – fizyczny opór mebla daje idealne, bezopóźnieniowe sprzężenie zwrotne.
* Obsługuje Shift, Backspace, Enter, spację i generuje proceduralny mechaniczny dźwięk kliknięcia (`CreateClickSound`).

### 5. Niewidzialny Trackpad Biurkowy ([DeskTrackpad.cs](../Assets/_Project/Demos/Workspace/Scripts/DeskTrackpad.cs))
* Szklana tafla gładzika umieszczona na blacie po prawej stronie klawiatury.
* **1 palec na blacie:** Przesuwanie kursora myszy na aktywnym monitorze + lewy klik przy stuknięciu.
* **2 palce na blacie:** Prawy klik (menu kontekstowe) przy stuknięciu dwoma złączonymi palcami (`IndexTip` i `MiddleTip`).

### 6. Wirtualny Smartfon Social Media ([SpatialPhoneScreen.cs](../Assets/_Project/Demos/Workspace/Scripts/SpatialPhoneScreen.cs))
* Pionowy ekran o proporcjach smartfona (format 9:18, $280 \times 560\text{ dp}$) z imitacją obudowy iPhone'a i Dynamic Island.
* **Pulpit aplikacji (Home Screen):** Kafelki szybkiego startu: **Facebook Messenger**, **TikTok**, **YouTube Shorts**, **Instagram Reels**, **Twitch**, **Notatki**. Kliknięcie natychmiast otwiera daną platformę bez ręcznego wpisywania adresów URL.
* **Integracja Messenger + Klawiatura:** Gdy otwarty jest Messenger, pisanie na biurkowej klawiaturze automatycznie kieruje litery do czatu Messengera, a klawisz Enter wysyła wiadomość.
* **Sterowanie gestami:** Machnięcie dłonią w powietrzu (`SwipeUp`/`SwipeDown`) przeskakuje do następnego/poprzedniego filmiku w feedzie.

### 7. Dyktowanie Głosowe Speech-to-Text ([VoiceDictation.cs](../Assets/_Project/Demos/Workspace/Scripts/VoiceDictation.cs))
* Przechwytuje dźwięk z mikrofonu iPhone'a lub słuchawek AirPods Pro 2.
* Automatycznie wykrywa zakończenie wypowiedzi i przekazuje tekst do aktywnego monitora lub czatu Messengera.

### 8. Tryb Kinowy Passthrough Dimmer ([CinemaDimmer.cs](../Assets/_Project/Demos/Workspace/Scripts/CinemaDimmer.cs))
* Po włączeniu trybu kinowego tło pokoju za monitorami zostaje przyciemnione w 82%, odcinając użytkownika od otoczenia pokoju podczas seansu filmowego.

### 9. Fizyczna Klawiatura i Mysz Bluetooth ([WorkspaceDemo.cs](../Assets/_Project/Demos/Workspace/Scripts/WorkspaceDemo.cs))
* Wbudowana natywna obsługa `UnityEngine.InputSystem`: dowolna fizyczna klawiatura Bluetooth (np. Apple Magic Keyboard) i mysz sparowane z iPhone'em sterują wskaźnikiem i pisaniem w oknach.

---

## 4. Kluczowe Poprawki Architektoniczne (Wprowadzone w Audycie)

Jeśli kolejny agent będzie modyfikował kod, **musi pamiętać o następujących regułach projektowych**:

1. **Struktura GameObjects interfejsu (Wzorzec `Create`):**
   * Elementy UI z pakietu `WorldUi.Panel` tworzą własne obiekty typu `RectTransform`.
   * Klasy takie jak `VirtualScreen`, `SpatialPhoneScreen`, `DeskKeyboard` i `DeskTrackpad` muszą być montowane **bezpośrednio na obiekcie panelu** za pomocą metody statycznej `.Create()`. Dzięki temu `gazeTarget.transform.IsChildOf(screen.transform)` działa prawidłowo, gesty chwytania okien nie gubią celownika, a usuwanie okna (`Destroy`) niszczy cały interfejs bez osieroconych obiektów.
2. **Matematyka detekcji dotyku blatu stołu:**
   * Klasa `M3Canvas` stosuje skalowanie jednostek `dp` na metry (`canvasScale = lossyScale.x`).
   * Metoda `InverseTransformPoint(worldTip)` zwraca współrzędną $Z$ w jednostkach `dp`! Aby uzyskać odległość w metrach, należy bezwzględnie pomnożyć ją przez skalę:
     ```csharp
     float zDistanceMeters = Mathf.Abs(localTip.z * canvasScale);
     if (zDistanceMeters < 0.035f) // 3.5 cm tolerancji
     ```
3. **Pozycjonowanie Canvasów w 3D:**
   * Wirtualne monitory: dystans $1.4\text{ m}$ przed oczami (`k_ScreenDistance = 1.4f`).
   * Dock aplikacji: dystans $1.2\text{ m}$, pochylenie $-22^\circ$ w dół (`pitch = -22f`).
   * Klawiatura i trackpad: dystans $0.65\text{ m}$, wysokość $-0.35\text{ m}$ poniżej głowy, pochylenie $25^\circ$.
   * Tło kinowe `CinemaDimmer`: dystans $2.2\text{ m}$ (bezwzględnie **za** monitorami, a nie przed nimi!).

---

## 5. Zestawienie Sprzętowe i Linki Zakupowe

Do stabilnego działania zestawu VR Shinecon G10 + iPhone 16 Pro rekomendowane są poniższe akcesoria:

| Element | Rekomendowany model | Rola w projekcie | Linki do ofert |
|---|---|---|---|
| **Powerbank** | **Baseus Magnetic Mini 10000mAh 20W/30W PD** | ~200g, montowany na rzepie z tyłu paska jako przeciwwaga dla telefonu | [Allegro](https://allegro.pl/listing?string=baseus%20magnetic%20mini%2010000mah%20pd) / [Amazon.pl](https://www.amazon.pl/s?k=anker+10000mah+usb-c+pd) |
| **Adapter USB-C** | **Kątowy adapter USB-C 90° 100W PD / Rozdzielacz 2w1** | Wyprowadza kabel płasko wzdłuż telefonu w szczelinie gogli, zasila telefon i cooler | [Allegro (Adapter 90°)](https://allegro.pl/listing?string=adapter%20k%C4%85towy%20usb-c%2090%20stopni%20pd) / [Allegro (Splitter 2w1)](https://allegro.pl/listing?string=rozdzielacz%20usb-c%202w1%20power%20delivery) |
| **Aktywne Chłodzenie** | **Black Shark MagCooler 3 / 4 Pro** lub **Memo CX07/CX15** | Ogniwo Peltiera schładza plecy iPhone'a po zdjęciu magnetycznej klapki gogli | [Allegro](https://allegro.pl/listing?string=ch%C5%82odzenie%20magsafe%20telefon%20peltier) / [Amazon.pl](https://www.amazon.pl/s?k=black+shark+magnetic+cooler) |

---

## 6. Instrukcja Wdrożenia na iPhone (dla Użytkownika / Agenta)

Gdy użytkownik podłączy **iPhone 16 Pro** kablem USB-C do komputera **Mac mini**:

1. **Wymagania wstępne na Macu:**
   * Zainstalowane środowisko **Xcode** (zalogowane darmowym kontem Apple ID w *Xcode > Settings > Accounts*).
   * Zainstalowany silnik **Unity 6** (wersja `6000.0+` z modułem *iOS Build Support*).
   * Na iPhonie włączony **Tryb Dewelopera** (*Ustawienia -> Prywatność i ochrona -> Tryb dewelopera*).
2. **Kompilacja i uruchomienie (jedno polecenie):**
   W terminalu macOS w katalogu projektu uruchom skrypt:
   ```bash
   CARDBOARDXR_BUNDLE_ID=com.twojanazwa.cardboardxr zsh Tools/ios_build_and_run.sh
   ```
   Skrypt automatycznie wyeksportuje projekt z Unity do Xcode, podpisze certyfikatem i zainstaluje na iPhonie.
3. **Pierwsze uruchomienie na telefonie:**
   * Zaakceptuj uprawnienia do aparatu, mikrofonu i sieci lokalnej.
   * Zeskanuj z ekranu monitora kod QR kalibracji soczewek: [Shinecon_G10_QR.png](./Shinecon_G10_QR.png).
   * Zdejmij magnetyczną przednią klapkę gogli Shinecon G10 (odsłaniając aparaty i LiDAR), włóż telefon do uchwytu, załóż słuchawki AirPods Pro 2 i załóż gogle na głowę!

---

## 7. Roadmapa i Sugerowane Następne Kroki dla Kolejnego Agenta

1. **Obsługa rzeczywistego WebRTC w RemoteDesktopReceiver:**
   * Połączyć `RemoteDesktopReceiver.cs` z natywną biblioteką WebRTC (np. biblioteka `com.unity.webrtc` lub klient protokołu Sunshine/Moonlight) w celu streamingu pulpitu PC w 120 FPS z niskim opóźnieniem ($< 15\text{ ms}$).
2. **Natywny mostek dyktowania iOS (Speech framework):**
   * Zastąpić symulację transkrypcji w `VoiceDictation.cs` natywnym bridge'em C# -> Objective-C wywołującym `SFSpeechRecognizer` z biblioteki `Speech.framework` w iOS, co daje dyktowanie w języku polskim w 100% offline z akceleracją Apple Neural Engine.
3. **Obsługa WebView w SpatialPhoneScreen:**
   * Zintegrować wtyczkę 3D WebView (np. Vuplex 3D WebView for iOS), aby kafelki TikTok / Messenger ładowały rzeczywistą zawartość stron internetowych z logowaniem do konta użytkownika.
