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
2. Terminal wypisze linię z adresem i **tokenem** (hasłem dostępu), np.:
   ```
   Wpisz do iPhone'a, plik Documents/pulpit.txt (jedna linia):  192.168.1.20:8765 AbCdEf123456
   ```
   Zapisz całą linię. Token jest stały (zapisany na Macu), więc wpisujesz go raz. Bez niego nikt
   inny w sieci Wi-Fi nie zobaczy ekranu. Nowy token: `--new-token`. Terminal zostaw otwarty.

## Krok 2b. Płynny obraz – WebRTC (opcjonalnie, polecane)

Zamiast ~10–20 kl/s (JPEG) ekran leci jako szyfrowane wideo ~30 kl/s.

1. Mac: `pip3 install aiortc numpy` i uruchom serwer ponownie (wypisze „WebRTC: dostępny”).
2. Unity (raz, przed budowaniem): **Window → Package Manager → + → Add package by name** →
   `com.unity.webrtc`, wersja `3.0.0-pre.8` → Add. To pakiet w wersji przedpremierowej; jeśli
   sprawi problemy, usuń go – aplikacja wróci do trybu JPEG.
3. Bez któregokolwiek z tych kroków wszystko działa dalej w trybie JPEG (okno pokaże, który tryb jest użyty).

## Krok 3. Wpisz adres Maca do iPhone'a (raz)

1. Na iPhonie otwórz CardboardXR, wejdź do **Spatial Workspace** i stuknij w docku **PC**. Aplikacja
   utworzy wtedy plik `pulpit.txt`. Za pierwszym razem pokaże komunikat, że brakuje adresu.
2. Otwórz aplikację **Pliki** → **Na moim iPhonie** → **CardboardXR** → **pulpit.txt**.
3. W pierwszej pustej linii wpisz **całą linię** z kroku 2 (adres, spacja, token), np.
   `192.168.1.20:8765 AbCdEf123456`, i zapisz.
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

## Przeglądarka w goglach (bez komputera)

W docku **Aplikacje** → **Przeglądarka (iPhone)** albo powiedz „przeglądarka”. To silnik Safari
działający na iPhonie, wyświetlany jako okno w VR.

- Klik: spójrz na stronę i uszczypnij (albo gładzik / mysz).
- Adres: przycisk **Adres** pod oknem → pisz → **Enter**. Słowa bez kropki szukają w DuckDuckGo.
- Pisanie i dyktowanie trafiają do pola, w które kliknąłeś na stronie.
- Przewijanie: machnięcie otwartą dłonią w górę/dół albo kółko myszy. Głosem: „wstecz”, „odśwież”.
- Bezpieczeństwo: tylko strony https (http tylko w sieci domowej); inne rodzaje adresów
  (pliki, skrypty, telefon, aplikacje) są blokowane; strony nie mogą otwierać okien ani pobierać plików.
- Ok. 10 klatek na sekundę – do czytania i pisania, nie do filmów.

## Sterowanie w goglach

| Co | Jak |
|---|---|
| Kliknięcie przycisku | Spójrz na przycisk i zrób uszczypnięcie (kciuk + palec wskazujący) |
| Kursor na ekranie Maca | Gładzik biurkowy (przesuwaj palcem) albo mysz Bluetooth |
| Klik w aplikacji | Stuknięcie jednym palcem w gładzik / lewy przycisk myszy |
| Prawy klik | Stuknięcie dwoma palcami / prawy przycisk myszy |
| Pisanie | Klawiatura biurkowa, klawiatura Bluetooth albo przycisk **Głos** (dyktowanie) |
| Przeniesienie okna | Przycisk „Chwyć…” pod oknem, obróć głowę, uszczypnij, aby odłożyć |
| Rozciąganie okna (szerokość i wysokość osobno) | Uchwyt w **prawym dolnym rogu** okna → róg podąża za wzrokiem → uszczypnij (albo stuknij uchwyt), aby zakończyć. Głosem: „rozciągnij”. Przeglądarka układa wtedy stronę od nowa pod nowy rozmiar |
| Skala całego okna (z tekstem) | **-** / **+** pod oknem (0,6–2×) albo głosem „większe okno” / „mniejsze okno” |
| Rozmiar tekstu strony (przeglądarka) | **Aa** pod oknem albo „większy tekst” / „mniejszy tekst” |
| Wszystko przed siebie | **Wyśrodkuj** w docku |

