using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // What V.I.C.T.O.R. remembers about you (the hologram's "Pamięć" button, "pamięć", "co pamiętasz"): the notes
    // that go into every question, with a button to wipe them and one to switch the memory off. Only the notes
    // (at most 1500 characters, about 400 tokens) and the last 4 exchanges are sent with a question; older
    // exchanges are folded into the notes by the cheapest model the key has (AssistantSession).
    public class MemoryView : MonoBehaviour
    {
        VirtualScreen m_Screen;
        AssistantSession m_Session;
        TextMeshProUGUI m_Text;
        M3Button m_Forget, m_Toggle;
        float m_ForgetArmedUntil;

        public void Initialize(VirtualScreen screen, AssistantSession session)
        {
            m_Screen = screen;
            m_Session = session;
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(680f, 520f)));
            m_Text = M3.Text("MemoryText", m_Screen.Rect, "", M3Theme.Type.BodyMedium, VirtualScreen.BodyCenter, VirtualScreen.BodySize,
                M3Theme.OnSurface, TextAlignmentOptions.TopLeft);
            m_Text.overflowMode = TextOverflowModes.Page;
            VirtualScreen.AttachToBody(m_Text.rectTransform);

            m_Forget = M3.Button("MemoryForget", m_Screen.Rect, "Zapomnij", M3Button.Variant.Tonal, Vector2.zero, Forget, "close", 160f);
            m_Toggle = M3.Button("MemoryToggle", m_Screen.Rect, "", M3Button.Variant.Outlined, Vector2.zero, Toggle, null, 190f);
            VirtualScreen.PinToFooter((RectTransform)m_Forget.transform, 0f, 100f);
            VirtualScreen.PinToFooter((RectTransform)m_Toggle.transform, 0f, 300f);
            WorkspaceSounds.Attach(screen);
            session.Changed += Refresh;
            Refresh();
        }

        // The first press asks, a second within 4 s wipes the notes, the queue and the conversation
        void Forget()
        {
            if (Time.unscaledTime > m_ForgetArmedUntil)
            {
                m_ForgetArmedUntil = Time.unscaledTime + 4f;
                m_Forget.Text = "Na pewno?";
                return;
            }
            m_ForgetArmedUntil = 0f;
            m_Forget.Text = "Zapomnij";
            m_Session.ForgetEverything();
        }

        void Toggle() => m_Session.MemoryEnabled = !m_Session.MemoryEnabled;

        void Update()
        {
            if (m_ForgetArmedUntil > 0f && Time.unscaledTime > m_ForgetArmedUntil)
            {
                m_ForgetArmedUntil = 0f;
                m_Forget.Text = "Zapomnij";
            }
        }

        void Refresh()
        {
            if (m_Screen == null)
                return;
            bool on = m_Session.MemoryEnabled;
            m_Toggle.Text = on ? "Pamięć: włączona" : "Pamięć: wyłączona";
            m_Screen.SetTitle($"Pamięć {AssistantSession.Name}-a" + (on ? $" · {m_Session.Notes.Length} znaków" : ""));
            string notes = !on ? "Pamięć jest wyłączona: nic się nie zapisuje ani nie jest dołączane do pytań."
                : m_Session.Notes.Length == 0 ? "Jeszcze nic nie zapamiętałem. Notatki powstają, gdy rozmowa się wydłuża albo zaczniesz nową („Nowa” w oknie asystenta)."
                : m_Session.Notes;
            m_Text.text = "<noparse>" + notes.Replace("</noparse>", "") + "</noparse>\n\n<color=#938F99>Z każdym pytaniem idą tylko te notatki " +
                          "i 4 ostatnie wymiany (około 1000 tokenów); starsze streszcza najtańszy model, jaki ma Twój klucz.</color>";
        }

        void OnDestroy()
        {
            if (m_Session != null)
                m_Session.Changed -= Refresh;
        }
    }
}
