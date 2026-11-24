using System;
using System.Collections.Generic;
using System.IO;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CardboardXR.Demos.Workspace
{
    // Your own films in a VirtualScreen: .mp4/.mov/.m4v from the app's Documents/Filmy folder (copied in
    // with Finder, the Files app, or Więcej > Pliki). Footer: back 10 s, play/pause, forward 10 s, next
    // film, and "Kino", which dims the room and plays the film on the big cinema screen as well.
    // Stretch the window (corner handle) for a bigger picture. Voice: "pauza" / "odtwórz".
    public class FilmPlayer : MonoBehaviour
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Filmy");
        static readonly string[] k_Videos = { ".mp4", ".mov", ".m4v" };

        // The film playing most recently: the cinema screen shows it
        public static FilmPlayer Active { get; private set; }

        VirtualScreen m_Screen;
        RawImage m_Image;
        VideoPlayer m_Player;
        RenderTexture m_Texture;
        M3Button m_PlayButton;
        List<string> m_Files;
        int m_Index;
        string m_Name;
        float m_NextTitle;

        public Texture PlayingTexture => m_Player != null && m_Player.isPrepared && m_Texture != null ? m_Texture : null;
        public bool IsPlaying => m_Player != null && m_Player.isPlaying;

        // Documents/Filmy first, then any films in Documents itself
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
                        if (Array.IndexOf(k_Videos, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                            files.Add(file);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] {e.Message}");
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public void Initialize(VirtualScreen screen, List<string> files, int index, Action toggleCinema)
        {
            m_Screen = screen;
            m_Files = files;
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(860f, 560f))); // 16:9 picture

            var imageObject = new GameObject("FilmPicture", typeof(RectTransform));
            imageObject.transform.SetParent(screen.Rect, false);
            ((RectTransform)imageObject.transform).anchoredPosition = VirtualScreen.BodyCenter;
            m_Image = imageObject.AddComponent<RawImage>();
            m_Image.raycastTarget = false;
            m_Image.color = Color.white;
            m_Image.enabled = false;
            imageObject.transform.SetAsFirstSibling();

            // Footer, from the left: 48 dp buttons 8 dp apart (the scale buttons and stretch handle are further right)
            AddButton("FilmBack", "replay_10", () => Seek(-10.0), 40f);
            m_PlayButton = AddButton("FilmPlay", "pause", TogglePause, 96f);
            AddButton("FilmForward", "forward_10", () => Seek(10.0), 152f);
            AddButton("FilmNext", "skip_next", Next, 208f);
            AddButton("FilmCinema", "movie", () => toggleCinema?.Invoke(), 264f);
            WorkspaceSounds.Attach(screen);

            m_Player = gameObject.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.source = VideoSource.Url;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.audioOutputMode = VideoAudioOutputMode.Direct;
            m_Player.isLooping = false;
            m_Player.prepareCompleted += OnPrepared;
            m_Player.errorReceived += OnError;
            m_Player.loopPointReached += _ => SetPlayIcon();
            m_Screen.Resized += _ => Fit();
            Play(index);
        }

        M3Button AddButton(string name, string icon, UnityEngine.Events.UnityAction action, float x)
        {
            var button = M3.IconButton(name, m_Screen.Rect, icon, M3Button.Variant.Tonal, Vector2.zero, action, 48f);
            VirtualScreen.PinToFooter((RectTransform)button.transform, 0f, x);
            return button;
        }

        public void Next() { if (m_Files.Count > 0) Play((m_Index + 1) % m_Files.Count); }

        public void TogglePause()
        {
            if (m_Player == null || !m_Player.isPrepared)
                return;
            if (m_Player.isPlaying)
                m_Player.Pause();
            else
                m_Player.Play();
            Active = this;
            SetPlayIcon();
        }

        void Play(int index)
        {
            m_Index = index;
            m_Name = Path.GetFileNameWithoutExtension(m_Files[index]);
            m_Screen.SetTitle(m_Name + " — wczytywanie…");
            m_Image.enabled = false;
            m_Player.Stop();
            m_Player.url = m_Files[index];
            m_Player.Prepare();
            Active = this;
        }

        void OnPrepared(VideoPlayer player)
        {
            int width = Mathf.Max(16, (int)player.width), height = Mathf.Max(16, (int)player.height);
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != height)
            {
                if (m_Texture != null)
                {
                    m_Texture.Release();
                    Destroy(m_Texture);
                }
                m_Texture = new RenderTexture(width, height, 0);
            }
            player.targetTexture = m_Texture;
            m_Image.texture = m_Texture;
            m_Image.enabled = true;
            Fit();
            player.Play();
            SetPlayIcon();
        }

        void OnError(VideoPlayer player, string message)
        {
            m_Screen.SetTitle(m_Name + " — nie można odtworzyć (wymagany H.264 lub HEVC w .mp4/.mov)");
            Debug.LogWarning($"[Workspace] Film {m_Name}: {message}");
        }

        void Seek(double seconds)
        {
            if (m_Player == null || !m_Player.isPrepared || !m_Player.canSetTime)
                return;
            double length = m_Player.length > 0 ? m_Player.length : double.MaxValue;
            m_Player.time = Math.Max(0.0, Math.Min(length - 0.5, m_Player.time + seconds));
        }

        void SetPlayIcon()
        {
            if (m_PlayButton != null)
                m_PlayButton.Icon = m_Player != null && m_Player.isPlaying ? "pause" : "play_arrow";
        }

        void Fit()
        {
            if (m_Texture == null)
                return;
            m_Image.rectTransform.sizeDelta =
                RemoteDesktopReceiver.FitInside(m_Texture.width / (float)Mathf.Max(1, m_Texture.height), m_Screen.BodySizeNow);
        }

        void Update()
        {
            // Time in the title once a second
            if (m_Player == null || !m_Player.isPrepared || Time.unscaledTime < m_NextTitle)
                return;
            m_NextTitle = Time.unscaledTime + 1f;
            m_Screen.SetTitle($"{m_Name}  {Clock(m_Player.time)} / {Clock(m_Player.length)}");
        }

        static string Clock(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
            if (m_Player != null)
            {
                m_Player.Stop();
                Destroy(m_Player);
            }
            if (m_Texture != null)
            {
                m_Texture.Release();
                Destroy(m_Texture);
            }
        }
    }
}
