using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // A web page in a VirtualScreen: Safari's engine (WKWebView) on the iPhone, rendered to a texture by
    // Plugins/iOS/CardboardXRBrowser.mm (see its security notes). iOS only; elsewhere it says so.
    //
    // Click: look at the page and pinch, or the desk trackpad / a mouse on the window's cursor.
    // Typing (desk keyboard, Bluetooth keyboard, dictation) goes into the page's focused field, or into
    // the address while "Adres" is on (Enter opens it; words without a dot search DuckDuckGo).
    // Scrolling: swipe up/down with an open hand, or the mouse wheel. The window's - / + change the
    // window's size; "Aa" steps the page's text size (like Safari's "Aa"). Under the page: the real host, and whether the page is fully encrypted.
    // A private window keeps no cookies, cache or history after it closes. A page the user isn't
    // looking at refreshes less often (like background tabs), to save battery and heat.
    public class BrowserView : MonoBehaviour
    {
        public const string k_HomePage = "https://duckduckgo.com/";
        const int k_TextureWidth = 920;   // 2 px per dp of the window's text area (460 x 220 dp by default)
        const int k_TextureHeight = 440;
        const float k_ResizeDelay = 0.4f; // after the window stops changing size, the page is laid out again
        int m_TextureWidth = k_TextureWidth;
        int m_TextureHeight = k_TextureHeight;
        bool m_ResizePending;
        float m_ResizeAt;
        const float k_FrameInterval = 0.05f;     // ~20 fps while looked at
        const float k_IdleFrameInterval = 0.5f;  // 2 fps when not
        const float k_LookedAtDegrees = 35f;

        VirtualScreen m_Screen;
        Transform m_Head;
        RectTransform m_PageRect;
        RawImage m_Page;
        Texture2D m_Texture;
        byte[] m_Buffer;
        GCHandle m_BufferHandle;
        float m_StatusUntil; // a message is in the header until then (SetStatus)
        const float k_StatusSeconds = 2.5f;
        M3Button m_AddressBtn;
        int m_Id;
        float m_NextFrame;
        bool m_AddressMode;
        string m_Address = "";
        string m_Url = "";
        string m_Title = "";
        bool m_Secure;
        bool m_Private;
        bool m_DirectTexture; // frames written straight into the Metal texture (no copy through C#)
        float m_Zoom = 1f;
        RectTransform m_Progress;

        public string Url => m_Url;
        public bool IsPrivate => m_Private;

        static readonly Dictionary<int, BrowserView> s_Views = new();

        public void Initialize(VirtualScreen screen, RectTransform canvas, Transform head, string url, bool privateMode = false)
        {
            m_Private = privateMode;
            m_Screen = screen;
            m_Head = head;
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);

            // The page, filling the window's text area; it takes the gaze so a pinch on it is a click
            var pageObject = new GameObject("BrowserPage", typeof(RectTransform));
            pageObject.transform.SetParent(canvas, false);
            m_PageRect = (RectTransform)pageObject.transform;
            VirtualScreen.AttachToBody(m_PageRect);
            m_Screen.Resized += OnWindowResized;
            m_Page = pageObject.AddComponent<RawImage>();
            m_Page.color = Color.white;
            pageObject.transform.SetAsFirstSibling();
            pageObject.AddComponent<PageSurface>().Owner = this;

            // Loading progress: a thin bar along the top of the page
            m_Progress = M3.Surface("BrowserProgress", canvas,
                VirtualScreen.BodyCenter + new Vector2(0f, VirtualScreen.BodySize.y / 2f - 2f), new Vector2(0f, 4f),
                M3Theme.Primary, 0f).rectTransform;
            VirtualScreen.PinTo(m_Progress, new Vector2(0f, 1f), new Vector2(30f, -82f));
            m_Progress.gameObject.SetActive(false);


            // Footer: back, forward, reload (left), the window's own - / + (middle), address (right)
            float y = VirtualScreen.FooterY;
            // back, forward, reload, Aa (text size) | the window's - / + (size) | Adres
            var back = M3.IconButton("BrowserBack", canvas, "chevron_left", M3Button.Variant.Text, new Vector2(-205f, y), Back, 48f);
            var forward = M3.IconButton("BrowserForward", canvas, "chevron_right", M3Button.Variant.Text, new Vector2(-153f, y), Forward, 48f);
            var reload = M3.IconButton("BrowserReload", canvas, "refresh", M3Button.Variant.Text, new Vector2(-101f, y), Reload, 48f);
            var textSize = M3.Button("BrowserTextSize", canvas, "Aa", M3Button.Variant.Text, new Vector2(-45f, y), CycleTextSize, null, 48f);
            // Footer positions from the window's left edge (it can be stretched)
            VirtualScreen.PinToFooter((RectTransform)back.transform, 0f, 55f);
            VirtualScreen.PinToFooter((RectTransform)forward.transform, 0f, 107f);
            VirtualScreen.PinToFooter((RectTransform)reload.transform, 0f, 159f);
            VirtualScreen.PinToFooter((RectTransform)textSize.transform, 0f, 215f);
            m_AddressBtn = M3.Button("BrowserAddress", canvas, "Adres", M3Button.Variant.Tonal, new Vector2(180f, y),
                ToggleAddressMode, null, 110f);
            VirtualScreen.PinToFooter((RectTransform)m_AddressBtn.transform, 1f, -80f);
            WorkspaceSounds.Attach(screen);

#if UNITY_IOS && !UNITY_EDITOR
            EnsureReceiver();
            // The page as big as the window's text area, 2 px per dp
            var area = m_Screen.BodySizeNow;
            m_TextureWidth = Mathf.Clamp(Mathf.RoundToInt(area.x * 2f), 64, 2048);
            m_TextureHeight = Mathf.Clamp(Mathf.RoundToInt(area.y * 2f), 64, 2048);
            m_Id = CardboardXR_BrowserCreate(m_TextureWidth, m_TextureHeight, m_Private ? 1 : 0);
            if (m_Id == 0)
            {
                SetStatus("Nie udało się uruchomić przeglądarki");
                return;
            }
            s_Views[m_Id] = this;
            m_Texture = new Texture2D(m_TextureWidth, m_TextureHeight, TextureFormat.RGBA32, false);
            m_Texture.Apply(false); // makes the GPU texture
            m_Page.texture = m_Texture;
            // Metal: the native side writes frames into the texture itself. Otherwise through a buffer.
            m_DirectTexture = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal;
            if (m_DirectTexture)
            {
                CardboardXR_BrowserSetTexture(m_Id, m_Texture.GetNativeTexturePtr());
            }
            else
            {
                m_Buffer = new byte[m_TextureWidth * m_TextureHeight * 4];
                m_BufferHandle = GCHandle.Alloc(m_Buffer, GCHandleType.Pinned);
            }
            if (m_Private)
                m_Screen.SetTitle("Prywatna przeglądarka");
            Navigate(string.IsNullOrEmpty(url) ? k_HomePage : url);
#else
            m_Page.color = new Color(0.9f, 0.9f, 0.92f);
            SetStatus("Przeglądarka działa tylko na iPhonie (iOS)", sticky: true);
#endif
        }

        // ---- Navigation ----

        // A URL, a domain ("wp.pl") or words to search for
        public void Navigate(string text)
        {
            string url = ToUrl(text);
            if (url == null)
                return;
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0 && CardboardXR_BrowserLoad(m_Id, url) == 0)
                SetStatus("Tego adresu nie można otworzyć (tylko http/https)");
