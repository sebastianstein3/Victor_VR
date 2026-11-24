# Rendery: jak Workspace wygląda w goglach

Wygenerowane z liczb, których używa aplikacja (kąty i odległości z `WorkspaceDemo.cs`, skala dp z
`M3Theme`, kolory i czcionki z projektu). Tło jednolite – w goglach w tym miejscu jest pokój z kamery.
To przybliżenie: bez zniekształcenia soczewek, bez obrazu z kamery, treść okien przykładowa.

| Plik | Co pokazuje |
|---|---|
| `1_na_wprost.png` | Patrząc prosto: notatnik, dock, początek klawiatury, z boków przeglądarka i telefon |
| `2_w_dol_biurko.png` | Patrząc w dół: klawiatura (klawisz „k” podświetlony pod palcem) i gładzik |
| `3_w_prawo_telefon.png` | W prawo: wirtualny telefon i okno Messengera z Maca |
| `4_w_lewo_przegladarka.png` | W lewo: przeglądarka (tryb ciemny, pasek postępu, domena z oznaczeniem szyfrowania) |
| `5_ekran_iphone_stereo.png` | Co wyświetla iPhone: osobny obraz dla każdego oka (63 mm rozstawu) |
| `6_rozciagniete_okno.png` | Przeglądarka rozciągnięta za uchwyt w rogu do 1000 × 640 dp (strona ułożona pod nowy rozmiar) |

Hologram Jarvisa (`render_hologram.py`, liczy tę samą geometrię co `HologramAssistant.cs` i rysuje ją addytywnie
jak `Hologram.shader`, w rozdzielczości jednego oka iPhone'a, 1311 × 1206 px; pokój jest zastępczy):

| Plik | Co pokazuje |
|---|---|
| `10_jarvis_na_biurku.png` | Hologram na biurku, mówi: szklana planeta z rozżarzonym rdzeniem w środku (jak na konsoli Jarvisa), na zewnątrz złote halo z fragmentów, komety, tarcza ze skalą i złota podstawa z radarem, „podłogą” z fragmentów i equalizerem; napisy, przyciski i panel statusu |
| `11_jarvis_stany.png` | Cztery stany: spoczynek, słucha (halo się zacieśnia), myśli (głęboki bursztyn), mówi |
| `12_jarvis_zblizenie.png` | Zbliżenie 3× (powiększone piksele, nie dodatkowe szczegóły) |

Uruchomienie: `python render_hologram.py <folder projektu Unity> <folder wyjściowy>`. Niewidoczne na zdjęciach:
ruch (pierścienie, sweep i paczki światła w włóknach się poruszają), prawdziwy pokój i dokładna odpowiedź kolorów
wyświetlacza.

Ponowne wygenerowanie (np. po zmianie układu): `pip install pillow numpy`, potem
`python render_workspace.py <folder projektu Unity> <folder wyjściowy>`.