## Narzędzia z menu Więcej

Przycisk **Więcej** w docku (albo głos „więcej”) otwiera menu (dwie strony 3 × 3): Asystent, Galeria, Filmy,
Widżety (Zegar, Minutnik, Notatka, Pogoda, Kalendarz), Napisy, Układy, Gry, Rysowanie, PDF, Zdjęcia 3D,
Środowisko, Modele 3D, Pliki, Zrzut ekranu, Samouczek. Drugie naciśnięcie **Więcej** zamyka menu.

| Narzędzie | Co robi | Pliki i głos |
|---|---|---|
| Napisy na żywo | Napisy z mowy i tłumaczenie pl↔en (zgodnie z językiem przycisku **PL**). Wymaga iOS 26.4+ i pobranych języków w Ustawieniach iOS | „napisy” |
| Zegar, Minutnik | Małe panele, zostają tam, gdzie je odłożysz | „zegar”, „minutnik” |
| Notatka | Notatka dyktowana głosem lub z klawiatury | „notatka” |
| Pogoda | Open-Meteo; miasto w pliku `pogoda.txt` (domyślnie Warszawa) | „pogoda” |
| Kalendarz | Wydarzenia z kalendarza iPhone'a (zezwól na dostęp do kalendarza) | „kalendarz” |
| Czytnik PDF | Strony: przyciski w stopce, gest otwartej dłoni góra/dół, „następna strona” / „poprzednia strona”. Rozciągnięte okno renderuje się ostrzej | folder `PDF` (albo `Documents`), „pdf” |
| Środowisko | Zmierzch, Noc, Ocean, Kosmos albo własne zdjęcie 360 (.jpg/.png equirectangular) lub wideo 360 (.mp4/.mov). Kamera przezroczysta jest wtedy wyłączona | folder `Srodowiska`; „środowisko”, „pokój” wyłącza |
| Modele 3D | .glb/.gltf stawiane na prawdziwej powierzchni, na którą patrzysz. **Chwytanie:** uszczypnij model (ze śledzeniem dłoni 6DoF idzie za palcami, obrót nadgarstka go obraca, rozwarcie palców odstawia go na stół lub podłogę pod nim); bez pozycji dłoni idzie za głową, a uszczypnięcie gdziekolwiek go odstawia (to samo robi przycisk **Chwyć**). Panel: -30° / +30° / - / + obraca i skaluje, **Na stół** przynosi model na powierzchnię, na którą patrzysz. Modele zapisują się w układzie. **Pobierz modele:** katalog darmowych modeli (Khronos glTF Sample Assets, CC0 / CC-BY 4.0; obok pliku `.zrodlo.txt` ze źródłem) albo własne linki https do .glb w pliku `modele_url.txt` (limit 60 MB; plik musi być prawdziwym .glb). Wymaga pakietu `com.unity.cloud.gltfast` w buildzie | folder `Modele`; „modele”, „pobierz model” |
| Zdjęcia 3D | Zdjęcia przestrzenne HEIC i filmy przestrzenne MV-HEVC z iPhone'a 15 Pro / 16 Pro (filmy: iOS 17+), obrazy obok siebie .jpg/.png z `_sbs` w nazwie, inne obrazy płasko. Każde oko widzi swój obraz. Dalej/wstecz: przyciski w stopce, gest góra/dół, „następne” / „poprzednie” | folder `Przestrzenne`; „zdjęcia 3d” |
| Filmy | Twoje filmy .mp4/.mov/.m4v (H.264 lub HEVC) w oknie: -10 s, pauza, +10 s, następny, **Kino** (przyciemnia pokój i pokazuje film na dużym ekranie kinowym). Okno można rozciągnąć | folder `Filmy`; „filmy”, „pauza” / „odtwórz”, „następne” |
| Asystent AI | Pytasz głosem („Zapytaj”, albo mów, gdy okno asystenta jest aktywne) lub piszesz i naciskasz Enter; odpowiada Claude, a odpowiedź jest czytana w AirPods (przycisk **Głos** wyłącza czytanie). **Klucz:** plik `klucz_ai.txt` z kluczem z console.anthropic.com w folderze aplikacji – przy otwarciu trafia do pęku kluczy iPhone'a, a plik jest usuwany. Płatne wg cennika Anthropic | „asystent”, „zapytaj …” z każdego miejsca, „usuń klucz” |
| Hologram (V.I.C.T.O.R.) | Asystent AI **V.I.C.T.O.R.** jako **hologram w stylu Jarvisa**. **Wnętrze:** szklana „planeta” z południkami i równoleżnikami, rozżarzonym pomarańczowo-białym rdzeniem i pierścieniami z krążącymi kropkami. **Na zewnątrz** (jak złoty hologram z filmu): halo z tysięcy świecących fragmentów układów scalonych na dwóch obracających się powłokach, jasne łuki-komety z iskrą na czele, złote iskry, cienka tarcza ze skalą zwrócona do Ciebie oraz podstawa projektora w tym samym złocie (segmentowe pierścienie, „podłoga” z fragmentów, radar, ząbki-equalizer, smuga światła). Stoi na stole, na który patrzysz, albo wisi w powietrzu. Reaguje na to, co robi asystent: słucha (halo i pierścienie się zacieśniają), myśli (szybciej wiruje, głęboki bursztyn, światło szybko biegnie fragmentami), mówi (rdzeń i ząbki pulsują z każdym słowem, z planety wychodzą fale światła, a słowa pojawiają się jako napisy z podświetlonym bieżącym). Obok **panel statusu** (FPS, pamięć, bateria, czasy ostatnich odpowiedzi, stan, model, język głosu, czas pracy; „ukryj hud” / „pokaż hud”). Pod spodem nazwa i przyciski: stop, zamknij, **Zapytaj**, **Na blat** (stawia hologram na biurku albo stole, na który patrzysz; głosem „hologram na blat”), **Model** (wybór modelu z listy dla Twojego klucza) i **Pamięć** (notatki o Tobie). Uszczypnij go, aby chwycić i przenieść (obrót nadgarstka go obraca). Pytania: „Victor, …” (albo „Jarvis, …”) z każdego miejsca albo **Zapytaj**. Wymaga klucza jak Asystent | „jarvis”, „hologram”, „wyłącz hologram” |
| Galeria | Zdjęcia i filmy z aplikacji Zdjęcia, bez kopiowania: miniatury, filtr Wszystkie / Przestrzenne (iOS 18+) / Filmy, strony gestem góra/dół. Uszczypnięcie otwiera oryginał (z iCloud, jeśli trzeba) w nowym oknie: zdjęcia i przestrzenne w 3D, filmy w odtwarzaczu | „galeria” |
| Układy | Zapis i otwieranie zestawów okien, widżetów, środowiska i trybu kina: Praca, Kino, Czytanie, Własny. Otwarcie zamyka obecne okna i ustawia układ przed Tobą | „układ praca”, „zapisz układ kino” |
| Gry | Warcaby (Ty białymi, zasady polskie: bicie obowiązkowe i najdłuższe, damki dalekiego zasięgu), chińczyk (Ty czerwonym, 3 graczy komputerowych) i kółko i krzyżyk. Plansza staje na stole, na który patrzysz; ruch: spójrz na pionek i uszczypnij, potem na pole. **Kółko i krzyżyk rysujesz tuszem jak w „Rysowaniu”:** uszczypnij nad wolnym polem i narysuj krzyżyk (dowolny bazgroł, środek kreski wybiera pole) – zamieni się w równy krzyżyk, a komputer odpowie narysowanym kółkiem; bez śledzenia dłoni 6DoF po prostu uszczypnij pole. Przycisk **Poziom** przełącza łatwy / trudny. Panel obok: Nowa gra, kostka / poziom, Na stół | „warcaby”, „chińczyk”, „kółko i krzyżyk”, „rzuć” |
| Rysowanie | Uszczypnij w powietrzu i rysuj (6 kolorów, 3 grubości, Cofnij, Wyczyść – dwa razy). Rysunki zostają w pokoju po zamknięciu panelu. Wymaga śledzenia dłoni 6DoF | „rysuj”, „cofnij” |
| Zrzut ekranu | Zapisuje widok lewego oka w aplikacji Zdjęcia (zgoda na dodawanie) | „zrzut ekranu” |
| Pliki (iCloud) | Kopiuje pliki z iCloud Drive (i innych miejsc z aplikacji Pliki) do właściwych folderów aplikacji – bez Findera. Patrz niżej | „pliki” |

