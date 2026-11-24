using System.Text;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // The assistant's window (Więcej > Asystent, "asystent"): the conversation as text. Ask by voice (the
    // "Zapytaj" button, or any dictation while this window is active) or type a question with a keyboard
    // and press Enter. The conversation itself, the Claude API and reading the answers aloud are the
    // AssistantSession's (shared with the hologram); this is only its window.
    public class AssistantView : MonoBehaviour
    {
        VirtualScreen m_Screen;
        AssistantSession m_Session;
        TextMeshProUGUI m_Text;
        M3Button m_SpeakButton;
        readonly StringBuilder m_Shown = new();
        string m_LastDraft = "";

        public void Initialize(VirtualScreen screen, AssistantSession session)
        {
            m_Screen = screen;
            m_Session = session;
            m_Screen.SetTitle("Asystent AI");
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetText("");
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(760f, 540f)));
            m_Text = M3.Text("AssistantText", m_Screen.Rect, "", M3Theme.Type.BodyLarge, VirtualScreen.BodyCenter,
                VirtualScreen.BodySize, M3Theme.OnSurface, TextAlignmentOptions.BottomLeft);
            m_Text.overflowMode = TextOverflowModes.Page;
            VirtualScreen.AttachToBody(m_Text.rectTransform);

            var ask = M3.Button("AssistantAsk", m_Screen.Rect, "Zapytaj", M3Button.Variant.Filled, Vector2.zero, session.Listen, "pinch", 130f);
            m_SpeakButton = M3.Button("AssistantSpeak", m_Screen.Rect, "Głos", M3Button.Variant.Tonal, Vector2.zero, ToggleReadAloud, null, 90f);
            var reset = M3.Button("AssistantNew", m_Screen.Rect, "Nowa", M3Button.Variant.Text, Vector2.zero, session.NewConversation, "refresh", 100f);
            var key = M3.Button("AssistantKey", m_Screen.Rect, "Klucz", M3Button.Variant.Outlined, Vector2.zero, session.PasteKeyFromClipboard, null, 100f);
            VirtualScreen.PinToFooter((RectTransform)ask.transform, 0f, 75f);
            VirtualScreen.PinToFooter((RectTransform)m_SpeakButton.transform, 0f, 190f);
            VirtualScreen.PinToFooter((RectTransform)reset.transform, 0f, 290f);
            VirtualScreen.PinToFooter((RectTransform)key.transform, 0f, 395f);
            m_SpeakButton.Selected = session.ReadAloud;
            WorkspaceSounds.Attach(screen);

            session.UserSaid += OnUserSaid;
            session.Answered += OnAnswered;
            session.Notice += OnNotice;
            session.Changed += OnChanged;

            if (!session.RefreshKey())
                Show("Brak klucza API Claude.\n\nSkopiuj klucz ze strony platform.claude.com/settings/keys (na Macu: Schowek uniwersalny przeniesie go na " +
                     "iPhone'a) i naciśnij „Klucz” (albo powiedz „wklej klucz”): zostanie sprawdzony i schowany w pęku kluczy, a schowek wyczyszczony. " +
                     $"Można też wrzucić plik „{AssistantSession.KeyFileName}” z kluczem do folderu aplikacji.");
            else
                Show($"Zapytaj {AssistantSession.Name}-a o cokolwiek: przycisk „Zapytaj”, mów, gdy to okno jest aktywne, albo pisz i naciśnij Enter.\n" +
                     "Głosem z każdego miejsca: „Victor, …”. „Hologram” pokazuje go jako hologram, „zmień model” wybiera model, „pamięć” pokazuje notatki.");
        }

        void ToggleReadAloud()
        {
            m_Session.ReadAloud = !m_Session.ReadAloud;
            m_SpeakButton.Selected = m_Session.ReadAloud;
        }

        void OnUserSaid(string question) => Append("<b>Ty:</b> " + Escape(question));
        void OnAnswered(string answer) => Append($"<b>{AssistantSession.Name}:</b> " + Escape(answer));
        void OnNotice(string notice) => Append("<color=#F2B8B5>" + Escape(notice) + "</color>");

        void OnChanged()
        {
            if (m_Screen == null)
                return;
            switch (m_Session.State)
            {
                case AssistantState.Thinking: m_Screen.SetTitle("Asystent AI – myślę…"); break;
                case AssistantState.Listening: m_Screen.SetTitle("Asystent AI – słucham…"); break;
                case AssistantState.Speaking: m_Screen.SetTitle("Asystent AI – mówię"); break;
                default: m_Screen.SetTitle("Asystent AI"); break;
            }
        }

        void Show(string text)
        {
            m_Shown.Clear();
            Append(text);
        }

        void Append(string line)
        {
            if (m_Text == null)
                return;
            if (m_Shown.Length > 0)
                m_Shown.Append("\n\n");
            m_Shown.Append(line);
            // Keep the end in view: the newest at the bottom, older pages scroll away
            m_Text.text = m_Shown.ToString();
            m_Text.ForceMeshUpdate();
            m_Text.pageToDisplay = Mathf.Max(1, m_Text.textInfo.pageCount);
        }

        void Update()
        {
            // Typed questions: what the keyboard types into this window, sent on Enter
            if (m_Screen == null)
                return;
            string draft = m_Screen.Text ?? "";
            if (draft == m_LastDraft)
                return;
            m_LastDraft = draft;
            if (draft.EndsWith("\n"))
            {
                m_Screen.SetText("");
                m_LastDraft = "";
                m_Session.Ask(draft);
            }
            else if (!m_Session.Busy && draft.Length > 0)
                m_Screen.SetTitle("Pytanie: " + (draft.Length > 40 ? "…" + draft.Substring(draft.Length - 40) : draft));
        }

        void OnDestroy()
        {
            if (m_Session == null)
                return;
            m_Session.UserSaid -= OnUserSaid;
            m_Session.Answered -= OnAnswered;
            m_Session.Notice -= OnNotice;
            m_Session.Changed -= OnChanged;
        }

        static string Escape(string text) => "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";
    }
}
