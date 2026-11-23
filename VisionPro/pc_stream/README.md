# Ekran komputera w goglach (okno „PC” w Spatial Workspace)

1. Na komputerze (Windows lub Mac, Python 3.9+):
   ```
   pip install mss pillow
   python stream_server.py
   ```
   Do sterowania z gogli (klik z gładzika, pisanie, dyktowanie) doinstaluj `pyautogui pyperclip`
   i uruchom `python stream_server.py --allow-input`.
   macOS: Ustawienia systemowe → Prywatność i ochrona → **Nagrywanie ekranu** (i **Dostępność** dla
   `--allow-input`) dla Terminala.
2. Skrypt wypisze adres, np. `192.168.1.20:8765`. Na iPhonie: aplikacja **Pliki** → CardboardXR →
   `pulpit.txt` (powstaje po pierwszym otwarciu okna PC) → wpisz adres w pierwszej linii.
3. W goglach: dock → **PC**. iOS zapyta raz o dostęp do sieci lokalnej – zezwól.

Kursor okna przesuwa gładzik biurkowy lub mysz Bluetooth; stuknięcie = klik, dwa palce = prawy klik.
Pisanie i dyktowanie przy aktywnym oknie PC idą do komputera.

Wydajność: jedna klatka JPEG na zapytanie, zwykle 10–20 kl/s w dobrym Wi-Fi (`--width 960` lub
`--quality 50` przyspiesza). To nie jest streaming do gier; następny krok to Moonlight/WebRTC.

**Bezpieczeństwo:** każdy w tej samej sieci Wi-Fi widzi ekran, a z `--allow-input` może nim sterować.
Tylko w domowej sieci; zatrzymuj skryptem Ctrl+C po użyciu.

## Każda aplikacja jako osobny ekran (dock → „Aplikacje”)

Okno w goglach pokazuje listę aplikacji z widocznym oknem na komputerze; wybrana staje się osobnym
ekranem (kolejne „Aplikacje” = kolejne ekrany). Na Macu doinstaluj `pip install pyobjc-framework-Quartz`.

- Okno jest wycinane z ekranu tam, gdzie leży, więc **nie może być zasłonięte ani zminimalizowane**.
  Ułóż aplikacje obok siebie; Mac mini bez monitora: duży wirtualny ekran, np. darmowy **BetterDisplay**.
- Klik i pisanie najpierw przywołują tę aplikację na wierzch (`--allow-input`).
- Co da się tak pokazać: aplikacje Maca (Messenger, WhatsApp, Safari/Chrome z TikTok, Instagram,
  YouTube), aplikacje z iPhone'a/iPada działające na Macu z Apple Silicon (jeśli twórca pozwala),
  a także drugi telefon z Androidem przez `scrcpy` (okno „scrcpy” na liście).
- **Aplikacji z iPhone'a, który jest w goglach, nie da się pokazać**: iOS nie pozwala jednej aplikacji
  widzieć ekranu innej, a jedyny ekran telefonu jest zajęty przez gogle. iPhone Mirroring z macOS jest
  niedostępne w UE i wymaga zablokowanego iPhone'a.
