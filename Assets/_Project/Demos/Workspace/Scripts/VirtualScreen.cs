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
        const float k_DefaultHeight = 360f;
        const float k_GrabBarHeight = 38f;

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
            var receiver = gameObject.AddComponent<RemoteDesktopReceiver>();
            receiver.Initialize(m_CanvasRect, host);
        }

        void BuildInterface(string title)
        {
            // 1. Header Bar (Top)
            float topY = (k_DefaultHeight / 2f) - 28f;
            m_TitleText = M3.Text("Title", m_CanvasRect, title, M3Theme.Type.TitleMedium,
                new Vector2(-90f, topY), new Vector2(280f, 36f), M3Theme.OnSurface, TextAlignmentOptions.Left);

            // Pin Button
            m_PinButton = M3.IconButton("PinBtn", m_CanvasRect, "check", M3Button.Variant.Outlined,
                new Vector2(180f, topY), TogglePin, 36f);

            // Close Button
            M3.IconButton("CloseBtn", m_CanvasRect, "close", M3Button.Variant.Text,
                new Vector2(226f, topY), CloseScreen, 36f);

            // Divider line
            M3.Surface("HeaderDivider", m_CanvasRect, new Vector2(0f, topY - 22f),
                new Vector2(k_DefaultWidth - 40f, 2f), M3Theme.OutlineVariant, 0f);

            // 2. Document Text Area (Center)
            m_BodyText = M3.Text("Content", m_CanvasRect, m_Content, M3Theme.Type.BodyLarge,
                new Vector2(0f, 10f), new Vector2(k_DefaultWidth - 60f, 200f), M3Theme.OnSurface, TextAlignmentOptions.TopLeft);
            // Page overflow, so a long text shows its last page (where the cursor is) instead of
            // cutting the newest characters off with an ellipsis.
            m_BodyText.overflowMode = TextOverflowModes.Page;

            // 3. Footer Stats (Above Grab Bar)
            float footerY = -(k_DefaultHeight / 2f) + 64f;
            m_FooterText = M3.Text("Footer", m_CanvasRect, "Znaki: 0 | Słowa: 0", M3Theme.Type.BodyMedium,
                new Vector2(-120f, footerY), new Vector2(220f, 24f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Left);

            // Quick Clear Button
            M3.Button("ClearBtn", m_CanvasRect, "Wyczyść", M3Button.Variant.Text,
                new Vector2(180f, footerY), Clear, null, 80f);

            // 4. Bottom Grab Bar (Grab to drag anywhere in the room)
            float grabY = -(k_DefaultHeight / 2f) + 24f;
            m_GrabButton = M3.Button("GrabBar", m_CanvasRect, "Chwyć, aby przenieść ekran", M3Button.Variant.Tonal,
                new Vector2(0f, grabY), OnGrabClicked, "open_with", k_DefaultWidth - 60f);
        }

        public void AppendChar(char c)
        {
            m_Content += c;
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        public void AppendText(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                m_Content += str;
                UpdateContentDisplay();
                Focused?.Invoke(this);
            }
        }

        public void Backspace()
        {
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
            m_Content += "\n";
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        void UpdateContentDisplay()
        {
            if (m_BodyText != null)
            {
                m_BodyText.text = m_Content + "<color=#80c5ff>|</color>"; // Cursor visual
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
                // Smoothly follows user gaze at drag distance
                Vector3 targetPos = m_Head.position + m_Head.forward * m_DragDistance;
                Quaternion targetRot = Quaternion.LookRotation(targetPos - m_Head.position, Vector3.up);

                transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * 10f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * 10f);
            }
        }
    }
}
