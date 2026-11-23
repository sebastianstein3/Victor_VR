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
