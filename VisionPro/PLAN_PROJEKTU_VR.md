# Plan Projektu: DIY Headset VR/MR (CardboardXR) na iPhone 16 Pro i VR Shinecon G10

Kompleksowa dokumentacja techniczna, architektoniczna oraz sprzętowa dla mobilnego zestawu Mixed Reality / Spatial Computing opartego o iPhone 16 Pro, gogle **VR Shinecon G10 (SC-G10)** oraz silnik **CardboardXR (Unity 6)**.

---

## 1. Architektura i Koncepcja Systemu

Projekt realizuje pełnowymiarowy system przestrzenny typu *Spatial Workspace* (wzorowany na Apple Vision Pro) bez konieczności druku 3D:

* **Platforma bazowa:** [CardboardXR](../README.md) – otwartoźródłowy projekt łączący Google Cardboard SDK z Apple ARKit (AR Foundation) w Unity.
* **Gogle:** VR Shinecon G10 (SC-G10) z soczewkami asferycznymi PMMA 40 mm i zdejmowaną magnetycznie przednią pokrywą (odsłaniającą potrójny aparat, LiDAR i mikrofony).
* **Śledzenie 6DoF:** ARKit Visual-Inertial Odometry z fuzją LiDAR meshing w przestrzeni rzeczywistej.
* **Śledzenie Dłoni:** MediaPipe 21-keypoint 3D Hand Tracking (`SpatialGestureDetector.cs`) z optymalizacją pod iPhone Neural Engine.
* **Przestrzeń robocza:** Pływające monitory wirtualne, wirtualny smartfon social media (TikTok / Messenger), niewidzialna klawiatura biurkowa, haptyczny trackpad oraz zdalny pulpit PC/Mac.

---

## 2. Kalibracja Optyczna (Profil QR Shinecon G10)

Aby zniwelować zniekształcenia beczkowe soczewek oraz uzyskać ostry, stereoskopowy obraz bez rozmycia krawędzi, wygenerowano dedykowany profil optyczny zgodny z Google Cardboard V2 / CardboardXR:

* Skrypt generujący: [generate_shinecon_qr.py](./generate_shinecon_qr.py)
* Wygenerowany kod QR: [Shinecon_G10_QR.png](./Shinecon_G10_QR.png)
* Wizualna karta kalibracyjna: [Shinecon_G10_Profile.html](./Shinecon_G10_Profile.html)
* **Kluczowe parametry profilu:**
  * Odległość od ekranu do soczewki: **42 mm**
  * Rozstaw źrenic (IPD): **62 mm** (zakres regulacji 58–70 mm)
  * Kąt widzenia (FOV): **95°**
  * Współczynniki dystorsji radialnej: $k_1 = 0.32$, $k_2 = 0.22$

---

## 3. Rozpoznawanie Gestów Przestrzennych (`SpatialGestureDetector.cs`)

Moduł [SpatialGestureDetector.cs](../Assets/_Project/Scripts/Hands/SpatialGestureDetector.cs) przetwarza pozycje 21 stawów dłoni w czasie rzeczywistym i udostępnia gesty statyczne i dynamiczne:

* **Pinch (Uszczypnięcie kciuk + wskazujący):** Kliknięcie, chwytanie okien, pisanie wzrokiem i selekcja.
* **OpenPalmForward (Otwarta dłoń przed siebie - STOP):** Otwiera / zamyka główny dock aplikacji lub natychmiast pauzuje odtwarzanie.
* **OpenPalmUser (Otwarta dłoń zwrócona do oczu):** Wywołuje menu podręczne bezpośrednio na nadgarstku/dłoni.
* **Pointing (Wskazywanie palcem):** Celownik wskaźnika i stukanie w klawiaturę biurkową.
* **ThumbsUp (Kciuk w górę):** Potwierdzenie akcji, polubienie postu / wideo.
* **Fist (Zaciśnięta pięść):** Przenoszenie ekranów w przestrzeni 3D.
* **Swipe Up / Swipe Down:** Przewijanie feedu wideo (TikTok, YouTube Shorts, Reels) lub przewijanie czatu Messenger.
* **Swipe Left / Swipe Right:** Przełączanie wirtualnych pulpitów lub zakładek.
* **Push Forward (Wciśnięcie w przód):** Fizyczne kliknięcie wirtualnego przycisku w przestrzeni AR.

