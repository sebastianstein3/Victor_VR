# Samouczek: ekrany z Maca i iPada w goglach

Ten samouczek pokazuje, jak zobaczyć w goglach (iPhone 16 Pro + Shinecon G10):

- **cały ekran Maca** jako duży monitor,
- **pojedyncze aplikacje z Maca** (np. Messenger, przeglądarka) jako osobne ekrany,
- **ekran iPada** jako duży ekran w VR.

> **Ważne:** aplikacji z iPhone'a, który jest w goglach, nie da się tak pokazać. iOS wyświetla na
> iPhonie tylko jedną aplikację naraz, a jego ekran jest zajęty przez gogle. Dlatego aplikacje
> uruchamia Mac albo iPad, a gogle je pokazują.

**W goglach:** ten samouczek jest też w aplikacji: w Spatial Workspace stuknij w docku **?** albo
powiedz „pomoc”. Przy pierwszym uruchomieniu Workspace otwiera się sam.

Potrzebujesz: Maca (np. Mac mini), iPhone'a z aplikacją CardboardXR (Spatial Workspace) i jednej sieci
Wi-Fi dla wszystkich urządzeń. Do ekranu iPada: iPad z tym samym Apple ID co Mac.

---

## Krok 1. Przygotuj Maca (raz)

1. Otwórz **Terminal** (Spotlight: `Cmd + Spacja`, wpisz „Terminal”).
2. Zainstaluj potrzebne dodatki. Wklej i zatwierdź Enterem:
   ```
   pip3 install mss pillow pyobjc-framework-Quartz pyautogui pyperclip
   ```
3. Daj Terminalowi uprawnienia: **Ustawienia systemowe → Prywatność i ochrona**
   - **Nagrywanie ekranu** → włącz **Terminal** (bez tego gogle zobaczą czarny ekran),
   - **Dostępność** → włącz **Terminal** (tylko jeśli chcesz klikać i pisać z gogli).

   Po zmianie zamknij i otwórz Terminal ponownie.

## Krok 2. Uruchom serwer ekranu na Macu

1. W Terminalu przejdź do folderu projektu (przeciągnij folder `VisionPro/pc_stream` do okna Terminala
   po wpisaniu `cd `) i uruchom:
   ```
   python3 stream_server.py --allow-input
   ```
   Bez `--allow-input` gogle tylko oglądają, nie mogą klikać ani pisać.
2. Terminal wypisze adres, np.:
   ```
   Wpisz do iPhone'a, plik Documents/pulpit.txt:  192.168.1.20:8765
   ```
   Zapisz ten adres. Terminal zostaw otwarty: dopóki działa, gogle widzą ekran.

## Krok 3. Wpisz adres Maca do iPhone'a (raz)

1. Na iPhonie otwórz CardboardXR, wejdź do **Spatial Workspace** i stuknij w docku **PC**. Aplikacja
   utworzy wtedy plik `pulpit.txt`. Za pierwszym razem pokaże komunikat, że brakuje adresu.
2. Otwórz aplikację **Pliki** → **Na moim iPhonie** → **CardboardXR** → **pulpit.txt**.
3. W pierwszej pustej linii wpisz adres z kroku 2, np. `192.168.1.20:8765`, i zapisz.
   Możesz to też zrobić na Macu: Finder → iPhone → karta **Pliki** → CardboardXR.

## Krok 4. Cały ekran Maca w goglach

1. Włóż iPhone'a do gogli i otwórz **Spatial Workspace**.
2. W docku (pasek przycisków pod oknami) spójrz na **PC** i zrób uszczypnięcie.
3. Przy pierwszym połączeniu iOS zapyta o **dostęp do sieci lokalnej**. Wybierz **Zezwól**.
4. Na dole okna pojawi się nazwa Maca i liczba klatek na sekundę.

## Krok 5. Każda aplikacja Maca jako osobny ekran

1. Na Macu otwórz aplikacje, które chcesz widzieć, i **ułóż je obok siebie**. Okno nie może być
   zasłonięte ani zminimalizowane, bo gogle wycinają je z ekranu tam, gdzie leży.
   Mac mini bez monitora: zainstaluj darmowe **BetterDisplay** i utwórz duży wirtualny ekran.
2. W goglach stuknij w docku **Aplikacje** albo powiedz „aplikacje”.
3. Pojawi się lista aplikacji z Maca. Wybierz jedną, a okno zamieni się w jej ekran.
4. Kolejne **Aplikacje** dają kolejne ekrany. Wybrane aplikacje wrócą przy następnym uruchomieniu.

