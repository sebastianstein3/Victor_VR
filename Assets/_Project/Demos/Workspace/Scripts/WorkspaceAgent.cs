using System;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // What the assistant's tools reach in the workspace (IAgentHost), and the question it asks before it changes
    // anything: a small panel in front of the user with Zezwól / Odmów, or "tak" / "nie" by voice. An answer not
    // given within 50 seconds is a no.
    public partial class WorkspaceDemo
    {
        RectTransform m_ConfirmPanel;
        TextMeshProUGUI m_ConfirmText;
        Action<bool> m_ConfirmAnswer;
        float m_ConfirmUntil;

        // Only the phrases of the allowed list run (AgentToolbox.WorkspacePhrases): nothing that deletes or changes
        // settings, keys or memory
        public bool RunWorkspaceCommand(string phrase) =>
            Array.IndexOf(AgentToolbox.WorkspacePhrases, phrase) >= 0 && TryVoiceCommand(phrase);

        public bool OpenUrl(string url)
        {
            if (m_Core == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;
            CreateBrowserScreen(uri.AbsoluteUri);
            return true;
        }

        public void ShowNote(string text)
        {
            var widget = SpawnWidget("notatka");
            if (widget != null)
                widget.LoadData(text);
        }

        public void Confirm(string question, Action<bool> answer)
        {
            if (m_Core == null || m_ConfirmAnswer != null) // one question at a time
            {
                answer(false);
                return;
            }
            if (m_ConfirmPanel == null)
            {
                m_ConfirmPanel = WorldUi.Panel("Potwierdź", transform, new Vector2(440f, 210f), 0.95f, M3Theme.SurfaceContainerHigh, 24f);
                m_ConfirmText = M3.Text("ConfirmText", m_ConfirmPanel, "", M3Theme.Type.BodyLarge, new Vector2(0f, 38f), new Vector2(400f, 100f),
                    M3Theme.OnSurface);
                M3.Button("ConfirmYes", m_ConfirmPanel, "Zezwól", M3Button.Variant.Filled, new Vector2(-100f, -64f), () => ResolveConfirm(true), "check", 170f);
                M3.Button("ConfirmNo", m_ConfirmPanel, "Odmów", M3Button.Variant.Tonal, new Vector2(100f, -64f), () => ResolveConfirm(false), "close", 170f);
                WorkspaceSounds.Attach(m_ConfirmPanel);
            }
            m_ConfirmAnswer = answer;
            m_ConfirmUntil = Time.unscaledTime + 45f;
            m_ConfirmText.text = "<noparse>" + (question ?? "").Replace("</noparse>", "") + "</noparse>";
            WorldUi.PlaceInFront(m_ConfirmPanel, m_Core.Head, 0.95f, -4f);
            m_ConfirmPanel.gameObject.SetActive(true);
        }

        void ResolveConfirm(bool yes)
        {
            var answer = m_ConfirmAnswer;
            m_ConfirmAnswer = null;
            if (m_ConfirmPanel != null)
                m_ConfirmPanel.gameObject.SetActive(false);
            answer?.Invoke(yes);
        }

        void UpdateConfirm()
        {
            if (m_ConfirmAnswer != null && Time.unscaledTime > m_ConfirmUntil)
                ResolveConfirm(false);
        }

        // While a question is open, "tak" / "nie" answer it (and nothing else is taken from that sentence)
        bool HandleConfirmSpeech(string spoken)
        {
            string text = spoken.Trim().TrimEnd('.', '!', '?', ',').ToLowerInvariant();
            if (text == "tak" || text == "zezwól" || text == "zgoda" || text == "ok" || text == "jasne" || text == "yes")
            {
                ResolveConfirm(true);
                return true;
            }
            if (text == "nie" || text == "odmów" || text == "anuluj" || text == "stop" || text == "no")
            {
                ResolveConfirm(false);
                return true;
            }
            return false;
        }
    }
}
