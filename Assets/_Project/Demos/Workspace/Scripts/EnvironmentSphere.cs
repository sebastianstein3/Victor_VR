using System;
using System.Collections.Generic;
using System.IO;
using CardboardXR.Demos.Video;
using UnityEngine;
using UnityEngine.Video;

namespace CardboardXR.Demos.Workspace
{
    // Environments, like visionOS's: the room is replaced by a place all around (360 degrees), and the
    // windows float in it. Built in: four generated skies (no files needed). Yours: equirectangular
    // photos (.jpg/.png) or 360 videos (.mp4/.mov) in the app's Documents/Srodowiska folder.
    // Passthrough is switched off while an environment is shown, and back on after.
    public class EnvironmentSphere : MonoBehaviour
    {
        public const string Room = "Pokój (wyłącz)";
        public static readonly string[] BuiltIn = { "Zmierzch", "Noc", "Ocean", "Kosmos" };
        public static string Folder => Path.Combine(Application.persistentDataPath, "Srodowiska");
        static readonly string[] k_Images = { ".jpg", ".jpeg", ".png" };
        static readonly string[] k_Videos = { ".mp4", ".mov", ".m4v" };

        Transform m_Head;
        CoreServices m_Core;
        MeshRenderer m_Renderer;
        Material m_Material;
        Texture m_Texture;     // ours to free (generated or loaded)
        VideoPlayer m_Video;
        RenderTexture m_VideoTexture;
        bool? m_PassthroughBefore;

        public string Current { get; private set; }

        public void Initialize(CoreServices core)
        {
            m_Core = core;
            m_Head = core.Head;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[Workspace] Brak shadera URP/Unlit: środowiska niedostępne");
                return;
            }
            var sphere = new GameObject("Environment Sphere");
            sphere.transform.SetParent(transform, false);
            sphere.AddComponent<MeshFilter>().sharedMesh = VideoSphere.BuildMesh(Projection.Full360, 40f);
            m_Renderer = sphere.AddComponent<MeshRenderer>();
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Material = new Material(shader) { name = "Environment" };
            m_Renderer.sharedMaterial = m_Material;
            sphere.SetActive(false);
        }