---

## 4. Wirtualne Monitory i Zdalny Pulpit PC/Mac

Moduły [VirtualScreen.cs](../Assets/_Project/Demos/Workspace/Scripts/VirtualScreen.cs) oraz [RemoteDesktopReceiver.cs](../Assets/_Project/Demos/Workspace/Scripts/RemoteDesktopReceiver.cs):

* Pływające monitory 16:9 z paskiem manipulacji 3D (obrót, przesuwanie bliżej/dalej, zakotwiczenie w przestrzeni pokoju).
* **Streaming Zdalnego Pulpitu:** Odbiór klatek wideo niskiego opóźnienia z komputera PC lub Maca (przez sieć Wi-Fi 6E/USB-C) bezpośrednio na wielkim wirtualnym ekranie.
* **Wirtualny Kursor:** Wizualizacja pozycji wskaźnika kontrolowanego wzrokiem, gładzikiem biurkowym lub fizyczną myszą Bluetooth.

---

## 5. Dźwięk Przestrzenny 3D dla Apple AirPods Pro 2

* Każde okno (monitor, smartfon, odtwarzacz wideo) posiada dedykowany komponent `AudioSource` z parametrem `spatialBlend = 1.0f` i logarytmicznym spadkiem natężenia dźwięku w przestrzeni.
* W połączeniu z **AirPods Pro 2** i technologią **Dynamic Head Tracking** w iOS 18 dźwięk z filmu z lewego monitora dochodzi dokładnie z lewej strony; gdy obrócisz głowę w stronę tego monitora, dźwięk naturalnie centruje się w uszach.

---

## 6. Niewidzialna Klawiatura i Trackpad Biurkowy z Fizyczną Haptyką

Moduły [DeskKeyboard.cs](../Assets/_Project/Demos/Workspace/Scripts/DeskKeyboard.cs) oraz [DeskTrackpad.cs](../Assets/_Project/Demos/Workspace/Scripts/DeskTrackpad.cs):

* **Detekcja płaszczyzny biurka:** LiDAR z AR Foundation lokalizuje fizyczny blat stołu.
* **Klawiatura QWERTY:** Renderowana na blacie biurka. Stukanie opuszkami palców w fizyczny stół daje naturalną mechaniczną odpowiedź dotykową (haptykę) bez konieczności noszenia rękawic haptycznych.
* **Niewidzialny Trackpad:** Szklana strefa gładzika na prawo od klawiatury.
  * 1 palec na blacie = lewy klik i precyzyjne przesuwanie kursora myszy.
  * 2 palce na blacie = prawy klik (menu kontekstowe) i przewijanie dwoma palcami.
* **Fizyczna Klawiatura & Mysz Bluetooth:** Natywna obsługa przez `UnityEngine.InputSystem` – bezprzewodowa klawiatura (Magic Keyboard, MX Keys) i mysz sparowane z iPhone'em sterują bezpośrednio oknami w AR.

---

## 7. Wirtualny Smartfon Social Media (`SpatialPhoneScreen.cs`)

Pionowy ekran w proporcjach 9:18 z launcherem aplikacji społecznościowych:

* **Kafelki aplikacji (Home Screen):** Facebook Messenger, TikTok, YouTube Shorts, Instagram Reels, Twitch, Notatki. Jedno kliknięcie otwiera daną usługę bez żmudnego wpisywania adresów URL!
* **Messenger & Czat:** Stylizowany widok rozmowy z bezpośrednią integracją z klawiaturą biurkową oraz dyktowaniem mowy.
* **Sterowanie gestami:** Gest *Swipe Up / Down* w powietrzu zmienia oglądane filmiki w pionowym feedzie.

---

## 8. Dyktowanie Głosowe Speech-to-Text (`VoiceDictation.cs`)

