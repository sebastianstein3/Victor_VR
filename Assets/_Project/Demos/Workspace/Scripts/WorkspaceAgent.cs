using System;
using System.Collections.Generic;
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

        // ---- The windows, for the assistant: list them, close, resize, move ----

        class WindowRef
        {
            public string Name;      // what the user would call it
            public string Kind;
            public Transform Root;
            public bool Active;
            public Action Close;
            public Action Bigger, Smaller;   // null: the transform's scale is used
            public Action Front;
        }

        List<WindowRef> CollectWindows()
        {
            var list = new List<WindowRef>();
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                var captured = screen;
                string kind = screen.IsBrowser ? "przeglądarka" : screen.GetComponent<FilmPlayer>() != null ? "film" :
                    screen.GetComponent<PdfView>() != null ? "pdf" : screen.GetComponent<AssistantView>() != null ? "asystent" :
                    screen.GetComponent<MemoryView>() != null ? "pamięć" : screen.GetComponent<GalleryView>() != null ? "galeria" : "okno";
                list.Add(new WindowRef
                {
                    Name = string.IsNullOrEmpty(screen.Title) ? kind : screen.Title,
                    Kind = kind,
                    Root = screen.transform,
                    Active = screen == m_ActiveScreen,
                    Close = captured.Close,
                    Bigger = captured.Grow,
                    Smaller = captured.Shrink,
                    Front = () => SetActiveScreen(captured),
                });
            }
            if (m_PhoneScreen != null)
                list.Add(new WindowRef { Name = "Telefon", Kind = "telefon", Root = m_PhoneScreen.transform, Close = m_PhoneScreen.Close });
            foreach (var widget in m_Widgets)
            {
                if (widget != null)
                    list.Add(new WindowRef { Name = widget.Kind, Kind = "widżet", Root = widget.transform, Close = widget.Close });
            }
            if (m_CinemaPanel != null && m_CinemaMovable != null)
                list.Add(new WindowRef { Name = "Ekran kinowy", Kind = "kino", Root = m_CinemaPanel, Close = m_CinemaMovable.Close });
            if (m_ControlDock != null && m_ControlDock.gameObject.activeSelf)
                list.Add(new WindowRef { Name = "Dok", Kind = "panel", Root = m_ControlDock, Close = HideDock });
            if (m_Hologram != null)
                list.Add(new WindowRef { Name = "V.I.C.T.O.R.", Kind = "hologram", Root = m_Hologram.transform, Close = m_Hologram.Close });
            foreach (var model in m_Models)
            {
                if (model != null)
                    list.Add(new WindowRef { Name = "Model 3D", Kind = "model", Root = model.transform, Close = model.Close });
            }
            return list;
        }

        public string ListWindows()
        {
            var windows = CollectWindows();
            if (windows.Count == 0)
                return "Nie ma otwartych okien.";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < windows.Count; i++)
                text.Append(i + 1).Append(". ").Append(windows[i].Name).Append(" (").Append(windows[i].Kind).Append(windows[i].Active ? ", aktywne" : "").Append(")\n");
            return text.ToString().TrimEnd();
        }

        // Lower case without Polish diacritics, so "przegladarka" finds "przeglądarka"
        static string Plain(string text)
        {
            const string from = "ąćęłńóśźż", to = "acelnoszz";
            var plain = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").ToLowerInvariant())
            {
                int index = from.IndexOf(c);
                plain.Append(index >= 0 ? to[index] : c);
            }
            return plain.ToString();
        }

        public string WindowAction(string window, string action)
        {
            var windows = CollectWindows();
            WindowRef found = null;
            string query = Plain(window).Trim();
            if (int.TryParse(query, out int number) && number >= 1 && number <= windows.Count)
                found = windows[number - 1];
            if (found == null && (query == "aktywne" || query == "aktywny" || query == "to" || query == "ten"))
                found = windows.Find(w => w.Active) ?? (m_Core != null ? WindowNearestGaze(windows) : null);
            if (found == null)
                found = windows.Find(w => Plain(w.Name) == query);
            if (found == null && query.Length > 0)
                found = windows.Find(w => Plain(w.Name).Contains(query) || Plain(w.Kind).Contains(query) || query.Contains(Plain(w.Kind)));
            if (found == null)
                return "Nie ma takiego okna. Okna: " + ListWindows().Replace("\n", "; ");

            switch (action)
            {
                case "close":
                    found.Close();
                    return $"Zamknięte: {found.Name}.";
                case "bigger":
                    Resize(found, true);
                    return $"Powiększone: {found.Name}.";
                case "smaller":
                    Resize(found, false);
                    return $"Pomniejszone: {found.Name}.";
                case "front":
                    if (found.Front != null)
                        found.Front();
                    else
                        MoveWindow(found, 0f, 0f, 1f, true);
                    return $"{found.Name} jest teraz przed Tobą.";
                case "center":
                    MoveWindow(found, 0f, 0f, 1f, true);
                    return $"{found.Name} jest teraz przed Tobą.";
                case "left": MoveWindow(found, -22f, 0f, 1f, false); return $"{found.Name} przesunięte w lewo.";
                case "right": MoveWindow(found, 22f, 0f, 1f, false); return $"{found.Name} przesunięte w prawo.";
                case "up": MoveWindow(found, 0f, 14f, 1f, false); return $"{found.Name} przesunięte w górę.";
                case "down": MoveWindow(found, 0f, -14f, 1f, false); return $"{found.Name} przesunięte w dół.";
                case "closer": MoveWindow(found, 0f, 0f, 0.8f, false); return $"{found.Name} bliżej.";
                case "farther": MoveWindow(found, 0f, 0f, 1.25f, false); return $"{found.Name} dalej.";
                default:
                    return "Nieznana czynność.";
            }
        }

        WindowRef WindowNearestGaze(List<WindowRef> windows)
        {
            WindowRef best = null;
            float bestAngle = float.MaxValue;
            foreach (var w in windows)
            {
                float angle = Vector3.Angle(m_Core.Head.forward, w.Root.position - m_Core.Head.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = w;
                }
            }
            return best;
        }

        void Resize(WindowRef window, bool bigger)
        {
            if (window.Bigger != null && window.Smaller != null)
            {
                (bigger ? window.Bigger : window.Smaller)();
                return;
            }
            const float factor = 1.25f;
            // The scale of a panel is what makes 1 dp the right size at its distance: keep the change within 0.4x to 3x
            var t = window.Root;
            float next = Mathf.Clamp(t.localScale.x * (bigger ? factor : 1f / factor), t.localScale.x * 0.4f, t.localScale.x * 3f);
            t.localScale = t.localScale * (next / t.localScale.x);
        }

        // Turns the window about the head (yaw to the right, pitch up, degrees) and scales its distance, facing the head.
        // center: straight ahead at the same distance, at the height of the eyes.
        void MoveWindow(WindowRef window, float yaw, float pitch, float distanceFactor, bool center)
        {
            if (m_Core == null)
                return;
            var t = window.Root;
            if (m_RoomAnchors != null)
                m_RoomAnchors.Release(t);
            var head = m_Core.Head;
            Vector3 offset = t.position - head.position;
            float distance = Mathf.Clamp(offset.magnitude * distanceFactor, 0.5f, 5f);
            Vector3 direction;
            if (center)
            {
                Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            }
            else
            {
                direction = Quaternion.AngleAxis(yaw, Vector3.up) * offset.normalized;
                direction = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, direction).normalized) * direction;
            }
            Vector3 position = head.position + direction.normalized * distance;
            t.position = position;
            t.rotation = Quaternion.LookRotation(position - head.position, Vector3.up);
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