#endif
        }

        public static string ToUrl(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return text;
            // Other schemes (file:, javascript:, tel: ...) are never opened: treat them as a search
            bool looksLikeDomain = text.IndexOf(' ') < 0 && text.IndexOf('.') > 0 && text.IndexOf(':') < 0;
            return looksLikeDomain ? "https://" + text : "https://duckduckgo.com/?q=" + Uri.EscapeDataString(text);
        }

        void Back() { if (m_Id != 0) CardboardXR_Call(Op.Back); }
        void Forward() { if (m_Id != 0) CardboardXR_Call(Op.Forward); }
        void Reload() { if (m_Id != 0) CardboardXR_Call(Op.Reload); }
        public void GoBack() => Back();
        public void DoReload() => Reload();

        void ToggleAddressMode()
        {
            m_AddressMode = !m_AddressMode;
            m_Address = m_AddressMode ? "" : m_Address;
            if (m_AddressBtn != null)
                m_AddressBtn.Selected = m_AddressMode;
            ShowAddressOrState();
        }

        // ---- Input from the workspace ----

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (m_AddressMode)
            {
                m_Address += text;
                ShowAddressOrState();
                return;
            }
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserInsertText(m_Id, text);
#endif
        }

        public void Backspace()
        {
            if (m_AddressMode)
            {
                if (m_Address.Length > 0)
                    m_Address = m_Address.Substring(0, m_Address.Length - 1);
                ShowAddressOrState();
                return;
            }
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserKey(m_Id, "backspace");
#endif
        }

        public void Enter()
        {
            if (m_AddressMode)
            {
                string address = m_Address;
                ToggleAddressMode();
                Navigate(address);
                return;
            }
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserKey(m_Id, "enter");
#endif
        }

        public void Scroll(float pages)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserScroll(m_Id, pages);