        // Built-in names, then the user's files
        public static List<(string label, string source)> Find()
        {
            var list = new List<(string, string)>();
            foreach (var name in BuiltIn)
                list.Add((name, name));
            try
            {
                Directory.CreateDirectory(Folder);
                var files = new List<string>(Directory.GetFiles(Folder));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(k_Images, ext) >= 0 || Array.IndexOf(k_Videos, ext) >= 0)
                        list.Add((Path.GetFileNameWithoutExtension(file), file));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] {e.Message}");
            }
            return list;
        }

        // A built-in name, a file path, or null / Room for the room again
        public void Show(string source)
        {
            Clear();
            if (m_Renderer == null || string.IsNullOrEmpty(source) || source == Room)
            {
                SetPassthroughBack();
                Current = null;
                return;
            }

            var ext = Path.GetExtension(source).ToLowerInvariant();
            if (Array.IndexOf(BuiltIn, source) >= 0)
            {
                m_Texture = Generate(source);
            }
            else if (Array.IndexOf(k_Videos, ext) >= 0)
            {
                m_VideoTexture = new RenderTexture(2048, 1024, 0);
                m_Video = gameObject.AddComponent<VideoPlayer>();
                m_Video.source = VideoSource.Url;
                m_Video.url = source;
                m_Video.isLooping = true;
                m_Video.renderMode = VideoRenderMode.RenderTexture;
                m_Video.targetTexture = m_VideoTexture;
                m_Video.audioOutputMode = VideoAudioOutputMode.Direct;
                m_Video.Play();
            }
            else
            {
                try
                {
                    var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (image.LoadImage(File.ReadAllBytes(source)))
                        m_Texture = image;
                    else
                        Destroy(image);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Workspace] Środowisko {source}: {e.Message}");
                }
            }

            var texture = m_Video != null ? (Texture)m_VideoTexture : m_Texture;
            if (texture == null)
            {
                SetPassthroughBack();
                return;
            }
            m_Material.SetTexture("_BaseMap", texture);
            m_Material.mainTexture = texture;
            m_Renderer.gameObject.SetActive(true);
            Current = source;
            if (m_PassthroughBefore == null && m_Core.Passthrough != null)
            {
                m_PassthroughBefore = m_Core.Passthrough.IsOn;
                m_Core.Passthrough.Choose(false); // the place instead of the room
            }
        }

        void Clear()
        {
            if (m_Renderer != null)
                m_Renderer.gameObject.SetActive(false);
            if (m_Video != null)
            {
                m_Video.Stop();
                Destroy(m_Video);
                m_Video = null;
            }
            if (m_VideoTexture != null)
            {
                m_VideoTexture.Release();
                Destroy(m_VideoTexture);
                m_VideoTexture = null;
            }
            if (m_Texture != null)
            {
                Destroy(m_Texture);
                m_Texture = null;
            }
        }

        void SetPassthroughBack()
        {
            if (m_PassthroughBefore == true && m_Core != null && m_Core.Passthrough != null)
                m_Core.Passthrough.Choose(true);
            m_PassthroughBefore = null;
        }

        void LateUpdate()
        {
            // Centred on the eyes, so it is "infinitely" far: walking doesn't bring it closer
            if (m_Renderer != null && m_Renderer.gameObject.activeSelf && m_Head != null)
                m_Renderer.transform.position = m_Head.position;
        }

        void OnDestroy()
        {
            Clear();
            SetPassthroughBack();
            if (m_Material != null)
                Destroy(m_Material);
        }

        // ---- Generated skies (equirectangular: x = longitude, y = latitude from the bottom) ----
        static Texture2D Generate(string name)
        {
            const int width = 1024, height = 512;
            var pixels = new Color32[width * height];
            var random = new System.Random(name.GetHashCode());
            for (int y = 0; y < height; y++)
            {
                float v = y / (height - 1f); // 0 = straight down, 1 = straight up
                Color c = name switch
                {
                    "Zmierzch" => v > 0.5f
                        ? Color.Lerp(new Color(0.98f, 0.55f, 0.3f), new Color(0.2f, 0.12f, 0.35f), Mathf.Pow((v - 0.5f) * 2f, 0.6f))
                        : Color.Lerp(new Color(0.08f, 0.06f, 0.12f), new Color(0.35f, 0.18f, 0.22f), v * 2f),
                    "Noc" => v > 0.5f
                        ? Color.Lerp(new Color(0.08f, 0.12f, 0.25f), new Color(0.01f, 0.02f, 0.06f), (v - 0.5f) * 2f)
                        : Color.Lerp(new Color(0.01f, 0.01f, 0.02f), new Color(0.04f, 0.05f, 0.08f), v * 2f),
                    "Ocean" => v > 0.5f
                        ? Color.Lerp(new Color(0.75f, 0.88f, 0.98f), new Color(0.2f, 0.45f, 0.85f), (v - 0.5f) * 2f)
                        : Color.Lerp(new Color(0.02f, 0.12f, 0.25f), new Color(0.1f, 0.35f, 0.55f), Mathf.Pow(v * 2f, 3f)),
                    _ => new Color(0.005f, 0.005f, 0.015f), // Kosmos
                };
                var c32 = (Color32)c;
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = c32;
            }
            // Stars above the horizon (night, space)
            int stars = name == "Kosmos" ? 2500 : name == "Noc" ? 900 : 0;
            for (int i = 0; i < stars; i++)
            {
                int x = random.Next(width);
                int y = name == "Kosmos" ? random.Next(height) : height / 2 + random.Next(height / 2);
                byte b = (byte)random.Next(150, 256);
                pixels[y * width + x] = new Color32(b, b, (byte)Mathf.Min(255, b + 20), 255);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
