using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // The iPhone's Photos library in a window (Więcej > Galeria, "galeria"), no copying needed: a grid of
    // thumbnails, newest first, filtered to everything / spatial (iOS 18+) / videos. Pinch a thumbnail:
    // the original is fetched (from iCloud if needed, Plugins/iOS/CardboardXRPhotos.mm) and opens in a
    // new window: photos and spatial photos/videos in SpatialMediaView (each eye its picture), other
    // videos in FilmPlayer. Footer: previous / next page, the filter. Swipe up/down also turns pages.
    public class GalleryView : MonoBehaviour
    {
        const int k_Columns = 4;
        const int k_Rows = 2;
        const float k_Thumb = 160f; // dp
        const int k_ThumbPixels = 256;
        static readonly string[] k_FilterNames = { "Wszystkie", "Przestrzenne", "Filmy" };
        static readonly List<GalleryView> s_Open = new();

        VirtualScreen m_Screen;
        RectTransform m_Grid;
        M3Button m_FilterButton;
        Action<string, bool, bool> m_Open; // path, video, spatial
        readonly List<Texture2D> m_Textures = new();
        byte[] m_Buffer;
        GCHandle m_Handle;
        int m_Filter;
        int m_Count;
        int m_Page;
        bool m_Busy;

        int PerPage => k_Columns * k_Rows;
        int Pages => Mathf.Max(1, Mathf.CeilToInt(m_Count / (float)PerPage));

        public void Initialize(VirtualScreen screen, Action<string, bool, bool> open)
        {
            m_Screen = screen;
            m_Open = open;
            s_Open.Add(this);
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(760f, 580f)));
            m_Grid = M3.Group("GalleryGrid", m_Screen.Rect, VirtualScreen.BodyCenter, new Vector2(700f, 340f));

            var back = M3.IconButton("GalleryBack", m_Screen.Rect, "chevron_left", M3Button.Variant.Tonal, Vector2.zero, PreviousPage, 48f);
            var next = M3.IconButton("GalleryNext", m_Screen.Rect, "chevron_right", M3Button.Variant.Tonal, Vector2.zero, NextPage, 48f);
            m_FilterButton = M3.Button("GalleryFilter", m_Screen.Rect, k_FilterNames[0], M3Button.Variant.Outlined, Vector2.zero,
                CycleFilter, null, 150f);
            VirtualScreen.PinToFooter((RectTransform)back.transform, 0f, 40f);
            VirtualScreen.PinToFooter((RectTransform)next.transform, 0f, 96f);
            VirtualScreen.PinToFooter((RectTransform)m_FilterButton.transform, 0f, 205f);
            WorkspaceSounds.Attach(screen);

            m_Buffer = new byte[k_ThumbPixels * k_ThumbPixels * 4];
            m_Handle = GCHandle.Alloc(m_Buffer, GCHandleType.Pinned);
#if UNITY_IOS && !UNITY_EDITOR
            EnsureReceiver();
            m_Screen.SetTitle("Galeria — zezwól na dostęp do Zdjęć na ekranie telefonu");
            CardboardXR_PhotosAuthorize();
#else
            m_Screen.SetTitle("Galeria działa na iPhonie");