#endif
        }

        // A click at a point of the window in dp (the window's cursor); false outside the page
        public bool ClickAt(Vector2 windowDp)
        {
            var center = m_PageRect.anchoredPosition;
            var size = m_PageRect.sizeDelta;
            float u = (windowDp.x - (center.x - size.x / 2f)) / size.x;
            float v = 1f - (windowDp.y - (center.y - size.y / 2f)) / size.y;
            return Tap(u, v);
        }

        bool Tap(float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserTap(m_Id, u, v);
#endif
            return true;
        }

        // A gaze-and-pinch click on the page: where the head's ray meets the page
        void TapAtGaze()
        {
            if (m_Head == null)
                return;
            var plane = new Plane(m_PageRect.forward, m_PageRect.position);
            var ray = new Ray(m_Head.position, m_Head.forward);
            if (!plane.Raycast(ray, out float distance))
                return;
            Vector2 local = m_PageRect.InverseTransformPoint(ray.GetPoint(distance));
            var size = m_PageRect.rect.size;
            Tap(local.x / size.x + 0.5f, 0.5f - local.y / size.y);
        }

        // ---- Frames and state ----

        void Update()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id == 0)
                return;
            if (m_StatusUntil > 0f && Time.unscaledTime > m_StatusUntil)
                ShowHost();
            UpdateProgress(CardboardXR_BrowserProgress(m_Id));
            if (m_ResizePending && Time.unscaledTime >= m_ResizeAt && !m_Screen.IsStretching)
                ApplyTextureSize();
            if (Time.unscaledTime < m_NextFrame)
                return;
            m_NextFrame = Time.unscaledTime + (IsLookedAt() ? k_FrameInterval : k_IdleFrameInterval);
            if (m_DirectTexture)
            {
                CardboardXR_BrowserUpdateTexture(m_Id);
            }
            else if (CardboardXR_BrowserCopyFrame(m_Id, m_BufferHandle.AddrOfPinnedObject(), m_Buffer.Length) == 1)
            {
                m_Texture.LoadRawTextureData(m_Buffer);
                m_Texture.Apply(false);
            }
