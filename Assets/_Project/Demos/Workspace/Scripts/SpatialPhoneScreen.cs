using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CardboardXR.Demos.Video;
using CardboardXR.Interaction;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CardboardXR.Demos.Workspace
{
    // A vertical spatial phone screen (aspect ratio 9:18) floating in the room.
    // Has a Home Screen with app icons (TikTok, Shorts, Reels, Twitch) and an active media player
    // controllable via hand gestures (SwipeUp for next clip, SwipeDown for previous).
    public class SpatialPhoneScreen : MonoBehaviour
    {
        const float k_PhoneWidth = 280f;
        const float k_PhoneHeight = 560f;

        public struct SocialApp
        {
            public string name;
            public string icon;
            public string url;
            public Color color;
            public string[] sampleClips;
        }

        static readonly SocialApp[] s_Apps =
        {
            new()
            {
                name = "Messenger",
                icon = "bolt",
                url = "messenger.com",
                color = new Color(0.0f, 0.58f, 1.0f),
                sampleClips = new[]
                {
                    "[Marek]: Hej! Jak idą testy gogli VR na iPhone 16 Pro?",
                    "[Anna]: Widziałam to wideo z CardboardXR, kosmos!",
                    "[Piotr]: Klawiatura na biurku z LiDARem działa rewelacyjnie!",
                    "[Krzysztof]: Kiedy premiera Twojego stanowiska pracy w MR?"
                }
            },
            new()
            {
                name = "TikTok",
                icon = "auto_awesome",
                url = "tiktok.com/@trending",
                color = new Color(0.99f, 0.17f, 0.33f),
                sampleClips = new[]
                {
                    "@tech_master: Jak działa ARKit LiDAR w VR! #vr #tech",
                    "@travel_vibes: Zachód słońca nad fiordami w 4K #travel",
                    "@future_design: Spatial Computing i pływające monitory #apple",
                    "@coding_life: Pisanie kodu gestami dłoni w powietrzu! #dev"
                }
            },
            new()
            {
                name = "Shorts",
                icon = "play_arrow",
                url = "youtube.com/shorts",
                color = new Color(1f, 0.15f, 0.15f),
                sampleClips = new[]
                {
                    "Krótki przewodnik: Budowa gogli VR ze smartfona",
                    "Czy A18 Pro poradzi sobie z 120Hz VR? Test wydajności",
                    "Niewidzialna klawiatura na prawdziwym biurku! Recenzja"
                }
            },
            new()
            {
                name = "Reels",
                icon = "movie",
                url = "instagram.com/reels",
                color = new Color(0.88f, 0.19f, 0.42f),
                sampleClips = new[]
                {
                    "Architektura wnętrz w Mieszanej Rzeczywistości",
                    "Top 5 trików na gesty w przestrzeni 3D"
                }
            },
            new()
            {
                name = "Twitch",
                icon = "visibility",
                url = "twitch.tv/directory",
                color = new Color(0.57f, 0.27f, 1f),
                sampleClips = new[]
                {
                    "Stream na żywo: Tworzenie gier w Unity 6",
                    "Speedrun klasycznych gier VR bez kontrolerów"
                }
            },
            new()
            {
                name = "Notatki",
                icon = "edit",
                url = "icloud.com/notes",
                color = new Color(0.95f, 0.75f, 0.2f),
                sampleClips = new[]
                {
                    "Zadanie: Skalibrować profil soczewek Shinecon G10",
                    "Kupić aktywny cooler MagSafe z płytką Peltiera",
                    "Sprawdzić 120 FPS na ProMotion w Unity"
                }
            }
        };

        enum ViewMode
        {
            Home,
            AppPlayer
        }

        RectTransform m_CanvasRect;
        RectTransform m_HomeContainer;
        RectTransform m_PlayerContainer;
        Transform m_Head;
        float m_Distance = 1.25f;
        bool m_IsDragging;
        Vector3 m_DragOffset = new(0f, 0f, 1.25f);
        // This phone's own copy of the app table, so a chat sent here doesn't come back in the next session.
        SocialApp[] m_Apps;

        ViewMode m_Mode = ViewMode.Home;
        SocialApp m_CurrentApp;
        int m_CurrentClipIndex;
        bool m_IsPlaying = true;

        TextMeshProUGUI m_PlayerTitleText;
        TextMeshProUGUI m_PlayerUrlText;
        TextMeshProUGUI m_ClipContentText;
        TextMeshProUGUI m_StatusText;
        M3Button m_PlayPauseBtn;
        M3Button m_GrabBtn;
        AudioSource m_SpatialAudio;

        public event Action<SpatialPhoneScreen> Closed;

        public RectTransform Rect => m_CanvasRect;
        public bool IsDragging => m_IsDragging;

        public static SpatialPhoneScreen Create(Transform parent, Transform head, Vector3 startPos, Quaternion startRot, float distance = 1.25f)
        {
            var panel = WorldUi.Panel("SpatialPhone", parent, new Vector2(k_PhoneWidth, k_PhoneHeight), distance,
                new Color(0.08f, 0.09f, 0.12f), 36f);
            panel.SetPositionAndRotation(startPos, startRot);

            var phone = panel.gameObject.AddComponent<SpatialPhoneScreen>();
            phone.Initialize(panel, head, distance);
            return phone;
        }

        public void Initialize(RectTransform canvasRect, Transform head, float distance = 1.25f)
        {
            m_CanvasRect = canvasRect;
            m_Head = head;
            m_Distance = distance;

            // Configure 3D Spatial Audio for AirPods Pro 2 directly on the moving smartphone
            m_SpatialAudio = gameObject.AddComponent<AudioSource>();
            m_SpatialAudio.spatialBlend = 1.0f;
            m_SpatialAudio.spatialize = true;
            m_SpatialAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            m_SpatialAudio.minDistance = 0.5f;
            m_SpatialAudio.maxDistance = 8f;

            m_Apps = new SocialApp[s_Apps.Length];
            for (int i = 0; i < s_Apps.Length; i++)
            {
                m_Apps[i] = s_Apps[i];
                m_Apps[i].sampleClips = (string[])s_Apps[i].sampleClips.Clone();
            }

            BuildPhoneFrame();
            ShowHomeScreen();
            WorkspaceSounds.Attach(this);
        }

        void BuildPhoneFrame()
        {
            // Top Dynamic Island & Status Bar
            float topY = (k_PhoneHeight / 2f) - 22f; // 258
            string timeStr = DateTime.Now.ToString("HH:mm");
            M3.Text("Clock", m_CanvasRect, timeStr, M3Theme.Type.LabelLarge,
                new Vector2(-80f, topY), new Vector2(80f, 24f), M3Theme.OnSurface, TextAlignmentOptions.Left);

            // Dynamic Island pill
            M3.Surface("DynamicIsland", m_CanvasRect, new Vector2(0f, topY), new Vector2(76f, 18f),
                Color.black, 9f);

            // Close button (top right)
            M3.IconButton("ClosePhone", m_CanvasRect, "close", M3Button.Variant.Text,
                new Vector2(100f, topY - 10f), () => { Closed?.Invoke(this); Destroy(gameObject); }, 48f);

            // Bottom Grab Handle Bar
            float grabY = -(k_PhoneHeight / 2f) + 32f; // -248: the 56 dp button then ends 4 dp inside the panel
            m_GrabBtn = M3.Button("PhoneGrab", m_CanvasRect, "Przenieś smartfon", M3Button.Variant.Text,
                new Vector2(0f, grabY), OnGrabClick, "open_with", k_PhoneWidth - 40f);

            // Content Containers
            m_HomeContainer = M3.Group("HomeContainer", m_CanvasRect, new Vector2(0f, 0f), new Vector2(k_PhoneWidth, k_PhoneHeight - 90f));
            m_PlayerContainer = M3.Group("PlayerContainer", m_CanvasRect, new Vector2(0f, 0f), new Vector2(k_PhoneWidth, k_PhoneHeight - 90f));
        }

        string m_ChatDraft = "";
        bool m_IsChatFocused;

        public bool IsChatFocused
        {
            get => m_IsChatFocused && m_Mode == ViewMode.AppPlayer && m_CurrentApp.name == "Messenger";
            set => m_IsChatFocused = value;
        }

        public void AppendChatChar(char c)
        {
            m_ChatDraft += c;
            UpdateClipDisplay();
        }

        public void ChatBackspace()
        {
            if (m_ChatDraft.Length > 0)
            {
                m_ChatDraft = m_ChatDraft.Substring(0, m_ChatDraft.Length - 1);
                UpdateClipDisplay();
            }
        }

        public void SendChatMessage()
        {
            if (string.IsNullOrWhiteSpace(m_ChatDraft))
                return;

            var list = new List<string>(m_CurrentApp.sampleClips)
            {
                $"[Ty]: {m_ChatDraft}"
            };
            m_CurrentApp.sampleClips = list.ToArray();
            for (int i = 0; i < m_Apps.Length; i++)
            {
                if (m_Apps[i].name == m_CurrentApp.name)
                    m_Apps[i].sampleClips = m_CurrentApp.sampleClips;
            }
            m_CurrentClipIndex = m_CurrentApp.sampleClips.Length - 1;
            m_ChatDraft = "";
            UpdateClipDisplay();
        }

        public void ShowHomeScreen()
        {
            StopVideo();
            m_Mode = ViewMode.Home;
            m_IsChatFocused = false;
            m_HomeContainer.gameObject.SetActive(true);
            m_PlayerContainer.gameObject.SetActive(false);

            // Clear old home items
            foreach (Transform t in m_HomeContainer)
                Destroy(t.gameObject);

            // Greeting header
            M3.Text("HomeHeader", m_HomeContainer, "Twoje Aplikacje", M3Theme.Type.TitleMedium,
                new Vector2(0f, 190f), new Vector2(240f, 30f), M3Theme.OnSurface, TextAlignmentOptions.Center);
            M3.Text("HomeSub", m_HomeContainer, "Wybierz aplikację do podglądu", M3Theme.Type.BodyMedium,
                new Vector2(0f, 166f), new Vector2(240f, 20f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Center);

            // Grid of 6 App Icons (2 columns x 3 rows)
            float startY = 92f;
            float spacingY = 100f;
            float posX = 64f;

            for (int i = 0; i < m_Apps.Length; i++)
            {
                var app = m_Apps[i];
                float colX = (i % 2 == 0) ? -posX : posX;
                float rowY = startY - (i / 2) * spacingY;

                // App Icon Squircle Button
                M3.IconButton($"AppBtn_{app.name}", m_HomeContainer, app.icon, M3Button.Variant.Filled,
                    new Vector2(colX, rowY), () => LaunchApp(app), M3.k_ButtonHeight);

                // App Label
                M3.Text($"Label_{app.name}", m_HomeContainer, app.name, M3Theme.Type.LabelLarge,
                    new Vector2(colX, rowY - 40f), new Vector2(110f, 20f), M3Theme.OnSurface, TextAlignmentOptions.Center);
            }

            // Home is rebuilt each time it is shown, so its buttons get the tap again
            WorkspaceSounds.Attach(m_HomeContainer);
        }

        public void LaunchApp(SocialApp app)
        {
            FindVideos();
            m_Mode = ViewMode.AppPlayer;
            m_CurrentApp = app;
            m_CurrentClipIndex = 0;
            m_IsPlaying = true;
            m_IsChatFocused = (app.name == "Messenger");

            m_HomeContainer.gameObject.SetActive(false);
            m_PlayerContainer.gameObject.SetActive(true);

            // Clear old player items
            foreach (Transform t in m_PlayerContainer)
                Destroy(t.gameObject);

            BuildAppPlayerView();
        }

        void BuildAppPlayerView()
        {
            // Top Nav Bar
            float headerY = 196f;
            M3.IconButton("BackBtn", m_PlayerContainer, "chevron_left", M3Button.Variant.Text,
                new Vector2(-96f, headerY), ShowHomeScreen, 48f);

            m_PlayerTitleText = M3.Text("AppTitle", m_PlayerContainer, m_CurrentApp.name, M3Theme.Type.TitleMedium,
                new Vector2(8f, headerY + 6f), new Vector2(130f, 28f), m_CurrentApp.color, TextAlignmentOptions.Center);

            m_PlayerUrlText = M3.Text("AppUrl", m_PlayerContainer, m_CurrentApp.url, M3Theme.Type.BodyMedium,
                new Vector2(8f, headerY - 16f), new Vector2(150f, 20f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Center);

            // Simulated Video Display Card (Aspect 9:14 inside screen)
            M3.Surface("VideoScreenBg", m_PlayerContainer, new Vector2(0f, 10f),
                new Vector2(k_CardWidth, k_CardHeight), new Color(0.14f, 0.15f, 0.18f), 20f);

            // Where a real clip plays (see PlayVideo); hidden until one is ready
            var videoRect = M3.Group("VideoImage", m_PlayerContainer, new Vector2(0f, 10f), new Vector2(k_CardWidth, k_CardHeight));
            m_VideoImage = videoRect.gameObject.AddComponent<RawImage>();
            m_VideoImage.raycastTarget = false;
            m_VideoImage.enabled = false;

            // Inner video content text
            m_ClipContentText = M3.Text("ClipContent", m_PlayerContainer, "", M3Theme.Type.BodyMedium,
                new Vector2(0f, 40f), new Vector2(220f, 140f), Color.white, TextAlignmentOptions.Center);
            // Long chats must not hide the draft line at the bottom: show the last page of the text.
            m_ClipContentText.overflowMode = TextOverflowModes.Page;

            // Overlay Video Info
            m_StatusText = M3.Text("StatusText", m_PlayerContainer, "Gest: Swipe Up \u2191 nast\u0119pny film", M3Theme.Type.BodyMedium,
                new Vector2(0f, -60f), new Vector2(220f, 30f), m_CurrentApp.color, TextAlignmentOptions.Center);

            // Playback controls row
            float ctrlY = -115f;
            M3.IconButton("PrevClipBtn", m_PlayerContainer, "chevron_left", M3Button.Variant.Tonal,
                new Vector2(-76f, ctrlY), PrevClip, 48f);

            m_PlayPauseBtn = M3.IconButton("PlayPauseBtn", m_PlayerContainer, "pause", M3Button.Variant.Filled,
                new Vector2(0f, ctrlY), TogglePlayPause, M3.k_ButtonHeight);

            M3.IconButton("NextClipBtn", m_PlayerContainer, "skip_next", M3Button.Variant.Tonal,
                new Vector2(76f, ctrlY), NextClip, 48f);

            // Back to the app list (its own row, below the controls and above the grab handle)
            M3.Button("HomeBarBtn", m_PlayerContainer, "Pulpit aplikacji", M3Button.Variant.Text,
                new Vector2(0f, -180f), ShowHomeScreen, "home", 170f);

            UpdateClipDisplay();
            WorkspaceSounds.Attach(this);
        }

        // ---- Real video: clips the user copied to the phone ----
        // iPhone: the app's Documents/Klipy folder (Finder's Files tab or the Files app can copy into it,
        // since the app shares Documents). Vertical clips fit best. With none there, the feed apps show
        // their sample texts as before. The sound goes through the phone's spatial AudioSource, so it
        // comes from where the phone floats (AirPods with head tracking).
        static readonly string[] k_VideoExtensions = { ".mp4", ".mov", ".m4v" };
        const float k_CardWidth = 248f;
        const float k_CardHeight = 310f;
        public static string ClipFolder => Path.Combine(Application.persistentDataPath, "Klipy");

        // Local files (Url set) and Google Drive links from Klipy/linki.txt (DriveId set, Url once downloaded)
        readonly List<VideoEntry> m_Clips = new();
        public const string k_LinksFileName = "linki.txt";
        bool m_Downloading;
        VideoPlayer m_Player;
        RenderTexture m_VideoTexture;
        RawImage m_VideoImage;

        bool ShowsVideo => m_Clips.Count > 0 && m_Mode == ViewMode.AppPlayer && m_CurrentApp.name != "Messenger" &&
                           m_CurrentApp.name != "Notatki";
        int ClipCount => ShowsVideo ? m_Clips.Count : (m_CurrentApp.sampleClips?.Length ?? 0);

        void FindVideos()
        {
            m_Clips.Clear();
            try
            {
                Directory.CreateDirectory(ClipFolder);
                var files = new List<string>();
                foreach (var file in Directory.GetFiles(ClipFolder))
                {
                    if (Array.IndexOf(k_VideoExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                        files.Add(file);
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                    m_Clips.Add(new VideoEntry { Title = Path.GetFileNameWithoutExtension(file), Url = file });
                AddDriveLinks(Path.Combine(ClipFolder, k_LinksFileName));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpatialPhone] Nie można odczytać {ClipFolder}: {e.Message}");
            }
            Debug.Log($"[SpatialPhone] Klipy wideo: {m_Clips.Count} w {ClipFolder}");
        }

        // Klipy/linki.txt: one Google Drive link per line (shared as "Anyone with the link"), optionally
        // followed by a name; lines starting with # are skipped. Downloaded once, by the Video demo's
        // DriveDownloader, into the app's cache.
        void AddDriveLinks(string listFile)
        {
            if (!File.Exists(listFile))
            {
                File.WriteAllText(listFile,
                    "# Linki do filmów z Google Drive (udostępnione: \"Każdy, kto ma link\"), jeden w linii,\n" +
                    "# opcjonalnie z nazwą po spacji, np.:\n# https://drive.google.com/file/d/ABC123/view Mój klip\n");
                return;
            }
            foreach (var raw in File.ReadAllLines(listFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                int space = line.IndexOf(' ');
                string link = space > 0 ? line.Substring(0, space) : line;
                string name = space > 0 ? line.Substring(space + 1).Trim() : null;
                if (!DriveLink.TryGetFileId(link, out var id))
                    continue;
                m_Clips.Add(new VideoEntry
                {
                    // DriveDownloader takes the file's own name for titles that start like this
                    Title = string.IsNullOrEmpty(name) ? "Drive video " + id : name,
                    DriveId = id,
                    Url = VideoLibrary.CachedFile(id),
                });
            }
        }

        IEnumerator DownloadThenPlay(VideoEntry entry, int index)
        {
            m_Downloading = true;
            yield return DriveDownloader.Download(entry, progress =>
            {
                if (m_StatusText != null && ShowsVideo && m_CurrentClipIndex == index)
                    m_StatusText.text = $"Pobieranie z Drive {progress * 100f:F0}%";
            });
            m_Downloading = false;
            // Still on this clip? Play it, or say why not
            if (!ShowsVideo || m_CurrentClipIndex != index)
                yield break;
            if (entry.Url != null)
                PlayVideo(index);
            else if (m_StatusText != null)
                m_StatusText.text = "<noparse>" + (entry.Status ?? "Nie pobrano") + "</noparse>";
        }

        void PlayVideo(int index)
        {
            if (m_Player == null)
            {
                m_Player = gameObject.AddComponent<VideoPlayer>();
                m_Player.playOnAwake = false;
                m_Player.isLooping = true;
                m_Player.renderMode = VideoRenderMode.RenderTexture;
                m_Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                m_Player.controlledAudioTrackCount = 1;
                m_Player.SetTargetAudioSource(0, m_SpatialAudio);
                m_Player.prepareCompleted += OnVideoPrepared;
                m_Player.errorReceived += (_, message) =>
                {
                    Debug.LogWarning($"[SpatialPhone] Błąd wideo: {message}");
                    if (m_StatusText != null)
                        m_StatusText.text = "Nie można odtworzyć tego pliku";
                };
            }
            var clip = m_Clips[index];
            if (clip.NeedsDownload)
            {
                m_Player.Stop();
                if (m_VideoImage != null)
                    m_VideoImage.enabled = false;
                if (!m_Downloading)
                    StartCoroutine(DownloadThenPlay(clip, index));
                else if (m_StatusText != null)
                    m_StatusText.text = "Czekam na poprzednie pobieranie...";
                return;
            }
            m_Player.Stop();
            m_Player.source = VideoSource.Url;
            m_Player.url = clip.Url;
            m_Player.EnableAudioTrack(0, true);
            m_Player.Prepare();
            if (m_StatusText != null)
                m_StatusText.text = "<noparse>" + clip.Title + "</noparse>";
        }

        void OnVideoPrepared(VideoPlayer player)
        {
            int width = (int)player.width, height = (int)player.height;
            if (width <= 0 || height <= 0)
                return;
            // At most 1080 px on the long side: the card is small, and the phone renders twice (stereo).
            float shrink = Mathf.Min(1f, 1080f / Mathf.Max(width, height));
            int texWidth = Mathf.Max(2, (int)(width * shrink)), texHeight = Mathf.Max(2, (int)(height * shrink));
            if (m_VideoTexture == null || m_VideoTexture.width != texWidth || m_VideoTexture.height != texHeight)
            {
                if (m_VideoTexture != null)
                    m_VideoTexture.Release();
                m_VideoTexture = new RenderTexture(texWidth, texHeight, 0);
            }
            player.targetTexture = m_VideoTexture;
            if (m_VideoImage != null)
            {
                m_VideoImage.texture = m_VideoTexture;
                m_VideoImage.uvRect = CropToFill(width / (float)height, k_CardWidth / k_CardHeight);
                m_VideoImage.enabled = true;
            }
            if (m_IsPlaying)
                player.Play();
        }

        // The part of a video of aspect videoAspect that fills a frame of aspect frameAspect (cropping
        // the sides or the top and bottom, never stretching).
        public static Rect CropToFill(float videoAspect, float frameAspect)
        {
            if (videoAspect > frameAspect)
            {
                float w = frameAspect / videoAspect;
                return new Rect((1f - w) / 2f, 0f, w, 1f);
            }
            float h = videoAspect / frameAspect;
            return new Rect(0f, (1f - h) / 2f, 1f, h);
        }

        void StopVideo()
        {
            if (m_Player != null)
                m_Player.Stop();
            if (m_VideoImage != null)
                m_VideoImage.enabled = false;
        }

        void OnDestroy()
        {
            if (m_VideoTexture != null)
            {
                m_VideoTexture.Release();
                Destroy(m_VideoTexture);
            }
        }

        public void NextClip()
        {
            int count = ClipCount;
            if (count == 0)
                return;
            m_CurrentClipIndex = (m_CurrentClipIndex + 1) % count;
            UpdateClipDisplay();
        }

        public void PrevClip()
        {
            int count = ClipCount;
            if (count == 0)
                return;
            m_CurrentClipIndex = (m_CurrentClipIndex - 1 + count) % count;
            UpdateClipDisplay();
        }

        public void TogglePlayPause()
        {
            m_IsPlaying = !m_IsPlaying;
            if (m_Player != null && ShowsVideo)
            {
                if (m_IsPlaying) m_Player.Play();
                else m_Player.Pause();
            }
            // An icon button has no Label (it is null), so set the icon through M3Button.Icon.
            if (m_PlayPauseBtn != null)
                m_PlayPauseBtn.Icon = m_IsPlaying ? "pause" : "play_arrow";
            if (m_StatusText != null)
                m_StatusText.text = m_IsPlaying ? "Odtwarzanie..." : "Wstrzymano";
        }

        void UpdateClipDisplay()
        {
            if (m_ClipContentText != null && m_CurrentApp.sampleClips != null)
            {
                if (m_CurrentApp.name == "Messenger")
                {
                    // <noparse>: typed or dictated "<" must show as text, not start a TMP tag
                    string history = "<noparse>" + string.Join("\n", m_CurrentApp.sampleClips).Replace("</noparse>", "") + "</noparse>";
                    string draftLine = string.IsNullOrEmpty(m_ChatDraft)
                        ? "<color=#888888>Pisz na klawiaturze biurkowej...</color>"
                        : $"<b>Ty:</b> <noparse>{m_ChatDraft.Replace("</noparse>", "")}</noparse><color=#0084ff>|</color>";
                    m_ClipContentText.text = $"<b>[Czat Messenger]</b>\n\n{history}\n\n--------------------\n{draftLine}";
                    ShowLastPage(m_ClipContentText);
                    if (m_StatusText != null)
                        m_StatusText.text = "Pisz na klawiaturze | Enter wysyła wiadomość";
                }
                else if (ShowsVideo)
                {
                    m_ClipContentText.text = "";
                    PlayVideo(m_CurrentClipIndex);
                }
                else
                {
                    string clip = m_CurrentApp.sampleClips[m_CurrentClipIndex];
                    m_ClipContentText.text = $"<b>[{m_CurrentApp.name} #{m_CurrentClipIndex + 1}]</b>\n\n{clip}";
                    m_ClipContentText.pageToDisplay = 1;
                    if (m_StatusText != null)
                        m_StatusText.text = "Gest: Swipe Up \u2191 nast\u0119pny film";
                }
            }
        }

        static void ShowLastPage(TMP_Text text)
        {
            text.ForceMeshUpdate();
            text.pageToDisplay = Mathf.Max(1, text.textInfo.pageCount);
        }

        public void HandleSwipeGesture(DynamicGesture gesture)
        {
            if (m_Mode != ViewMode.AppPlayer)
                return;

            if (gesture == DynamicGesture.SwipeUp)
            {
                NextClip();
                Debug.Log($"[SpatialPhone] SwipeUp -> Następny film w {m_CurrentApp.name}");
            }
            else if (gesture == DynamicGesture.SwipeDown)
            {
                PrevClip();
                Debug.Log($"[SpatialPhone] SwipeDown -> Poprzedni film w {m_CurrentApp.name}");
            }
        }

        void OnGrabClick()
        {
            if (m_IsDragging)
                StopDragging();
            else
                StartDragging();
        }

        public void StartDragging()
        {
            m_IsDragging = true;
            if (m_Head != null)
                m_DragOffset = Quaternion.Inverse(m_Head.rotation) * (transform.position - m_Head.position);
            if (m_GrabBtn != null)
                m_GrabBtn.Label.text = "Upuść telefon w tym miejscu";
        }

        public void StopDragging()
        {
            m_IsDragging = false;
            if (m_GrabBtn != null)
                m_GrabBtn.Label.text = "Przenieś smartfon";
        }

        void Update()
        {
            if (m_IsDragging && m_Head != null)
            {
                // Carried where it was in the view when the drag began, so its handle stays under the gaze.
                Vector3 targetPos = m_Head.position + m_Head.rotation * m_DragOffset;
                Quaternion targetRot = Quaternion.LookRotation(targetPos - m_Head.position, Vector3.up);

                transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * 10f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * 10f);
            }
        }
    }
}