**Jak wgrać pliki:** na Macu Finder > iPhone > Pliki > CardboardXR, przeciągnij pliki do właściwego folderu
(`PDF`, `Srodowiska`, `Modele`, `Przestrzenne`; brakujący folder utwórz). Albo w aplikacji Pliki na iPhonie:
Na moim iPhonie > CardboardXR. Nazwy folderów bez polskich znaków, jak wyżej.

**Bez Findera – Więcej > Pliki:**
1. **Z telefonu (zdejmij gogle):** okno wyboru plików pojawi się na ekranie iPhone'a. Wyjmij telefon z gogli,
   zaznacz pliki (iCloud Drive, Na moim iPhonie, Dysk Google…), włóż telefon z powrotem.
2. **Połącz folder iCloud** (raz): wybierz folder, np. utwórz na Macu `iCloud Drive/VisionPro` i wrzucaj tam
   pliki. Od tej pory jego zawartość widać w oknie „Pliki” w goglach – jedno uszczypnięcie kopiuje plik
   (ikona chmury: plik pobierze się z iCloud; ptaszek: już skopiowany). „Odśwież” czyta folder od nowa.

Każdy plik trafia sam do właściwego folderu: PDF → `PDF`, .glb → `Modele`, filmy → `Filmy` (przestrzenne
MV-HEVC → `Przestrzenne`, z „360” w nazwie → `Srodowiska`), zdjęcia 2:1 → `Srodowiska`, pozostałe zdjęcia i
HEIC → `Przestrzenne`, reszta → `Inne`. Nic nie jest nadpisywane: drugi plik o tej samej nazwie dostaje „(2)”.

