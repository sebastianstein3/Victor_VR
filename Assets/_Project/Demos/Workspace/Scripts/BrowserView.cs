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
    // Scrolling: swipe up/down with an open hand, or the mouse wheel.
    public class BrowserView : MonoBehaviour
    {
        public const string k_HomePage = "https://duckduckgo.com/";
        const int k_TextureWidth = 920;   // 2 px per dp of the window's text area (460 x 220 dp)
        const int k_TextureHeight = 440;
        const float k_FrameInterval = 0.1f; // ~10 snapshots a second

        VirtualScreen m_Screen;
        Transform m_Head;
        RectTransform m_PageRect;
        RawImage m_Page;
        Texture2D m_Texture;
        byte[] m_Buffer;
        GCHandle m_BufferHandle;
        TextMeshProUGUI m_Status;
        M3Button m_AddressBtn;
        int m_Id;
        float m_NextFrame;
        bool m_AddressMode;
        string m_Address = "";
        string m_Url = "";
        string m_Title = "";

        public string Url => m_Url;

        static readonly Dictionary<int, BrowserView> s_Views = new();

        public void Initialize(VirtualScreen screen, RectTransform canvas, Transform head, string url)
        {
            m_Screen = screen;
            m_Head = head;
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);

            // The page, filling the window's text area; it takes the gaze so a pinch on it is a click
            var pageObject = new GameObject("BrowserPage", typeof(RectTransform));
            pageObject.transform.SetParent(canvas, false);
            m_PageRect = (RectTransform)pageObject.transform;
            m_PageRect.anchoredPosition = VirtualScreen.BodyCenter;
            m_PageRect.sizeDelta = VirtualScreen.BodySize;
            m_Page = pageObject.AddComponent<RawImage>();
            m_Page.color = Color.white;
            pageObject.transform.SetAsFirstSibling();
            pageObject.AddComponent<PageSurface>().Owner = this;

            m_Status = M3.Text("BrowserStatus", canvas, "", M3Theme.Type.BodyMedium,
                VirtualScreen.BodyCenter + new Vector2(0f, -VirtualScreen.BodySize.y / 2f + 14f),
                new Vector2(VirtualScreen.BodySize.x, 20f), new Color(0.15f, 0.15f, 0.2f), TextAlignmentOptions.Center);

            // Footer: back, forward, reload (left), the window's own - / + (middle), address (right)
            float y = VirtualScreen.FooterY;
            M3.IconButton("BrowserBack", canvas, "chevron_left", M3Button.Variant.Text, new Vector2(-200f, y), Back, 48f);
            M3.IconButton("BrowserForward", canvas, "chevron_right", M3Button.Variant.Text, new Vector2(-144f, y), Forward, 48f);
            M3.IconButton("BrowserReload", canvas, "refresh", M3Button.Variant.Text, new Vector2(-88f, y), Reload, 48f);
            m_AddressBtn = M3.Button("BrowserAddress", canvas, "Adres", M3Button.Variant.Tonal, new Vector2(180f, y),
                ToggleAddressMode, null, 110f);
            WorkspaceSounds.Attach(screen);

#if UNITY_IOS && !UNITY_EDITOR
            EnsureReceiver();
            m_Id = CardboardXR_BrowserCreate(k_TextureWidth, k_TextureHeight);
            if (m_Id == 0)
            {
                SetStatus("Nie udało się uruchomić przeglądarki");
                return;
            }
            s_Views[m_Id] = this;
            m_Texture = new Texture2D(k_TextureWidth, k_TextureHeight, TextureFormat.RGBA32, false);
            m_Buffer = new byte[k_TextureWidth * k_TextureHeight * 4];
            m_BufferHandle = GCHandle.Alloc(m_Buffer, GCHandleType.Pinned);
            m_Page.texture = m_Texture;
            Navigate(string.IsNullOrEmpty(url) ? k_HomePage : url);
#else
            m_Page.color = new Color(0.9f, 0.9f, 0.92f);
            SetStatus("Przeglądarka działa tylko na iPhonie (iOS)");
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
            if (m_Id == 0 || Time.unscaledTime < m_NextFrame)
                return;
            m_NextFrame = Time.unscaledTime + k_FrameInterval;
            if (CardboardXR_BrowserCopyFrame(m_Id, m_BufferHandle.AddrOfPinnedObject(), m_Buffer.Length) == 1)
            {
                m_Texture.LoadRawTextureData(m_Buffer);
                m_Texture.Apply(false);
            }
#endif
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
        }

        void ApplyState(BrowserState state)
        {
            m_Url = state.url ?? "";
            m_Title = state.title ?? "";
            m_Screen.SetTitle(string.IsNullOrEmpty(m_Title) ? "Przeglądarka" : m_Title);
            if (!m_AddressMode)
                SetStatus(state.loading ? "Wczytywanie..." : "");
        }

        void ShowAddressOrState()
        {
            SetStatus(m_AddressMode ? "Adres: " + m_Address + "|" : "");
        }

        void SetStatus(string text)
        {
            if (m_Status != null)
                m_Status.text = "<noparse>" + text.Replace("</noparse>", "") + "</noparse>";
        }

        void OnDestroy()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
            {
                s_Views.Remove(m_Id);
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
        [DllImport("__Internal")] static extern int CardboardXR_BrowserCreate(int width, int height);
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
