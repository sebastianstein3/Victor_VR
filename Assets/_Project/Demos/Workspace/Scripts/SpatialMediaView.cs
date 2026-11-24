using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CardboardXR.Stereo;
using CardboardXR.UI;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Spatial photos and videos in a VirtualScreen, in 3D: each eye sees its own picture, like on Vision
    // Pro. Two quads over the window's text area, on the layers only one eye's camera draws
    // (StereoRig.LeftEyeOnlyLayer / RightEyeOnlyLayer, as the Video demo's 3D sphere does).
    // Files: the app's Documents/Przestrzenne folder. Spatial HEIC photos and MV-HEVC videos from an
    // iPhone 15 Pro / 16 Pro are read by Plugins/iOS/CardboardXRSpatial.swift; side-by-side files
    // ("_sbs" in the name, .jpg/.png) are split here; anything else is shown flat (2D).
    public class SpatialMediaView : MonoBehaviour
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Przestrzenne");
        static readonly string[] k_Photos = { ".heic", ".heif", ".jpg", ".jpeg", ".png", ".dng", ".tif", ".tiff", ".gif", ".webp" };
        static readonly string[] k_Videos = { ".mov", ".mp4" };

        VirtualScreen m_Screen;
        readonly MeshRenderer[] m_Eyes = new MeshRenderer[2];
        readonly Material[] m_Materials = new Material[2];
        readonly Texture2D[] m_Textures = new Texture2D[2];
        readonly byte[][] m_Buffers = new byte[2][];
        readonly GCHandle[] m_Handles = new GCHandle[2];
        List<string> m_Files;
        int m_Index;
        int m_VideoId;
        float m_Aspect = 16f / 9f;

        public string FilePath => m_Files != null && m_Index < m_Files.Count ? m_Files[m_Index] : null;

        public static List<string> FindFiles()
        {
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var file in Directory.GetFiles(Folder))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(k_Photos, ext) >= 0 || Array.IndexOf(k_Videos, ext) >= 0)
                        files.Add(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] {e.Message}");
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public void Initialize(VirtualScreen screen, List<string> files, int index)
        {
            m_Screen = screen;
            m_Files = files;
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(860f, 640f)));

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            for (int eye = 0; eye < 2; eye++)
            {
                var quad = new GameObject(eye == 0 ? "Left Eye Picture" : "Right Eye Picture");
                quad.transform.SetParent(screen.Rect, false);
                quad.transform.localPosition = new Vector3(VirtualScreen.BodyCenter.x, VirtualScreen.BodyCenter.y, -1f); // 1 dp in front
                quad.layer = eye == 0 ? StereoRig.LeftEyeOnlyLayer : StereoRig.RightEyeOnlyLayer;
                quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                m_Eyes[eye] = quad.AddComponent<MeshRenderer>();
                m_Eyes[eye].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (shader != null)
                    m_Materials[eye] = new Material(shader) { name = quad.name };
                m_Eyes[eye].sharedMaterial = m_Materials[eye];
                quad.SetActive(false);
            }

            var back = M3.IconButton("MediaBack", screen.Rect, "chevron_left", M3Button.Variant.Tonal, Vector2.zero, Previous, 48f);
            var next = M3.IconButton("MediaNext", screen.Rect, "chevron_right", M3Button.Variant.Tonal, Vector2.zero, Next, 48f);
            VirtualScreen.PinToFooter((RectTransform)back.transform, 0f, 55f);
            VirtualScreen.PinToFooter((RectTransform)next.transform, 0f, 115f);
            WorkspaceSounds.Attach(screen);
            m_Screen.Resized += _ => Fit();
            Show(index);
        }

        public void Next() { if (m_Files.Count > 0) Show((m_Index + 1) % m_Files.Count); }
        public void Previous() { if (m_Files.Count > 0) Show((m_Index - 1 + m_Files.Count) % m_Files.Count); }

        void Show(int index)
        {
            CloseVideo();
            m_Index = index;
            string path = m_Files[index];
            string name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            bool ok;
            string kind;
            if (Array.IndexOf(k_Videos, ext) >= 0)
            {
                ok = OpenVideo(path);
                kind = ok ? "film 3D" : "nie jest filmem przestrzennym (MV-HEVC)";
            }
            else if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") // HEIC and the rest: ImageIO on the phone
            {
                ok = OpenNativePhoto(path, out bool stereo);
                kind = stereo ? "zdjęcie 3D" : "zdjęcie 2D";
            }
            else
            {
                ok = OpenImage(path, name.IndexOf("_sbs", StringComparison.OrdinalIgnoreCase) >= 0);
                kind = name.IndexOf("_sbs", StringComparison.OrdinalIgnoreCase) >= 0 ? "zdjęcie 3D (obok siebie)" : "zdjęcie 2D";
            }
            if (!ok && Application.platform != RuntimePlatform.IPhonePlayer)
                kind = "HEIC i filmy 3D działają na iPhonie";
            else if (!ok)
                kind = "nie można otworzyć";
            m_Screen.SetTitle($"{name} — {kind} ({index + 1}/{m_Files.Count})");
            for (int eye = 0; eye < 2; eye++)
                m_Eyes[eye].gameObject.SetActive(ok);
            Fit();
        }

        // ---- Photos ----

        bool OpenNativePhoto(string path, out bool stereo)
        {
            stereo = false;
#if UNITY_IOS && !UNITY_EDITOR
            int id = CardboardXR_SpatialPhotoOpen(path, out int width, out int height, out int isStereo);
            if (id == 0)
                return false;
            stereo = isStereo == 1;
            // At most 2048 px on the long side
            float scale = Mathf.Min(1f, 2048f / Mathf.Max(width, height));
            int w = Mathf.Max(2, (int)(width * scale)), h = Mathf.Max(2, (int)(height * scale));
            for (int eye = 0; eye < 2; eye++)
            {
                Allocate(eye, w, h, TextureFormat.RGBA32);
                if (CardboardXR_SpatialPhotoCopy(id, eye, m_Handles[eye].AddrOfPinnedObject(), w, h, m_Buffers[eye].Length) == 1)
                    Upload(eye);
            }
            CardboardXR_SpatialPhotoClose(id);
            m_Aspect = w / (float)h;
            return true;
#else
            return false;
#endif
        }

        // .jpg / .png: side by side (left half to the left eye) or one picture for both eyes
        bool OpenImage(string path, bool sideBySide)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path)))
                {
                    Destroy(image);
                    return false;
                }
            }
            catch (Exception)
            {
                Destroy(image);
                return false;
            }
            if (m_Textures[0] != null)
                Destroy(m_Textures[0]);
            if (m_Textures[1] != null && m_Textures[1] != m_Textures[0])
                Destroy(m_Textures[1]);
            for (int eye = 0; eye < 2; eye++)
            {
                m_Textures[eye] = image;
                if (m_Materials[eye] == null)
                    continue;
                m_Materials[eye].SetTexture("_BaseMap", image);
                m_Materials[eye].mainTexture = image;
                m_Materials[eye].mainTextureScale = sideBySide ? new Vector2(0.5f, 1f) : Vector2.one;
                m_Materials[eye].mainTextureOffset = sideBySide && eye == 1 ? new Vector2(0.5f, 0f) : Vector2.zero;
                m_Materials[eye].SetTextureScale("_BaseMap", m_Materials[eye].mainTextureScale);
                m_Materials[eye].SetTextureOffset("_BaseMap", m_Materials[eye].mainTextureOffset);
            }
            m_Aspect = (sideBySide ? image.width / 2f : image.width) / Mathf.Max(1f, image.height);
            return true;
        }

        void Allocate(int eye, int width, int height, TextureFormat format)
        {
            bool shared = eye == 1 && m_Textures[1] == m_Textures[0]; // a flat or side-by-side image used by both eyes
            bool reuse = !shared && m_Textures[eye] != null && m_Textures[eye].width == width && m_Textures[eye].height == height &&
                         m_Textures[eye].format == format && m_Handles[eye].IsAllocated && m_Buffers[eye].Length == width * height * 4;
            if (!reuse)
            {
                if (m_Textures[eye] != null && !shared)
                    Destroy(m_Textures[eye]);
                if (m_Handles[eye].IsAllocated)
                    m_Handles[eye].Free();
                m_Textures[eye] = new Texture2D(width, height, format, false);
                m_Buffers[eye] = new byte[width * height * 4];
                m_Handles[eye] = GCHandle.Alloc(m_Buffers[eye], GCHandleType.Pinned);
            }
            if (m_Materials[eye] != null)
            {
                m_Materials[eye].SetTexture("_BaseMap", m_Textures[eye]);
                m_Materials[eye].mainTexture = m_Textures[eye];
                m_Materials[eye].mainTextureScale = Vector2.one;
                m_Materials[eye].mainTextureOffset = Vector2.zero;
                m_Materials[eye].SetTextureScale("_BaseMap", Vector2.one);
                m_Materials[eye].SetTextureOffset("_BaseMap", Vector2.zero);
            }
        }

        void Upload(int eye)
        {
            m_Textures[eye].LoadRawTextureData(m_Buffers[eye]);
            m_Textures[eye].Apply(false);
        }

        // ---- Videos ----

        bool OpenVideo(string path)
        {
#if UNITY_IOS && !UNITY_EDITOR
            m_VideoId = CardboardXR_SpatialVideoOpen(path, out int width, out int height);
            if (m_VideoId == 0)
                return false;
            for (int eye = 0; eye < 2; eye++)
                Allocate(eye, width, height, TextureFormat.BGRA32);
            m_Aspect = width / (float)Mathf.Max(1, height);
            return true;
#else
            return false;
#endif
        }

        void Update()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_VideoId != 0 && CardboardXR_SpatialVideoNext(m_VideoId, m_Handles[0].AddrOfPinnedObject(),
                    m_Handles[1].AddrOfPinnedObject(), m_Buffers[0].Length) == 1)
            {
                Upload(0);
                Upload(1);
            }