## Gdy coś nie działa

| Objaw | Co zrobić |
|---|---|
| „Zły lub brak tokenu” | Wpisz do `pulpit.txt` całą linię z serwera (adres i token po spacji) |
| „Wpisz IP komputera do Documents/pulpit.txt” | Krok 3: adres nie jest wpisany albo jest w linii zaczynającej się od `#` |
| „Brak połączenia z …” | Czy Terminal z serwerem jest otwarty? Czy iPhone i Mac są w tej samej sieci Wi-Fi? Czy pozwoliłeś na dostęp do sieci lokalnej (iPhone: Ustawienia → Prywatność → Sieć lokalna → CardboardXR)? |
| Czarny obraz | Brak uprawnienia **Nagrywanie ekranu** dla Terminala (krok 1.3) |
| Pusta lista w **Aplikacje** | Okna zminimalizowane lub zasłonięte; brak `pyobjc-framework-Quartz` (krok 1.2); stuknij **Odśwież** |
| „Brak okna …” | Aplikacja została zamknięta, zminimalizowana albo jest na innym biurku (Space) |
| „PC tylko podgląd” | Uruchom serwer z `--allow-input` i daj Terminalowi uprawnienie **Dostępność** |
| Obraz się tnie | Słabe Wi-Fi. Uruchom serwer z mniejszą jakością: `python3 stream_server.py --allow-input --width 960 --quality 50` |
| Mac nie widać na liście odbicia ekranu iPada | Włącz Odbiornik AirPlay (krok 6.1); to samo Apple ID i ta sama sieć Wi-Fi |