#endif
        }

        void OnWindowResized(VirtualScreen screen)
        {
            m_ResizePending = true;
            m_ResizeAt = Time.unscaledTime + k_ResizeDelay;
        }

        // A new page size (2 px per dp of the text area): the web view is laid out again at it, like a
        // desktop browser window being resized, instead of the old picture being stretched
        void ApplyTextureSize()
        {
            m_ResizePending = false;
#if UNITY_IOS && !UNITY_EDITOR
            var area = m_Screen.BodySizeNow;
            int width = Mathf.Clamp(Mathf.RoundToInt(area.x * 2f), 64, 2048);
            int height = Mathf.Clamp(Mathf.RoundToInt(area.y * 2f), 64, 2048);
            if (m_Id == 0 || (width == m_TextureWidth && height == m_TextureHeight))
                return;
            CardboardXR_BrowserSetTexture(m_Id, IntPtr.Zero);
            CardboardXR_BrowserResize(m_Id, width, height);
            var old = m_Texture;
            m_TextureWidth = width;
            m_TextureHeight = height;
            m_Texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            m_Texture.Apply(false);
            m_Page.texture = m_Texture;
            if (m_DirectTexture)
            {
                CardboardXR_BrowserSetTexture(m_Id, m_Texture.GetNativeTexturePtr());
            }
            else
            {
                if (m_BufferHandle.IsAllocated)
                    m_BufferHandle.Free();
                m_Buffer = new byte[width * height * 4];
                m_BufferHandle = GCHandle.Alloc(m_Buffer, GCHandleType.Pinned);
            }
            if (old != null)
                Destroy(old);
#endif
        }

        bool IsLookedAt()
        {
            if (m_Head == null)
                return true;
            var toPage = m_PageRect.position - m_Head.position;
            return Vector3.Angle(m_Head.forward, toPage) < k_LookedAtDegrees;
        }

        void UpdateProgress(float progress)
        {
            bool show = progress >= 0f && progress < 1f;
            if (m_Progress.gameObject.activeSelf != show)
                m_Progress.gameObject.SetActive(show);
            if (show)
            {
                // From the text area's top-left corner (30 dp in, 80 dp down), as wide as the progress
                float width = m_PageRect.rect.width * Mathf.Clamp01(progress);
                m_Progress.sizeDelta = new Vector2(width, 4f);
                m_Progress.anchoredPosition = new Vector2(30f + width / 2f, -82f);
            }
        }

        // Desktop or mobile version of sites, like Safari's "Request Desktop Website":
        // 0 auto (desktop for sites that need it, e.g. Messenger, and for windows 900+ dp wide), 1 desktop, 2 mobile
        public void SetDesktopMode(int mode)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserSetDesktopMode(m_Id, mode);