## Krok 6. Ekran iPada w goglach

1. Na Macu: **Ustawienia systemowe → Ogólne → AirDrop i Handoff → Odbiornik AirPlay** → włącz.
2. Na iPadzie: przesuń palcem z prawego górnego rogu (Centrum sterowania) → **Odbicie ekranu** →
   wybierz swojego Maca. Ekran iPada pokaże się na Macu.
3. W goglach stuknij **PC**. Jeśli na Macu odbicie jest w osobnym oknie, możesz też wybrać je
   w **Aplikacje**.
4. Ekran iPada w goglach to **tylko podgląd**: iOS nie pozwala sterować iPadem z zewnątrz.
   Połóż iPada na biurku i **dotykaj go normalnie**. W goglach widzisz swoje ręce i prawdziwego
   iPada przez kamerę, a jego obraz duży w VR. Pisać możesz klawiaturą Bluetooth sparowaną z iPadem.
5. Zakończenie: na iPadzie Centrum sterowania → Odbicie ekranu → **Zatrzymaj**.

---

## Sterowanie w goglach

| Co | Jak |
|---|---|
| Kliknięcie przycisku | Spójrz na przycisk i zrób uszczypnięcie (kciuk + palec wskazujący) |
| Kursor na ekranie Maca | Gładzik biurkowy (przesuwaj palcem) albo mysz Bluetooth |
| Klik w aplikacji | Stuknięcie jednym palcem w gładzik / lewy przycisk myszy |
| Prawy klik | Stuknięcie dwoma palcami / prawy przycisk myszy |
| Pisanie | Klawiatura biurkowa, klawiatura Bluetooth albo przycisk **Głos** (dyktowanie) |
| Przeniesienie okna | Przycisk „Chwyć…” pod oknem, obróć głowę, uszczypnij, aby odłożyć |
| Wszystko przed siebie | **Wyśrodkuj** w docku |

## Gdy coś nie działa

| Objaw | Co zrobić |
|---|---|
| „Wpisz IP komputera do Documents/pulpit.txt” | Krok 3: adres nie jest wpisany albo jest w linii zaczynającej się od `#` |
| „Brak połączenia z …” | Czy Terminal z serwerem jest otwarty? Czy iPhone i Mac są w tej samej sieci Wi-Fi? Czy pozwoliłeś na dostęp do sieci lokalnej (iPhone: Ustawienia → Prywatność → Sieć lokalna → CardboardXR)? |
| Czarny obraz | Brak uprawnienia **Nagrywanie ekranu** dla Terminala (krok 1.3) |
| Pusta lista w **Aplikacje** | Okna zminimalizowane lub zasłonięte; brak `pyobjc-framework-Quartz` (krok 1.2); stuknij **Odśwież** |
| „Brak okna …” | Aplikacja została zamknięta, zminimalizowana albo jest na innym biurku (Space) |
| „PC tylko podgląd” | Uruchom serwer z `--allow-input` i daj Terminalowi uprawnienie **Dostępność** |
| Obraz się tnie | Słabe Wi-Fi. Uruchom serwer z mniejszą jakością: `python3 stream_server.py --allow-input --width 960 --quality 50` |
| Mac nie widać na liście odbicia ekranu iPada | Włącz Odbiornik AirPlay (krok 6.1); to samo Apple ID i ta sama sieć Wi-Fi |

## Bezpieczeństwo

- Każdy w Twojej sieci Wi-Fi może podglądać ekran Maca, gdy serwer działa, a z `--allow-input` także
  nim sterować. Używaj tylko w domowej sieci.
- Po skończeniu zatrzymaj serwer w Terminalu klawiszami **Ctrl + C**.
- Awaryjne zatrzymanie sterowania: przesuń prawdziwą mysz Maca w róg ekranu.

## Czego się spodziewać

- Płynność: około 10–20 klatek na sekundę. Dobre do czatu, poczty, przeglądania i pracy, słabe do
  gier i szybkiego wideo.
- Ekran iPada ma dodatkowe opóźnienie (AirPlay + przesył do gogli), około 0,2–0,5 s.
- Ta funkcja jest nowa i nie była jeszcze sprawdzona na prawdziwych urządzeniach. Jeśli coś
  działa inaczej niż tu opisano, zanotuj komunikat z gogli albo z Terminala.