## Bezpieczeństwo

- Dostęp chroni token: bez niego serwer odmawia (błąd 401). Token i obraz idą jednak w sieci
  lokalnej bez szyfrowania, więc używaj tylko domowej sieci Wi-Fi, nie publicznej.
- `pulpit.txt` z tokenem leży w plikach aplikacji na iPhonie (widocznych w Finderze i aplikacji Pliki).
- Po skończeniu zatrzymaj serwer w Terminalu klawiszami **Ctrl + C**.
- Awaryjne zatrzymanie sterowania: przesuń prawdziwą mysz Maca w róg ekranu.

## Czego się spodziewać

- Płynność: około 10–20 klatek na sekundę. Dobre do czatu, poczty, przeglądania i pracy, słabe do
  gier i szybkiego wideo.
- Ekran iPada ma dodatkowe opóźnienie (AirPlay + przesył do gogli), około 0,2–0,5 s.
- Ta funkcja jest nowa i nie była jeszcze sprawdzona na prawdziwych urządzeniach. Jeśli coś
  działa inaczej niż tu opisano, zanotuj komunikat z gogli albo z Terminala.

## V.I.C.T.O.R. – klucz, pamięć i narzędzia

1. **Klucz API:** załóż go na https://platform.claude.com/settings/keys. Skopiuj (na Macu Schowek uniwersalny przeniesie go na iPhone'a)
   i powiedz „wklej klucz” albo naciśnij **Klucz** w oknie asystenta; iOS zapyta o zgodę na wklejenie. Klucz jest sprawdzany, trafia
   do pęku kluczy, a schowek jest czyszczony. Nigdy nie wklejaj go do czatu ani do commitów.
2. **Model:** hologram > **Model** albo „zmień model”. Droższy model = lepsze, ale wolniejsze i droższe odpowiedzi.
3. **Pamięć:** „pamięć” pokazuje notatki, „zapomnij wszystko” je kasuje. Z każdym pytaniem idzie ok. 1000 tokenów kontekstu.
4. **Research:** „Victor, sprawdź w internecie, …” (wyszukiwanie liczone osobno wg cennika Anthropic). „wyłącz research” je zatrzymuje.
5. **Pliki:** V.I.C.T.O.R. czyta i zapisuje pliki tekstowe i PDF-y w folderze aplikacji (Finder > iPhone > Pliki) oraz w **połączonym
   folderze**: „Więcej > Pliki > Połącz folder”, a w oknie wyboru wybierz folder z dostawcy **Dysk Google** (jeśli masz na iPhonie
   aplikację Dysk Google, widać ją w aplikacji Pliki). Zapis zawsze po Twojej zgodzie: panel **Zezwól / Odmów** albo „tak” / „nie”.
6. **MCP (opcjonalnie, Mac w tej samej sieci Wi-Fi):** uruchom serwer MCP na Macu w trybie Streamable HTTP, np. Google Workspace MCP
   (https://github.com/taylorwilsdon/google_workspace_mcp, MIT): według jego README po utworzeniu klienta OAuth w Google Cloud
   `uvx workspace-mcp --tool-tier core` (tryb HTTP: `--transport streamable-http`, domyślnie `http://localhost:8000/mcp`). Serwer
   musi być osiągalny z iPhone'a (adres Maca zamiast `localhost`; sprawdź w README, jak ustawić nasłuchiwanie). W `mcp_servery.txt`
   (Documents aplikacji; powstaje po „odśwież mcp”) dopisz linię `drive http://ADRES_MACA:8000/mcp`, powiedz „odśwież mcp”.
   Logowanie do Google robi serwer na Macu. Narzędzia, które coś zmieniają, pytają o zgodę.
7. **Wyłączniki:** „wyłącz narzędzia” (V.I.C.T.O.R. tylko rozmawia), „wyłącz research”.