* Przechwytuje mikrofon iPhone'a 16 Pro ze słuchawek AirPods Pro 2.
* Automatyczna detekcja mowy i ciszy (*Voice Activity Detection*).
* Wprowadza tekst bezpośrednio do aktywnego dokumentu na wirtualnym monitorze lub do wiadomości na Messengerze.

---

## 9. Tryb Kinowy z Wygaszaniem Otoczenia (`CinemaDimmer.cs`)

* Płynne ściemnianie tła passthrough (kamera AR zostaje zaciemniona w 90%), odcinając rozpraszający pokój i pozostawiając tylko lewitujący monitor o kinowym kontraście.
* Możliwość natychmiastowego powrotu do pełnego podglądu rzeczywistości gestem *OpenPalmForward* (STOP) lub kliknięciem w docku.

---

## 10. Zestawienie Rekomendowanego Sprzętu i Linki Zakupowe

Dla zapewnienia stabilnej, wielogodzinnej pracy zestawu VR Shinecon G10 + iPhone 16 Pro:

| Element | Rekomendowany model | Kluczowa funkcja | Gdzie kupić (Linki) |
|---|---|---|---|
| **Powerbank** | **Baseus Magnetic Mini 10000mAh 20W/30W PD** lub **Anker MagGo 10000mAh** | Lekki (~200g), port USB-C PD, montowany z tyłu paska jako przeciwwaga dla telefonu | [Allegro: Baseus Magnetic Mini 10000mAh](https://allegro.pl/listing?string=baseus%20magnetic%20mini%2010000mah%20pd) <br> [Amazon.pl: Anker 10000mAh PD](https://www.amazon.pl/s?k=anker+10000mah+usb-c+pd) |
| **Adapter Kątowy 90° / Rozdzielacz** | **Adapter kątowy USB-C 90° 100W PD** / **Rozdzielacz 2x USB-C PD (Dual PD Splitter)** | Chroni gniazdo USB-C w goglach Shinecon G10, pozwala równolegle ładować telefon i zasilać cooler | [Allegro: Adapter kątowy USB-C 90 stopni PD](https://allegro.pl/listing?string=adapter%20k%C4%85towy%20usb-c%2090%20stopni%20pd) <br> [Allegro: Rozdzielacz USB-C 2w1 PD](https://allegro.pl/listing?string=rozdzielacz%20usb-c%202w1%20power%20delivery) <br> [Amazon.pl: USB-C 90 Degree Adapter 100W](https://www.amazon.pl/s?k=adapter+usb-c+90+stopni+pd) |
| **Aktywne Chłodzenie MagSafe** | **Black Shark MagCooler 3 / 4 Pro** lub **Memo CX07 / CX15 MagSafe** | Ogniwo Peltiera schładza plecy iPhone'a 16 Pro po zdjęciu magnetycznej klapki Shinecon G10 | [Allegro: Chłodzenie MagSafe Peltier](https://allegro.pl/listing?string=ch%C5%82odzenie%20magsafe%20telefon%20peltier) <br> [Amazon.pl: Black Shark Magnetic Cooler](https://www.amazon.pl/s?k=black+shark+magnetic+cooler) |

---

## 11. Instrukcja Uruchomienia i Kompilacji (Unity 6 + Xcode)

1. Otwórz projekt w Unity 6: `d:\Narzędzia PROMETEUSZ\Vision Pro\CardboardXR`.
2. W menu górnym Unity wybierz: `CardboardXR > Make Missing Demo Scenes`.
3. Zbuduj i uruchom na iPhone 16 Pro przez skrypt:
   ```bash
   CARDBOARDXR_BUNDLE_ID=com.twojanazwa.cardboardxr zsh Tools/ios_build_and_run.sh
   ```
4. Zeskanuj kod QR kalibracji [Shinecon_G10_QR.png](./Shinecon_G10_QR.png) przy pierwszym uruchomieniu.
5. Włóż telefon do gogli Shinecon G10, zdejmij przednią klapkę, załóż słuchawki AirPods Pro 2 i usiądź przy biurku!
