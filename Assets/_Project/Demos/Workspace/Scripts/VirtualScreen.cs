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
        public string Title { get; private set; }
        public float Size => m_Size;

        // For restoring a saved workspace
        public void SetText(string text)
        {
            m_Content = text ?? "";
            UpdateContentDisplay();
        }

        public void SetSize(float size) => Resize(size / m_Size);

        public void SetPinned(bool pinned)
        {
            if (pinned != m_Pinned)
                TogglePin();
        }
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
            Title = title;

            // Configure 3D Spatial Audio for AirPods Pro 2 directly on the moving panel
            m_SpatialAudio = gameObject.AddComponent<AudioSource>();
            m_SpatialAudio.spatialBlend = 1.0f; // Full 3D spatialized
            m_SpatialAudio.spatialize = true;
            m_SpatialAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            m_SpatialAudio.minDistance = 0.8f;
            m_SpatialAudio.maxDistance = 10f;

            BuildInterface(title);
            AnchorToEdges();
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
            var half = LayoutSize / 2f;
            m_CursorPos.x = Mathf.Clamp(m_CursorPos.x, -half.x + 16f, half.x - 16f);
            m_CursorPos.y = Mathf.Clamp(m_CursorPos.y, -half.y + 16f, half.y - 16f);
            if (m_CursorRect != null)
            {
                m_CursorRect.anchoredPosition = m_CursorPos;
            }
        }

        // The computer's main screen (app null) or one app's window on it (RemoteDesktopReceiver)
        public string RemoteApp => m_Remote != null ? m_Remote.App : null;

        public void SetTitle(string title)
        {
            Title = title;
            if (m_TitleText != null)
                m_TitleText.text = "<noparse>" + title + "</noparse>";
        }

        // Hides or shows the notes text (the app list and the PC picture use the same area)
        public void ShowBody(bool show)
        {
            if (m_BodyText != null)
                m_BodyText.gameObject.SetActive(show);
        }

        public void EnableRemoteDesktop(string host = null, string app = null)
        {
            if (m_BodyText != null)
                m_BodyText.gameObject.SetActive(false);
            m_Remote = gameObject.AddComponent<RemoteDesktopReceiver>();
            m_Remote.Initialize(m_CanvasRect, host, app);
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

            // Window size: smaller / larger (footer, between the counter at x<-30 and Clear at x>135)
            M3.Button("SmallerBtn", m_CanvasRect, "-", M3Button.Variant.Text,
                new Vector2(20f, k_FooterY), () => Resize(1f / k_SizeStep), null, M3.k_ButtonHeight);
            M3.Button("LargerBtn", m_CanvasRect, "+", M3Button.Variant.Text,
                new Vector2(84f, k_FooterY), () => Resize(k_SizeStep), null, M3.k_ButtonHeight);

            // 4. Bottom Grab Bar (Grab to drag anywhere in the room)
            m_GrabButton = M3.Button("GrabBar", m_CanvasRect, "Chwyć, aby przenieść ekran", M3Button.Variant.Tonal,
                new Vector2(-40f, k_GrabY), OnGrabClicked, "open_with", k_DefaultWidth - 140f);

            // 5. Stretch handle, bottom-right corner: the corner then follows the gaze (StartStretching)
            m_StretchButton = M3.IconButton("StretchBtn", m_CanvasRect, "aspect_ratio", M3Button.Variant.Tonal,
                new Vector2(200f, k_GrabY), ToggleStretching, M3.k_ButtonHeight);
        }

        // The area the remote desktop picture fills, the same as the text area.
        // Window size, 0.6x to 2x of the 520 x 440 dp window (41 x 35 degrees -> up to 82 x 70). Above
        // ~1.1x a window reaches into the neighbouring slot (windows are 46 degrees apart): the user's
        // choice; drag it aside or close the neighbour, and "Wyśrodkuj" puts the layout back.
        const float k_SizeStep = 1.15f;
        const float k_MinSize = 0.6f;
        const float k_MaxSize = 2f;

        public void Grow() => Resize(k_SizeStep);
        public void Shrink() => Resize(1f / k_SizeStep);
        float m_Size = 1f;

        void Resize(float factor)
        {
            float size = Mathf.Clamp(m_Size * factor, k_MinSize, k_MaxSize);
            if (Mathf.Approximately(size, m_Size))
                return;
            // M3Canvas sets the scale that makes 1 unit 1 dp at the window's distance; size multiplies it.
            transform.localScale *= size / m_Size;
            m_Size = size;
            Focused?.Invoke(this);
        }

        // ---- Stretching: width and height on their own (the layout follows the edges) ----
        // The elements are anchored to the window's edges (AnchorToEdges): header to the top, footer and
        // grab bar to the bottom, the text area between them. So changing the window's size in dp lays it
        // out again by itself; the window stays centred where it is. The body's centre stays at
        // (0, k_BodyY) whatever the size, since its top and bottom margins are fixed.
        const float k_TopMargin = 80f;     // window top -> text area
        const float k_BottomMargin = 140f; // text area -> window bottom (footer row and grab bar)
        const float k_SideMargin = 30f;
        public static Vector2 MinLayoutSize => new(k_DefaultWidth, k_DefaultHeight);
        public static Vector2 MaxLayoutSize => new(1200f, 900f); // ~95 x 72 degrees at 1x

        M3Button m_StretchButton;
        bool m_IsStretching;
        public bool IsStretching => m_IsStretching;
        public event Action<VirtualScreen> Resized;

        public Vector2 LayoutSize => m_CanvasRect != null ? m_CanvasRect.sizeDelta : new Vector2(k_DefaultWidth, k_DefaultHeight);
        public Vector2 BodySizeNow => LayoutSize - new Vector2(2f * k_SideMargin, k_TopMargin + k_BottomMargin);

        public void SetLayoutSize(Vector2 size)
        {
            size = Vector2.Max(MinLayoutSize, Vector2.Min(MaxLayoutSize, size));
            if ((size - m_CanvasRect.sizeDelta).sqrMagnitude < 0.25f)
                return;
            m_CanvasRect.sizeDelta = size;
            MoveCursorDelta(Vector2.zero); // keep the cursor inside
            Resized?.Invoke(this);
        }

        public void StartStretching()
        {
            if (m_IsDragging)
                StopDragging();
            m_IsStretching = true;
            if (m_StretchButton != null)
                m_StretchButton.Selected = true;
            Focused?.Invoke(this);
        }

        public void StopStretching()
        {
            m_IsStretching = false;
            if (m_StretchButton != null)
                m_StretchButton.Selected = false;
        }

        void ToggleStretching()
        {
            if (m_IsStretching)
                StopStretching();
            else
                StartStretching();
        }

        // While stretching: the corner goes under the gaze (symmetric about the centre: the window stays put)
        void UpdateStretching()
        {
            var plane = new Plane(transform.forward, transform.position);
            var ray = new Ray(m_Head.position, m_Head.forward);
            if (!plane.Raycast(ray, out float distance))
                return;
            Vector2 local = m_CanvasRect.InverseTransformPoint(ray.GetPoint(distance)); // dp
            // The handle's centre is 60 dp in from the right edge and 44 dp up from the bottom
            var target = new Vector2(2f * (Mathf.Abs(local.x) + 60f), 2f * (Mathf.Abs(local.y) + 44f));
            SetLayoutSize(Vector2.Lerp(LayoutSize, target, Time.unscaledDeltaTime * 8f));
        }

        // Anchors the window's elements to its edges, so that SetLayoutSize lays them out (called once, at
        // the default size). Offsets are the default layout's, measured from the edge they follow.
        void AnchorToEdges()
        {
            AtTopLeftToRight(m_TitleText.rectTransform, 20f, 140f, 32f, 40f);
            Pin("PinBtn", new Vector2(1f, 1f), new Vector2(-98f, -32f));
            Pin("CloseBtn", new Vector2(1f, 1f), new Vector2(-34f, -32f));
            AtTopLeftToRight((RectTransform)m_CanvasRect.Find("HeaderDivider"), 20f, 20f, 72f, 2f);
            AttachToBody(m_BodyText.rectTransform);
            Pin("Footer", new Vector2(0f, 0f), new Vector2(130f, 112f));
            Pin("SmallerBtn", new Vector2(0.5f, 0f), new Vector2(20f, 112f));
            Pin("LargerBtn", new Vector2(0.5f, 0f), new Vector2(84f, 112f));
            Pin("ClearBtn", new Vector2(1f, 0f), new Vector2(-70f, 112f));
            Pin("GrabBar", new Vector2(0f, 0f), new Vector2(220f, 44f));
            Pin("StretchBtn", new Vector2(1f, 0f), new Vector2(-60f, 44f));
        }

        void Pin(string child, Vector2 anchor, Vector2 position)
        {
            var rect = m_CanvasRect.Find(child) as RectTransform;
            if (rect != null)
                PinTo(rect, anchor, position);
        }

        // ---- For what other components add to the window (browser, PC picture) ----

        // At a fixed offset from an edge/corner point of the window (anchor 0..1 on each axis)
        public static void PinTo(RectTransform rect, Vector2 anchor, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
        }

        // Fills the text area, whatever the window's size
        public static void AttachToBody(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(k_SideMargin, k_BottomMargin);
            rect.offsetMax = new Vector2(-k_SideMargin, -k_TopMargin);
        }

        // A line across the text area, its centre `up` dp above the area's bottom
        public static void AttachToBodyBottom(RectTransform rect, float up, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(k_SideMargin, k_BottomMargin + up - height / 2f);
            rect.offsetMax = new Vector2(-k_SideMargin, k_BottomMargin + up + height / 2f);
        }

        // In the footer row, x from the window's left (anchorX 0), centre (0.5) or right (1)
        public static void PinToFooter(RectTransform rect, float anchorX, float x) =>
            PinTo(rect, new Vector2(anchorX, 0f), new Vector2(x, 112f));

        static void AtTopLeftToRight(RectTransform rect, float left, float right, float fromTop, float height)
        {
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(left, -fromTop - height / 2f);
            rect.offsetMax = new Vector2(-right, -fromTop + height / 2f);
        }

        public static Vector2 BodyCenter => new(0f, k_BodyY);
        public static float FooterY => k_FooterY;

        // ---- Browser ----
        BrowserView m_Browser;
        public bool IsBrowser => m_Browser != null;
        public BrowserView Browser => m_Browser;

        public void EnableBrowser(string url = null, bool privateMode = false)
        {
            if (m_Browser != null)
                return;
            SetTitle(privateMode ? "Prywatna przeglądarka" : "Przeglądarka");
            m_Browser = gameObject.AddComponent<BrowserView>();
            m_Browser.Initialize(this, m_CanvasRect, m_Head, url, privateMode);
        }

        // The character counter and Clear (a browser or PC window uses the footer for its own buttons)
        public void ShowFooterStats(bool show)
        {
            if (m_FooterText != null)
                m_FooterText.gameObject.SetActive(show);
            var clear = m_CanvasRect.Find("ClearBtn");
            if (clear != null)
                clear.gameObject.SetActive(show);
        }
        public static Vector2 BodySize => new(k_DefaultWidth - 60f, k_BodyHeight);

        // ---- Pointer: the cursor (moved by the desk trackpad or a mouse) can click this window's buttons ----

        // Clicks the button under the cursor, if any; true when one was clicked. Also focuses the window.
        public bool ClickAtCursor()
        {
            Focused?.Invoke(this);
            if (m_Browser != null && CursorInsideBody())
                return m_Browser.ClickAt(m_CursorPos);
            if (m_Remote != null && CursorInsideBody())
                return m_Remote.Click(m_CursorPos);
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
            // On a PC window the secondary click is the PC's right click
            if (m_Remote != null && CursorInsideBody() && m_Remote.Click(m_CursorPos, right: true))
                return;
            TogglePin();
        }

        bool CursorInsideBody() =>
            Mathf.Abs(m_CursorPos.x - BodyCenter.x) <= BodySizeNow.x / 2f && Mathf.Abs(m_CursorPos.y - BodyCenter.y) <= BodySizeNow.y / 2f;

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
            if (m_Browser != null) { m_Browser.TypeText(c.ToString()); Focused?.Invoke(this); return; }
            if (m_Remote != null) { m_Remote.SendText(c.ToString()); Focused?.Invoke(this); return; }
            m_Content += c;
            UpdateContentDisplay();
            Focused?.Invoke(this);
        }

        public void AppendText(string str)
        {
            if (m_Browser != null) { m_Browser.TypeText(str); Focused?.Invoke(this); return; }
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
            if (m_Browser != null) { m_Browser.Backspace(); Focused?.Invoke(this); return; }
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
            if (m_Browser != null) { m_Browser.Enter(); Focused?.Invoke(this); return; }
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
            if (m_IsStretching)
                StopStretching();
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

        public void Close() => CloseScreen();

        void CloseScreen()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        void Update()
        {
            if (m_IsStretching && m_Head != null && m_CanvasRect != null)
                UpdateStretching();

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
