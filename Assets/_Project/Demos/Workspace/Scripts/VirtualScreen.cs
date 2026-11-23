using System;
using CardboardXR.Interaction;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // A movable, floating spatial monitor window in the user's room.
    // Can be grabbed and repositioned in 3D space, pinned, or edited via the DeskKeyboard.
    public class VirtualScreen : MonoBehaviour
    {
        const float k_DefaultWidth = 520f;
        const float k_DefaultHeight = 440f;

        RectTransform m_CanvasRect;
        TextMeshProUGUI m_TitleText;
        TextMeshProUGUI m_BodyText;
        TextMeshProUGUI m_FooterText;
        M3Button m_GrabButton;
        M3Button m_PinButton;
        RectTransform m_CursorRect;
        Vector2 m_CursorPos = Vector2.zero;
        AudioSource m_SpatialAudio;

        string m_Content = "Witaj w przestrzennym notatniku!\nPisz na wirtualnej klawiaturze biurkowej.";
        bool m_Pinned;
        bool m_IsDragging;
        float m_DragDistance = 1.35f;
        Vector3 m_DragOffset = new(0f, 0f, 1.35f);
        RemoteDesktopReceiver m_Remote;
        Transform m_Head;

        public event Action<VirtualScreen> Closed;
        public event Action<VirtualScreen> Focused;

        public bool IsDragging => m_IsDragging;
        public bool Pinned => m_Pinned;
        public string Text => m_Content;
        public RectTransform Rect => m_CanvasRect;

        public static VirtualScreen Create(Transform parent, Transform head, string title, Vector3 startPos, Quaternion startRot, float distance = 1.35f)
        {
            var panel = WorldUi.Panel($"Screen_{title}", parent, new Vector2(k_DefaultWidth, k_DefaultHeight), distance,
                M3Theme.SurfaceContainer, 28f);
            panel.SetPositionAndRotation(startPos, startRot);

            var screen = panel.gameObject.AddComponent<VirtualScreen>();
            screen.Initialize(panel, head, title, distance);
            return screen;
        }

        public void Initialize(RectTransform canvasRect, Transform head, string title, float distance = 1.35f)
        {
            m_CanvasRect = canvasRect;
            m_Head = head;
            m_DragDistance = distance;

            // Configure 3D Spatial Audio for AirPods Pro 2 directly on the moving panel
            m_SpatialAudio = gameObject.AddComponent<AudioSource>();
            m_SpatialAudio.spatialBlend = 1.0f; // Full 3D spatialized
            m_SpatialAudio.spatialize = true;
            m_SpatialAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            m_SpatialAudio.minDistance = 0.8f;
            m_SpatialAudio.maxDistance = 10f;

            BuildInterface(title);
            WorkspaceSounds.Attach(this);
            BuildVirtualCursor();
            UpdateContentDisplay();
        }

        void BuildVirtualCursor()
        {
            var cursorObj = new GameObject("VirtualCursor", typeof(RectTransform));
            cursorObj.transform.SetParent(m_CanvasRect, false);
            m_CursorRect = (RectTransform)cursorObj.transform;
            m_CursorRect.sizeDelta = new Vector2(12f, 12f);
            m_CursorRect.anchoredPosition = Vector2.zero;

            var img = cursorObj.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.2f, 0.75f, 1f, 0.9f);
            img.raycastTarget = false;
            M3Shapes.Apply(img, 6f); // Circular cursor dot
        }

        public void MoveCursorDelta(Vector2 delta)
        {
            m_CursorPos += delta;
            m_CursorPos.x = Mathf.Clamp(m_CursorPos.x, -k_DefaultWidth / 2f + 16f, k_DefaultWidth / 2f - 16f);
            m_CursorPos.y = Mathf.Clamp(m_CursorPos.y, -k_DefaultHeight / 2f + 16f, k_DefaultHeight / 2f - 16f);
            if (m_CursorRect != null)
            {
                m_CursorRect.anchoredPosition = m_CursorPos;
            }
        }

        public void EnableRemoteDesktop(string host = "192.168.1.100")
        {
            if (m_BodyText != null)
                m_BodyText.gameObject.SetActive(false);
            m_Remote = gameObject.AddComponent<RemoteDesktopReceiver>();
            m_Remote.Initialize(m_CanvasRect, host);
        }

        // Layout in dp, from the top of a 520 x 440 panel down. Every button is a full 56 dp target
        // (M3.k_ButtonHeight), and the rows are spaced so that none overlaps another:
        //   header row   y 160..216   title, pin, close
        //   divider      y 148
        //   body         y -80..140   text (or the remote desktop picture)
        //   footer row   y -136..-80  character count, Clear
        //   grab bar     y -204..-148 drag handle
        const float k_HeaderY = 188f;
        const float k_BodyY = 30f;
        const float k_BodyHeight = 220f;
        const float k_FooterY = -108f;
        const float k_GrabY = -176f;

        void BuildInterface(string title)
        {
            // 1. Header Bar (Top)
            m_TitleText = M3.Text("Title", m_CanvasRect, title, M3Theme.Type.TitleMedium,
                new Vector2(-110f, k_HeaderY), new Vector2(260f, 40f), M3Theme.OnSurface, TextAlignmentOptions.Left);

            // Pin Button
            m_PinButton = M3.IconButton("PinBtn", m_CanvasRect, "check", M3Button.Variant.Outlined,
                new Vector2(162f, k_HeaderY), TogglePin, M3.k_ButtonHeight);

            // Close Button
            M3.IconButton("CloseBtn", m_CanvasRect, "close", M3Button.Variant.Text,
                new Vector2(226f, k_HeaderY), CloseScreen, M3.k_ButtonHeight);

            // Divider line
            M3.Surface("HeaderDivider", m_CanvasRect, new Vector2(0f, 148f),
                new Vector2(k_DefaultWidth - 40f, 2f), M3Theme.OutlineVariant, 0f);

            // 2. Document Text Area (Center)
            m_BodyText = M3.Text("Content", m_CanvasRect, m_Content, M3Theme.Type.BodyLarge,
                new Vector2(0f, k_BodyY), new Vector2(k_DefaultWidth - 60f, k_BodyHeight), M3Theme.OnSurface, TextAlignmentOptions.TopLeft);
            // Page overflow, so a long text shows its last page (where the cursor is) instead of
            // cutting the newest characters off with an ellipsis.
            m_BodyText.overflowMode = TextOverflowModes.Page;

            // 3. Footer Stats and Clear, between the body and the grab bar
            m_FooterText = M3.Text("Footer", m_CanvasRect, "Znaki: 0 | Słowa: 0", M3Theme.Type.BodyMedium,
                new Vector2(-130f, k_FooterY), new Vector2(200f, 24f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Left);

            M3.Button("ClearBtn", m_CanvasRect, "Wyczyść", M3Button.Variant.Text,
                new Vector2(190f, k_FooterY), Clear, null, 110f);

            // 4. Bottom Grab Bar (Grab to drag anywhere in the room)
            m_GrabButton = M3.Button("GrabBar", m_CanvasRect, "Chwyć, aby przenieść ekran", M3Button.Variant.Tonal,
                new Vector2(0f, k_GrabY), OnGrabClicked, "open_with", k_DefaultWidth - 60f);
        }

        // The area the remote desktop picture fills, the same as the text area.
        public static Vector2 BodyCenter => new(0f, k_BodyY);
        public static Vector2 BodySize => new(k_DefaultWidth - 60f, k_BodyHeight);

        // ---- Pointer: the cursor (moved by the desk trackpad or a mouse) can click this window's buttons ----

        // Clicks the button under the cursor, if any; true when one was clicked. Also focuses the window.
        public bool ClickAtCursor()
        {
            Focused?.Invoke(this);
            if (m_Remote != null && CursorInsideBody())
            {
                m_Remote.SendInputEvent("click", m_CursorPos.x, m_CursorPos.y);
                return true;
            }
            foreach (var button in GetComponentsInChildren<M3Button>(false))
            {
                if (!button.isActiveAndEnabled || !button.Interactable)
                    continue;
                if (!CursorIsOver((RectTransform)button.transform))
                    continue;
                WorkspaceSounds.Muted = true;
                try { button.Button.onClick.Invoke(); }
                finally { WorkspaceSounds.Muted = false; }
                return true;
            }
            return false;
        }

        // The secondary click (right mouse button, two-finger tap): pins or unpins the window, so it
        // can be left in place and not carried along by the next drag.
        public void ContextClickAtCursor()
        {
            Focused?.Invoke(this);
            TogglePin();
        }

        bool CursorInsideBody() =>
            Mathf.Abs(m_CursorPos.x - BodyCenter.x) <= BodySize.x / 2f && Mathf.Abs(m_CursorPos.y - BodyCenter.y) <= BodySize.y / 2f;

        bool CursorIsOver(RectTransform rect)
        {
            // The rect's corners in the window's own dp space, the space the cursor moves in.
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var local = m_CanvasRect.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }
            return m_CursorPos.x >= minX && m_CursorPos.x <= maxX && m_CursorPos.y >= minY && m_CursorPos.y <= maxY;
        }

        // A PC desktop window has no text area: typing and dictation go to the PC (the receiver).
        public bool IsRemoteDesktop => m_Remote != null;

        public void AppendChar(char c)
        {
            if (m_Remote != null) { m_Remote.SendText(c.ToString()); Focused?.Invoke(this); return; }
            m_Content += c;
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        public void AppendText(string str)
        {
            if (m_Remote != null) { m_Remote.SendText(str); Focused?.Invoke(this); return; }
            if (!string.IsNullOrEmpty(str))
            {
                m_Content += str;
                UpdateContentDisplay();
                Focused?.Invoke(this);
            }
        }

        public void Backspace()
        {
            if (m_Remote != null) { m_Remote.SendKey("Backspace"); Focused?.Invoke(this); return; }
            if (m_Content.Length > 0)
            {
                m_Content = m_Content.Substring(0, m_Content.Length - 1);
                UpdateContentDisplay();
            }
            Focused?.Invoke(this);
        }

        public void Clear()
        {
            m_Content = "";
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        public void AppendNewline()
        {
            if (m_Remote != null) { m_Remote.SendKey("Enter"); Focused?.Invoke(this); return; }
            m_Content += "\n";
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        void UpdateContentDisplay()
        {
            if (m_BodyText != null)
            {
                // <noparse>: a typed or dictated "<" must show as text, not start a TMP tag.
                m_BodyText.text = "<noparse>" + m_Content.Replace("</noparse>", "") + "</noparse><color=#80c5ff>|</color>";
                m_BodyText.ForceMeshUpdate();
                m_BodyText.pageToDisplay = Mathf.Max(1, m_BodyText.textInfo.pageCount);
            }

            if (m_FooterText != null)
            {
                int chars = m_Content.Length;
                int words = string.IsNullOrWhiteSpace(m_Content) ? 0 : m_Content.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                m_FooterText.text = $"Znaki: {chars} | Słowa: {words}";
            }
        }

        public void StartDragging()
        {
            if (m_Pinned)
                return;
            m_IsDragging = true;
            if (m_Head != null)
                m_DragOffset = Quaternion.Inverse(m_Head.rotation) * (transform.position - m_Head.position);
            if (m_GrabButton != null)
                m_GrabButton.Label.text = "Upuść w tym miejscu";
        }

        public void StopDragging()
        {
            m_IsDragging = false;
            if (m_GrabButton != null)
                m_GrabButton.Label.text = "Chwyć, aby przenieść ekran";
        }

        void OnGrabClicked()
        {
            if (m_IsDragging)
                StopDragging();
            else
                StartDragging();
        }

        void TogglePin()
        {
            m_Pinned = !m_Pinned;
            if (m_PinButton != null)
            {
                m_PinButton.Selected = m_Pinned;
            }
            if (m_Pinned && m_IsDragging)
                StopDragging();
        }

        void CloseScreen()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        void Update()
        {
            if (m_IsDragging && m_Head != null && m_CanvasRect != null)
            {
                // Carried where it was in the view when the drag began (not snapped to the view's centre),
                // so its grab button stays where the user is looking and can be pressed again to drop it.
                Vector3 targetPos = m_Head.position + m_Head.rotation * m_DragOffset;
                Quaternion targetRot = Quaternion.LookRotation(targetPos - m_Head.position, Vector3.up);

                transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * 10f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * 10f);
            }
        }
    }
}