#endif
        }

        public void NextPage()
        {
            if (m_Page < Pages - 1)
                ShowPage(m_Page + 1);
        }

        public void PreviousPage()
        {
            if (m_Page > 0)
                ShowPage(m_Page - 1);
        }

        void CycleFilter()
        {
            m_Filter = (m_Filter + 1) % k_FilterNames.Length;
            m_FilterButton.Text = k_FilterNames[m_Filter];
            Fetch();
        }

        void Fetch()
        {
#if UNITY_IOS && !UNITY_EDITOR
            m_Count = CardboardXR_PhotosFetch(m_Filter);
#endif
            ShowPage(0);
        }

        void ShowPage(int page)
        {
            m_Page = Mathf.Clamp(page, 0, Pages - 1);
            foreach (Transform child in m_Grid)
                Destroy(child.gameObject);
            foreach (var texture in m_Textures)
                Destroy(texture);
            m_Textures.Clear();
            string empty = m_Filter == 1 ? " (zdjęcia przestrzenne: iOS 18+)" : "";
            m_Screen.SetTitle(m_Count == 0 ? $"Galeria — {k_FilterNames[m_Filter]}: brak{empty}"
                : $"Galeria — {k_FilterNames[m_Filter]} · {m_Page + 1}/{Pages}");

            int first = m_Page * PerPage;
            for (int i = 0; i < PerPage && first + i < m_Count; i++)
            {
                int index = first + i, column = i % k_Columns, row = i / k_Columns;
                var position = new Vector2((column - (k_Columns - 1) / 2f) * (k_Thumb + 8f), ((k_Rows - 1) / 2f - row) * (k_Thumb + 8f));
                AddThumbnail(index, position);
            }
        }

        void AddThumbnail(int index, Vector2 position)
        {
            // The whole tile is the button: an Image the gaze hits, with a Button (a Selectable, so a pinch
            // clicks it) and the picture on top
            var tile = M3.Surface($"Thumb_{index}", m_Grid, position, new Vector2(k_Thumb, k_Thumb), M3Theme.SurfaceContainerHighest, 12f, raycastTarget: true);
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;
            button.onClick.AddListener(() =>
            {
                WorkspaceSounds.PlayTap();
                Open(index);
            });

            int info = 0;
#if UNITY_IOS && !UNITY_EDITOR
            info = CardboardXR_PhotosInfo(index);
            if (CardboardXR_PhotosThumbnail(index, k_ThumbPixels, m_Handle.AddrOfPinnedObject(), m_Buffer.Length) == 1)
            {
                var texture = new Texture2D(k_ThumbPixels, k_ThumbPixels, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(m_Buffer);
                texture.Apply(false);
                m_Textures.Add(texture);
                var picture = new GameObject("Picture", typeof(RectTransform)).AddComponent<RawImage>();
                picture.transform.SetParent(tile.transform, false);
                picture.rectTransform.sizeDelta = new Vector2(k_Thumb - 8f, k_Thumb - 8f);
                picture.texture = texture;
                picture.raycastTarget = false;
            }
#endif
            if ((info & 2) != 0)
                Badge(tile.rectTransform, "3D");
            else if ((info & 1) != 0)
                Badge(tile.rectTransform, "FILM");
        }

        static void Badge(RectTransform tile, string text)
        {
            var badge = M3.Surface("Badge", tile, new Vector2(k_Thumb / 2f - 34f, -k_Thumb / 2f + 20f), new Vector2(56f, 26f),
                M3Theme.PrimaryContainer, 13f);
            badge.raycastTarget = false;
            var label = M3.Text("BadgeText", badge.rectTransform, text, M3Theme.Type.LabelLarge, Vector2.zero, new Vector2(56f, 26f),
                M3Theme.OnPrimaryContainer);
            label.raycastTarget = false;
        }

        void Open(int index)
        {
            if (m_Busy)
                return;
#if UNITY_IOS && !UNITY_EDITOR
            m_Busy = true;
            m_Screen.SetTitle("Galeria — otwieram (z iCloud może to chwilę potrwać)…");
            CardboardXR_PhotosExport(index);
#endif
        }

        void OnAuthorized(Auth auth)
        {
            if (!auth.ok)
            {
                m_Screen.SetTitle("Galeria — brak dostępu (Ustawienia > CardboardXR > Zdjęcia)");
                return;
            }
            Fetch();
        }

        void OnExported(Exported result)
        {
            m_Busy = false;
            if (!string.IsNullOrEmpty(result.error))
            {
                m_Screen.SetTitle("Galeria — " + result.error);
                return;
            }
            ShowPage(m_Page); // the title back
            m_Open?.Invoke(result.path, result.video, result.spatial);
        }

        void OnDestroy()
        {
            s_Open.Remove(this);
            foreach (var texture in m_Textures)
                Destroy(texture);
            if (m_Handle.IsAllocated)
                m_Handle.Free();
        }

        [Serializable]
        class Auth { public bool ok; public bool limited; }

        [Serializable]
        class Exported { public string path; public bool video; public bool spatial; public string name; public string error; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CardboardXR_PhotosAuthorize();
        [DllImport("__Internal")] static extern int CardboardXR_PhotosFetch(int filter);
        [DllImport("__Internal")] static extern int CardboardXR_PhotosInfo(int index);
        [DllImport("__Internal")] static extern int CardboardXR_PhotosThumbnail(int index, int size, IntPtr dst, int capacity);
        [DllImport("__Internal")] static extern void CardboardXR_PhotosExport(int index);

        static void EnsureReceiver()
        {
            if (GameObject.Find("GalleryNative") != null)
                return;
            var go = new GameObject("GalleryNative");
            DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
        }

        [Preserve]
        class Receiver : MonoBehaviour
        {
            [Preserve]
            void OnAuthorized(string json)
            {
                var auth = JsonUtility.FromJson<Auth>(json) ?? new Auth();
                foreach (var gallery in s_Open.ToArray())
                {
                    if (gallery != null)
                        gallery.OnAuthorized(auth);
                }
            }

            // Only the gallery that asked is busy; it opens the file
            [Preserve]
            void OnExported(string json)
            {
                var result = JsonUtility.FromJson<Exported>(json) ?? new Exported { error = "błąd" };
                foreach (var gallery in s_Open.ToArray())
                {
                    if (gallery != null && gallery.m_Busy)
                    {
                        gallery.OnExported(result);
                        break;
                    }
                }
            }
        }
#endif
    }
}
