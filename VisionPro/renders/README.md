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

Ponowne wygenerowanie (np. po zmianie układu): `pip install pillow numpy`, potem
`python render_workspace.py <folder projektu Unity> <folder wyjściowy>`.
