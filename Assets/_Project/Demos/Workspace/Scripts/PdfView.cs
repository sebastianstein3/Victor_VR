using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // A PDF in a VirtualScreen: pages rendered by PDFKit on the iPhone (Plugins/iOS/CardboardXRPdf.mm).
    // Files: the app's Documents/PDF folder (or Documents), copied in with Finder or the Files app.
    // Pages: the buttons under the page, an open-hand swipe up/down, or "następna/poprzednia strona".
    // Stretch the window (corner handle) for a bigger page; it is rendered again sharp at the new size.
    public class PdfView : MonoBehaviour
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "PDF");

        VirtualScreen m_Screen;
        RawImage m_Image;
        Texture2D m_Texture;
        byte[] m_Buffer;
        GCHandle m_Handle;
        string m_Path;
        string m_Name;
        int m_Id;
        int m_Page;
        int m_Pages;
        bool m_RenderPending;
        float m_RenderAt;

        public string FilePath => m_Path;
        public int Page => m_Page;

        // The PDFs to offer: Documents/PDF first, then any in Documents itself
        public static List<string> FindFiles()
        {
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var folder in new[] { Folder, Application.persistentDataPath })
                {
                    foreach (var file in Directory.GetFiles(folder))
                    {
                        if (string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                            files.Add(file);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Pdf] {e.Message}");
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public void Initialize(VirtualScreen screen, string path, int page = 0)
        {
            m_Screen = screen;
            m_Path = path;
            m_Name = Path.GetFileNameWithoutExtension(path);
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(620f, 760f))); // a page is tall

            var imageObject = new GameObject("PdfPage", typeof(RectTransform));
            imageObject.transform.SetParent(screen.Rect, false);
            var rect = (RectTransform)imageObject.transform;
            rect.anchoredPosition = VirtualScreen.BodyCenter;
            m_Image = imageObject.AddComponent<RawImage>();
            m_Image.raycastTarget = false;
            imageObject.transform.SetAsFirstSibling();

            var back = M3.IconButton("PdfBack", screen.Rect, "chevron_left", M3Button.Variant.Tonal, Vector2.zero, PreviousPage, 48f);
            var next = M3.IconButton("PdfNext", screen.Rect, "chevron_right", M3Button.Variant.Tonal, Vector2.zero, NextPage, 48f);
            VirtualScreen.PinToFooter((RectTransform)back.transform, 0f, 55f);
            VirtualScreen.PinToFooter((RectTransform)next.transform, 0f, 115f);
            WorkspaceSounds.Attach(screen);
            m_Screen.Resized += OnResized;

#if UNITY_IOS && !UNITY_EDITOR
            m_Id = CardboardXR_PdfOpen(path);
            m_Pages = m_Id != 0 ? CardboardXR_PdfPageCount(m_Id) : 0;
#endif
            if (m_Id == 0)
            {
                m_Screen.SetTitle(Application.isEditor || Application.platform != RuntimePlatform.IPhonePlayer
                    ? "PDF działa na iPhonie (iOS)" : "Nie można otworzyć: " + m_Name);
                return;
            }
            ShowPage(Mathf.Clamp(page, 0, m_Pages - 1));
        }

        public void NextPage() { if (m_Pages > 0 && m_Page < m_Pages - 1) ShowPage(m_Page + 1); }
        public void PreviousPage() { if (m_Page > 0) ShowPage(m_Page - 1); }

        void ShowPage(int page)
        {
            m_Page = page;
            m_Screen.SetTitle($"{m_Name} — {m_Page + 1} / {m_Pages}");
            Render();
        }

        void OnResized(VirtualScreen screen)
        {
            m_RenderPending = true;
            m_RenderAt = Time.unscaledTime + 0.3f;
        }

        void Update()
        {
            if (m_RenderPending && Time.unscaledTime >= m_RenderAt && !m_Screen.IsStretching)
            {
                m_RenderPending = false;
                Render();
            }
        }

        // The page fitted into the text area, rendered at 2 px per dp (at most 2048 px)
        void Render()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id == 0)
                return;
            float aspect = CardboardXR_PdfPageAspect(m_Id, m_Page);
            var fitted = RemoteDesktopReceiver.FitInside(aspect, m_Screen.BodySizeNow);
            m_Image.rectTransform.sizeDelta = fitted;
            float scale = Mathf.Min(2f, 2048f / Mathf.Max(fitted.x, fitted.y));
            int width = Mathf.Max(16, Mathf.RoundToInt(fitted.x * scale));
            int height = Mathf.Max(16, Mathf.RoundToInt(fitted.y * scale));
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != height)
            {
                if (m_Texture != null)
                    Destroy(m_Texture);
                if (m_Handle.IsAllocated)
                    m_Handle.Free();
                m_Texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                m_Buffer = new byte[width * height * 4];
                m_Handle = GCHandle.Alloc(m_Buffer, GCHandleType.Pinned);
            }
            if (CardboardXR_PdfRender(m_Id, m_Page, width, height, m_Handle.AddrOfPinnedObject(), m_Buffer.Length) == 1)
            {
                m_Texture.LoadRawTextureData(m_Buffer);
                m_Texture.Apply(false);
                m_Image.texture = m_Texture;
            }
#endif
        }

        void OnDestroy()
        {
            if (m_Screen != null)
                m_Screen.Resized -= OnResized;
#if UNITY_IOS && !UNITY_EDITOR
            if (m_Id != 0)
                CardboardXR_PdfClose(m_Id);
#endif
            if (m_Handle.IsAllocated)
                m_Handle.Free();
            if (m_Texture != null)
                Destroy(m_Texture);
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int CardboardXR_PdfOpen(string path);
        [DllImport("__Internal")] static extern void CardboardXR_PdfClose(int id);
        [DllImport("__Internal")] static extern int CardboardXR_PdfPageCount(int id);
        [DllImport("__Internal")] static extern float CardboardXR_PdfPageAspect(int id, int page);
        [DllImport("__Internal")] static extern int CardboardXR_PdfRender(int id, int page, int width, int height, IntPtr dst, int capacity);
#endif
    }
}