#endif
            SetStatus(mode == 1 ? "Wersja na komputer" : mode == 2 ? "Wersja mobilna" : "Wersja automatyczna");
        }

        // "Aa": 100 -> 125 -> 150 -> 175 -> 200 -> 75 -> 100 %
        static readonly float[] k_TextSizes = { 1f, 1.25f, 1.5f, 1.75f, 2f, 0.75f };

        void CycleTextSize()
        {
            int next = 0;
            for (int i = 0; i < k_TextSizes.Length; i++)
            {
                if (Mathf.Approximately(k_TextSizes[i], m_Zoom))
                    next = (i + 1) % k_TextSizes.Length;
            }
            ZoomBy(k_TextSizes[next] / m_Zoom);
        }

        // Text zoom, like Safari's "Aa" (also the voice commands "większy/mniejszy tekst")
        public void ZoomBy(float factor)
        {
            m_Zoom = Mathf.Clamp(m_Zoom * factor, 0.5f, 3f);
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_BrowserSetZoom(m_Id, m_Zoom);
#endif
            SetStatus($"Tekst {m_Zoom * 100f:F0}%");
        }

        [Serializable]
        class BrowserState
        {
            public int id;
            public string url;
            public string title;
            public bool loading;
            public bool canGoBack;
            public bool canGoForward;
            public bool secure;
        }

        void ApplyState(BrowserState state)
        {
            m_Url = state.url ?? "";
            m_Title = state.title ?? "";
            m_Secure = state.secure;
            if (!m_AddressMode)
                ShowHost();
        }

        void ShowAddressOrState()
        {
            if (m_AddressMode)
                SetStatus("Adres: " + m_Address + "|", sticky: true);
            else
                ShowHost();
        }

        // The header shows the real host first, as current browsers do in their address bar (a page's
        // title can say anything): against lookalike pages. Green "Szyfrowane" when the page and
        // everything on it came over https, red otherwise. Nothing is drawn over the page itself.
        void ShowHost()
        {
            m_StatusUntil = 0f;
            string name = string.IsNullOrEmpty(m_Title) ? "Przeglądarka" : m_Title;
            if (m_Private)
                name = "[Prywatna] " + name;
            string host = Uri.TryCreate(m_Url, UriKind.Absolute, out var uri) ? uri.Host : "";
            if (string.IsNullOrEmpty(host))
            {
                m_Screen.SetTitle(name);
                return;
            }
            string mark = m_Secure ? "<color=#5AC878>Szyfrowane</color> · " : "<color=#E65A46>NIESZYFROWANE</color> · ";
            m_Screen.SetTitle(host + " — " + name, mark);
        }

        // A short message in the header (loading, text size, address), then the host again
        void SetStatus(string text, bool sticky = false)
        {
            if (string.IsNullOrEmpty(text))
            {
                ShowHost();
                return;
            }
            m_Screen.SetTitle(text);
            m_StatusUntil = sticky ? float.PositiveInfinity : Time.unscaledTime + k_StatusSeconds;
        }

        void OnDestroy()
        {
            if (m_Screen != null)
                m_Screen.Resized -= OnWindowResized;
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
            {
                s_Views.Remove(m_Id);
                // Stop writing into the texture before it is freed
                CardboardXR_BrowserSetTexture(m_Id, IntPtr.Zero);
                CardboardXR_BrowserDestroy(m_Id);
            }
#endif
            if (m_BufferHandle.IsAllocated)
                m_BufferHandle.Free();
            if (m_Texture != null)
                Destroy(m_Texture);
        }

        // ---- Native ----

        enum Op { Back, Forward, Reload }

        void CardboardXR_Call(Op op)
        {
#if UNITY_IOS && !UNITY_EDITOR
            switch (op)
            {
                case Op.Back: CardboardXR_BrowserGoBack(m_Id); break;
                case Op.Forward: CardboardXR_BrowserGoForward(m_Id); break;
                case Op.Reload: CardboardXR_BrowserReload(m_Id); break;
            }
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int CardboardXR_BrowserCreate(int width, int height, int privateMode);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserSetTexture(int id, IntPtr texture);
        [DllImport("__Internal")] static extern int CardboardXR_BrowserUpdateTexture(int id);
        [DllImport("__Internal")] static extern float CardboardXR_BrowserProgress(int id);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserSetZoom(int id, float zoom);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserResize(int id, int width, int height);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserSetDesktopMode(int id, int mode);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserDestroy(int id);
        [DllImport("__Internal")] static extern int CardboardXR_BrowserLoad(int id, string url);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserGoBack(int id);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserGoForward(int id);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserReload(int id);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserTap(int id, float u, float v);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserScroll(int id, float pages);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserInsertText(int id, string text);
        [DllImport("__Internal")] static extern void CardboardXR_BrowserKey(int id, string key);
        [DllImport("__Internal")] static extern int CardboardXR_BrowserCopyFrame(int id, IntPtr destination, int capacity);

        // The native side reports page changes to the GameObject named "BrowserNative"
        static void EnsureReceiver()
        {
            if (GameObject.Find("BrowserNative") != null)
                return;
            var go = new GameObject("BrowserNative");
            DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
        }

        [Preserve]
        class Receiver : MonoBehaviour
        {
            [Preserve]
            void OnBrowserState(string json)
            {
                var state = JsonUtility.FromJson<BrowserState>(json);
                if (state != null && s_Views.TryGetValue(state.id, out var view) && view != null)
                    view.ApplyState(state);
            }
        }
#endif

        // On the page image: a gaze click (look + pinch) becomes a tap where the gaze meets the page
        class PageSurface : MonoBehaviour, IPointerClickHandler
        {
            public BrowserView Owner;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (Owner != null)
                    Owner.TapAtGaze();
            }
        }
    }
}
