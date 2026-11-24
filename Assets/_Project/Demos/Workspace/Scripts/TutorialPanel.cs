using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // The "Mac and iPad screens in the headset" tutorial, inside a VirtualScreen, page by page with
    // Back / Next. The same steps in full: VisionPro/SAMOUCZEK_EKRANY.md in the repository.
    // A screen showing it isn't saved with the workspace (WorkspaceDemo.SaveWorkspace skips it).
    public class TutorialPanel : MonoBehaviour
    {
        // Each page fits the window's text area: a heading and at most ~7 short lines.
        static readonly string[] k_Pages =
        {
            "<b>Ekrany z Maca i iPada w goglach</b>\n\n" +
            "Zobaczysz tu cały ekran Maca, pojedyncze aplikacje Maca jako osobne ekrany i ekran iPada.\n\n" +
            "Aplikacji z iPhone'a w goglach nie da się pokazać: iOS wyświetla jedną aplikację naraz, " +
            "a ekran jest zajęty przez gogle. Wszystkie urządzenia: jedna sieć Wi-Fi.",

            "<b>1. Mac (raz)</b>\n\n" +
            "Terminal, wklej:\n" +
            "<noparse>pip3 install mss pillow pyobjc-framework-Quartz pyautogui pyperclip</noparse>\n" +
            "(płynny obraz: także <noparse>aiortc numpy</noparse>)\n" +
            "Ustawienia systemowe > Prywatność i ochrona: włącz Terminal w <b>Nagrywanie ekranu</b> " +
            "(i w <b>Dostępność</b>, aby klikać z gogli). Potem otwórz Terminal ponownie.",

            "<b>2. Serwer na Macu</b>\n\n" +
            "W folderze VisionPro/pc_stream uruchom:\n" +
            "<noparse>python3 stream_server.py --allow-input</noparse>\n\n" +
            "Zapisz całą linię, którą wypisze: adres i token, np.\n<noparse>192.168.1.20:8765 AbCdEf123456</noparse>\n" +
            "Token chroni dostęp. Nie zamykaj Terminala. Bez --allow-input gogle tylko oglądają.",

            "<b>3. Adres w iPhonie (raz)</b>\n\n" +
            "Stuknij w docku <b>PC</b>: powstanie plik pulpit.txt.\n" +
            "Na iPhonie: Pliki > Na moim iPhonie > CardboardXR > <b>pulpit.txt</b>.\n" +
            "Wklej w pierwszej linii całą linię z serwera (adres, spacja, token) i zapisz.\n" +
            "(Albo na Macu: Finder > iPhone > Pliki > CardboardXR.)",

            "<b>4. Mac w goglach</b>\n\n" +
            "<b>PC</b> w docku: cały ekran Maca. Przy pierwszym razie zezwól na dostęp do sieci lokalnej.\n\n" +
            "<b>Aplikacje</b> w docku (albo powiedz „aplikacje”): lista aplikacji Maca, " +
            "wybrana staje się osobnym ekranem. Okna na Macu ułóż obok siebie, niezasłonięte.",

            "<b>5. iPad w goglach</b>\n\n" +
            "Mac: Ustawienia > Ogólne > AirDrop i Handoff > <b>Odbiornik AirPlay</b>: wł.\n" +
            "iPad: Centrum sterowania > <b>Odbicie ekranu</b> > Twój Mac.\n" +
            "W goglach: <b>PC</b>. To tylko podgląd: dotykaj prawdziwego iPada na biurku, " +
            "widzisz go przez kamerę, a obraz duży w VR.",

            "<b>Sterowanie</b>\n\n" +
            "Klik: spójrz na przycisk i uszczypnij.\n" +
            "Kursor: gładzik biurkowy lub mysz Bluetooth; stuknięcie = klik, dwa palce = prawy klik.\n" +
            "Pisanie: klawiatura biurkowa, Bluetooth albo <b>Głos</b>.\n" +
            "Rozciąganie: uchwyt w prawym dolnym rogu, potem prowadź wzrokiem róg, uszczypnij = koniec.\n" +
            "Skala całego okna: <b>-</b> / <b>+</b> (do 2x).\n" +
            "Aktywne okno ma fioletowy tytuł; gest w lewo/prawo wybiera sąsiednie.\n" +
            "Głos: „zamknij”, „przenieś” i „upuść”. <b>Wyśrodkuj</b> ustawia wszystko przed Tobą.",

            "<b>Więcej: narzędzia</b>\n\n" +
            "Przycisk <b>Więcej</b> w docku: napisy, widżety (zegar, minutnik, notatka, pogoda, kalendarz), PDF, " +
            "filmy, zdjęcia 3D, środowiska 360, modele 3D, pliki.\n" +
            "<b>Pliki (iCloud)</b>: połącz raz folder z iCloud Drive, potem kopiujesz pliki w goglach. " +
            "Albo Finder (iPhone > Pliki): <b>PDF</b>, <b>Filmy</b>, <b>Srodowiska</b>, <b>Modele</b>, <b>Przestrzenne</b>.\n" +
            "Głos: napisy, pdf, filmy, pauza, pliki, środowisko, modele, zdjęcia 3d.",

            "<b>Gdy coś nie działa</b>\n\n" +
            "Brak połączenia: czy serwer działa i ta sama sieć Wi-Fi?\n" +
            "Czarny obraz: Nagrywanie ekranu dla Terminala.\n" +
            "Pusta lista Aplikacji: okna zminimalizowane lub zasłonięte.\n" +
            "Tnie się: --width 960 --quality 50.\n" +
            "Koniec: Ctrl+C w Terminalu. Pełny opis: VisionPro/SAMOUCZEK_EKRANY.md",
        };

        VirtualScreen m_Screen;
        RectTransform m_Group;
        TextMeshProUGUI m_Text;
        M3Button m_BackBtn;
        M3Button m_NextBtn;
        int m_Page;

        public static int PageCount => k_Pages.Length;

        public void Initialize(VirtualScreen screen, RectTransform canvas)
        {
            m_Screen = screen;
            m_Screen.ShowBody(false);

            // The text above, the buttons along the bottom of the window's text area (-80..140 dp)
            m_Group = M3.Group("Tutorial", canvas, VirtualScreen.BodyCenter, VirtualScreen.BodySize);
            float bodyHalf = VirtualScreen.BodySize.y / 2f;
            m_Text = M3.Text("TutorialText", m_Group, "", M3Theme.Type.BodyMedium,
                new Vector2(0f, 32f), new Vector2(VirtualScreen.BodySize.x, VirtualScreen.BodySize.y - 64f),
                M3Theme.OnSurface, TextAlignmentOptions.TopLeft);
            float buttonY = -bodyHalf + M3.k_ButtonHeight / 2f;
            m_BackBtn = M3.Button("TutorialBack", m_Group, "Wstecz", M3Button.Variant.Outlined,
                new Vector2(-120f, buttonY), () => ShowPage(m_Page - 1), "chevron_left", 140f);
            m_NextBtn = M3.Button("TutorialNext", m_Group, "Dalej", M3Button.Variant.Filled,
                new Vector2(120f, buttonY), () => ShowPage(m_Page + 1), "chevron_right", 140f);
            WorkspaceSounds.Attach(m_Group);
            ShowPage(0);
        }

        void ShowPage(int page)
        {
            if (page >= k_Pages.Length)
            {
                // "Zamknij" on the last page closes the tutorial window
                m_Screen.Close();
                return;
            }
            m_Page = Mathf.Clamp(page, 0, k_Pages.Length - 1);
            m_Text.text = k_Pages[m_Page];
            m_Screen.SetTitle($"Samouczek {m_Page + 1}/{k_Pages.Length}");
            m_BackBtn.Interactable = m_Page > 0;
            m_NextBtn.Text = m_Page == k_Pages.Length - 1 ? "Zamknij" : "Dalej";
        }
    }
}