#endif
        }

        void CloseVideo()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_VideoId != 0)
                CardboardXR_SpatialVideoClose(m_VideoId);
#endif
            m_VideoId = 0;
        }

        // The picture fitted into the text area, without stretching
        void Fit()
        {
            var size = RemoteDesktopReceiver.FitInside(m_Aspect, m_Screen.BodySizeNow);
            for (int eye = 0; eye < 2; eye++)
                m_Eyes[eye].transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        void OnDestroy()
        {
            CloseVideo();
            if (m_Textures[1] != null && m_Textures[1] != m_Textures[0])
                Destroy(m_Textures[1]);
            if (m_Textures[0] != null)
                Destroy(m_Textures[0]);
            for (int eye = 0; eye < 2; eye++)
            {
                if (m_Handles[eye].IsAllocated)
                    m_Handles[eye].Free();
                if (m_Materials[eye] != null)
                    Destroy(m_Materials[eye]);
            }
        }

        static Mesh s_Quad;

        // A 1 x 1 quad facing -Z (the side the window is seen from), UV (0,0) at the bottom-left
        static Mesh QuadMesh()
        {
            if (s_Quad != null)
                return s_Quad;
            s_Quad = new Mesh { name = "Picture Quad" };
            s_Quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            s_Quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            s_Quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_Quad.RecalculateNormals();
            return s_Quad;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int CardboardXR_SpatialPhotoOpen(string path, out int width, out int height, out int stereo);
        [DllImport("__Internal")] static extern int CardboardXR_SpatialPhotoCopy(int id, int eye, IntPtr dst, int width, int height, int capacity);
        [DllImport("__Internal")] static extern void CardboardXR_SpatialPhotoClose(int id);
        [DllImport("__Internal")] static extern int CardboardXR_SpatialVideoOpen(string path, out int width, out int height);
        [DllImport("__Internal")] static extern int CardboardXR_SpatialVideoNext(int id, IntPtr left, IntPtr right, int capacity);
        [DllImport("__Internal")] static extern void CardboardXR_SpatialVideoClose(int id);
#endif
    }
}
