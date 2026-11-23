using System.Collections;
using System.Collections.Generic;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // In a VirtualScreen: the list of apps with a window on the computer (stream_server.py /windows);
    // picking one turns the screen into that app's own screen (RemoteDesktopReceiver with ?app=). So
    // Messenger, a browser, a mirrored Android phone (scrcpy) and so on can each be a separate screen.
    // Apps of the iPhone in the headset itself can't be shown: iOS doesn't let one app see another's
    // screen, and the phone's only screen is busy with the headset.
    public class AppPicker : MonoBehaviour
    {
        const int k_Columns = 2;
        const int k_Rows = 3;
        const float k_ButtonWidth = 222f;

        VirtualScreen m_Screen;
        RectTransform m_Group;
        TextMeshProUGUI m_Status;
        readonly List<string> m_Apps = new();
        int m_Page;

        public void Initialize(VirtualScreen screen, RectTransform canvas)
        {
            m_Screen = screen;
            m_Screen.ShowBody(false);
            m_Group = M3.Group("AppList", canvas, VirtualScreen.BodyCenter, VirtualScreen.BodySize);
            m_Status = M3.Text("AppListStatus", canvas, "Szukam aplikacji na komputerze...", M3Theme.Type.BodyLarge,
                VirtualScreen.BodyCenter, new Vector2(VirtualScreen.BodySize.x, 60f), M3Theme.OnSurfaceVariant);
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            m_Status.gameObject.SetActive(true);
            m_Status.text = "Szukam aplikacji na komputerze...";
            yield return RemoteDesktopReceiver.FetchApps(apps =>
            {
                m_Apps.Clear();
                m_Apps.AddRange(apps);
                m_Page = 0;
                if (m_Apps.Count == 0)
                    m_Status.text = "Brak widocznych okien. Otwórz aplikacje na komputerze (obok siebie, nie zminimalizowane) i odśwież.";
                else
                    m_Status.gameObject.SetActive(false);
                ShowPage();
            }, error =>
            {
                m_Status.text = "<noparse>" + error + "</noparse>";
                ShowPage();
            });
        }

        // 2 x 3 buttons in the text area: up to 5 apps and "Dalej" (more) or "Odśwież" (reload)
        void ShowPage()
        {
            foreach (Transform child in m_Group)
                Destroy(child.gameObject);

            int perPage = k_Columns * k_Rows - 1; // the last place is for Dalej / Odśwież
            int pages = Mathf.Max(1, Mathf.CeilToInt(m_Apps.Count / (float)perPage));
            m_Page %= pages;
            int first = m_Page * perPage;
            int shown = Mathf.Min(perPage, m_Apps.Count - first);

            for (int i = 0; i <= shown; i++)
            {
                // Place i in the grid: rows 64 dp apart (56 dp buttons + 8 dp), columns centred
                int column = i % k_Columns, row = i / k_Columns;
                var position = new Vector2((column - 0.5f) * (k_ButtonWidth + 8f), 64f - row * 64f);
                if (i < shown)
                {
                    string app = m_Apps[first + i];
                    M3.Button($"App_{i}", m_Group, app, M3Button.Variant.Tonal, position, () => Pick(app), null, k_ButtonWidth);
                }
                else if (pages > 1)
                {
                    M3.Button("NextPage", m_Group, $"Dalej ({m_Page + 1}/{pages})", M3Button.Variant.Outlined, position,
                        () => { m_Page++; ShowPage(); }, null, k_ButtonWidth);
                }
                else
                {
                    M3.Button("Reload", m_Group, "Odśwież", M3Button.Variant.Outlined, position,
                        () => StartCoroutine(Load()), "refresh", k_ButtonWidth);
                }
            }
            WorkspaceSounds.Attach(m_Group);
        }

        void Pick(string app)
        {
            Destroy(m_Group.gameObject);
            Destroy(m_Status.gameObject);
            m_Screen.SetTitle(app);
            m_Screen.EnableRemoteDesktop(null, app);
            Destroy(this);
        }
    }
}
