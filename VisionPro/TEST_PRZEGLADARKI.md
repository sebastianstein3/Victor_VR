# Test stron w przeglądarce Workspace (2026-11-23)

**Jak testowano:** prawdziwe strony otwarte w Chromium (Playwright) ustawionym jak przeglądarka w
goglach: obszar strony jak w oknie VR (w punktach, gęstość 2×), tryb ciemny, język polski, identyfikacja
jak Safari na iPhonie, zablokowana ta sama lista trackerów co w `CardboardXRBrowser.mm`. Zrzuty wstawione
w okno przeglądarki VR (`renders/7_test_stron.png`, `8_…`, `9_…`).

**Zastrzeżenie:** na iPhonie działa silnik Safari (WebKit), nie Chromium, a test szedł z serwera w chmurze.
Wyniki pokazują układ stron w tym rozmiarze, nie zastępują testu na telefonie.

## Wyniki (8 stron, 3 rozmiary okna)

| Strona | 460 × 220 (stare domyślne) | 700 × 420 (nowe domyślne) | 940 × 500 (rozciągnięte) | Uwagi |
|---|---|---|---|---|
| DuckDuckGo | OK | OK | OK | tryb ciemny |
| Wikipedia | tylko tytuł | czytelny artykuł | dużo tekstu | strona jasna |
| YouTube (m.) | pasek nawigacji 26% okna | OK | OK | tryb ciemny, 2 trackery zablokowane |
| GitHub | pół nagłówka | OK | OK | |
| Messenger | odsyła do App Store | – | wersja mobilna bez czatu | **wersja na komputer: logowanie OK** |
| Onet | okno zgody 100% | okno zgody 100% | okno zgody 100% | kliknij „Przejdź do serwisu” raz; 3 trackery zablokowane |
| WP | okno zgody 100% | okno zgody 100% | okno zgody 100% | kliknij raz; 1 tracker zablokowany |
| Allegro | 403 | 403 | 403 | blokuje ruch z chmury – na Twoim iPhonie nie powinno |

## Co zmieniono na podstawie testu
1. **Domyślne okno przeglądarki 760 × 640 dp** (strona 700 × 420, jak iPad mini poziomo) zamiast
   520 × 440 (strona 460 × 220 pokazywała tylko nagłówki).
2. **Wersja na komputer**: automatycznie dla messenger.com, web.whatsapp.com, facebook.com i przy oknie
   900+ dp szerokości; głosem „wersja komputerowa” / „wersja mobilna” / „wersja automatyczna”.
3. **Domena i „Szyfrowane” w nagłówku okna** zamiast paska na dole strony (zasłaniał treść);
   krótkie komunikaty też w nagłówku.

## Znane ograniczenia
- Okna zgody na cookies (RODO) trzeba kliknąć raz na stronę; w oknie prywatnym za każdym razem.
  Automatycznego zamykania nie dodano: polskie serwisy (WP) używają zmiennych nazw elementów, a ukrycie
  okna bez zgody często blokuje przewijanie strony.
- Tryb ciemny działa tylko na stronach, które go obsługują.
- Większe domyślne okno przeglądarki (≈60°) zachodzi na sąsiednie sloty okien – przesuń je lub użyj
  „Wyśrodkuj”.

Powtórzenie testu: `VisionPro/renders/browser_test/capture_vr.js` (Node + Playwright), potem
`render_browser_test.py`.
