using System;
using System.Collections.Generic;
using CardboardXR.Interaction;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
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
                    "@future_design: Spatial Computing i pływające monitory 💻 #apple",
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

            BuildPhoneFrame();
            ShowHomeScreen();
        }

        void BuildPhoneFrame()
        {
            // Top Dynamic Island & Status Bar
            float topY = (k_PhoneHeight / 2f) - 20f;
            string timeStr = DateTime.Now.ToString("HH:mm");
            M3.Text("Clock", m_CanvasRect, timeStr, M3Theme.Type.LabelLarge,
                new Vector2(-80f, topY), new Vector2(80f, 24f), M3Theme.OnSurface, TextAlignmentOptions.Left);

            // Dynamic Island pill
            M3.Surface("DynamicIsland", m_CanvasRect, new Vector2(0f, topY), new Vector2(76f, 18f),
                Color.black, 9f);

            // Close button (top right)
            M3.IconButton("ClosePhone", m_CanvasRect, "close", M3Button.Variant.Text,
                new Vector2(105f, topY), () => { Closed?.Invoke(this); Destroy(gameObject); }, 28f);

            // Bottom Grab Handle Bar
            float grabY = -(k_PhoneHeight / 2f) + 20f;
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
            for (int i = 0; i < s_Apps.Length; i++)
            {
                if (s_Apps[i].name == m_CurrentApp.name)
                    s_Apps[i].sampleClips = m_CurrentApp.sampleClips;
            }
            m_CurrentClipIndex = m_CurrentApp.sampleClips.Length - 1;
            m_ChatDraft = "";
            UpdateClipDisplay();
        }

        public void ShowHomeScreen()
        {
            m_Mode = ViewMode.Home;
            m_IsChatFocused = false;
            m_HomeContainer.gameObject.SetActive(true);
            m_PlayerContainer.gameObject.SetActive(false);

            // Clear old home items
            foreach (Transform t in m_HomeContainer)
                Destroy(t.gameObject);

            // Greeting header
            M3.Text("HomeHeader", m_HomeContainer, "Twoje Aplikacje", M3Theme.Type.TitleMedium,
                new Vector2(0f, 175f), new Vector2(240f, 30f), M3Theme.OnSurface, TextAlignmentOptions.Center);
            M3.Text("HomeSub", m_HomeContainer, "Wybierz aplikację do podglądu", M3Theme.Type.BodyMedium,
                new Vector2(0f, 152f), new Vector2(240f, 18f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Center);

            // Grid of 6 App Icons (2 columns x 3 rows)
            float startY = 88f;
            float spacingY = 88f;
            float posX = 60f;

            for (int i = 0; i < s_Apps.Length; i++)
            {
                var app = s_Apps[i];
                float colX = (i % 2 == 0) ? -posX : posX;
                float rowY = startY - (i / 2) * spacingY;

                // App Icon Squircle Button
                M3.IconButton($"AppBtn_{app.name}", m_HomeContainer, app.icon, M3Button.Variant.Filled,
                    new Vector2(colX, rowY), () => LaunchApp(app), 50f);

                // App Label
                M3.Text($"Label_{app.name}", m_HomeContainer, app.name, M3Theme.Type.LabelLarge,
                    new Vector2(colX, rowY - 36f), new Vector2(100f, 20f), M3Theme.OnSurface, TextAlignmentOptions.Center);
            }
        }

        public void LaunchApp(SocialApp app)
        {
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
            float headerY = 185f;
            M3.IconButton("BackBtn", m_PlayerContainer, "chevron_left", M3Button.Variant.Text,
                new Vector2(-105f, headerY), ShowHomeScreen, 32f);

            m_PlayerTitleText = M3.Text("AppTitle", m_PlayerContainer, m_CurrentApp.name, M3Theme.Type.TitleMedium,
                new Vector2(0f, headerY + 4f), new Vector2(150f, 28f), m_CurrentApp.color, TextAlignmentOptions.Center);

            m_PlayerUrlText = M3.Text("AppUrl", m_PlayerContainer, m_CurrentApp.url, M3Theme.Type.BodyMedium,
                new Vector2(0f, headerY - 18f), new Vector2(220f, 18f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Center);

            // Simulated Video Display Card (Aspect 9:14 inside screen)
            M3.Surface("VideoScreenBg", m_PlayerContainer, new Vector2(0f, 10f),
                new Vector2(248f, 310f), new Color(0.14f, 0.15f, 0.18f), 20f);

            // Inner video content text
            m_ClipContentText = M3.Text("ClipContent", m_PlayerContainer, "", M3Theme.Type.BodyMedium,
                new Vector2(0f, 40f), new Vector2(220f, 140f), Color.white, TextAlignmentOptions.Center);
            // Long chats must not hide the draft line at the bottom: show the last page of the text.
            m_ClipContentText.overflowMode = TextOverflowModes.Page;

            // Overlay Video Info
            m_StatusText = M3.Text("StatusText", m_PlayerContainer, "Gest: Swipe Up \u2191 nast\u0119pny film", M3Theme.Type.BodyMedium,
                new Vector2(0f, -80f), new Vector2(220f, 30f), m_CurrentApp.color, TextAlignmentOptions.Center);

            // Playback controls row
            float ctrlY = -115f;
            M3.IconButton("PrevClipBtn", m_PlayerContainer, "chevron_left", M3Button.Variant.Tonal,
                new Vector2(-70f, ctrlY), PrevClip, 36f);

            m_PlayPauseBtn = M3.IconButton("PlayPauseBtn", m_PlayerContainer, "pause", M3Button.Variant.Filled,
                new Vector2(0f, ctrlY), TogglePlayPause, 44f);

            M3.IconButton("NextClipBtn", m_PlayerContainer, "skip_next", M3Button.Variant.Tonal,
                new Vector2(70f, ctrlY), NextClip, 36f);

            // Bottom Home Indicator Bar (Clicking it returns to Home)
            M3.Surface("HomeBar", m_PlayerContainer, new Vector2(0f, -170f), new Vector2(100f, 6f),
                M3Theme.Outline, 3f);
            M3.Button("HomeBarBtn", m_PlayerContainer, "Pulpit aplikacji", M3Button.Variant.Text,
                new Vector2(0f, -170f), ShowHomeScreen, "home", 140f);

            UpdateClipDisplay();
        }

        public void NextClip()
        {
            if (m_CurrentApp.sampleClips == null || m_CurrentApp.sampleClips.Length == 0)
                return;
            m_CurrentClipIndex = (m_CurrentClipIndex + 1) % m_CurrentApp.sampleClips.Length;
            UpdateClipDisplay();
        }

        public void PrevClip()
        {
            if (m_CurrentApp.sampleClips == null || m_CurrentApp.sampleClips.Length == 0)
                return;
            m_CurrentClipIndex = (m_CurrentClipIndex - 1 + m_CurrentApp.sampleClips.Length) % m_CurrentApp.sampleClips.Length;
            UpdateClipDisplay();
        }

        public void TogglePlayPause()
        {
            m_IsPlaying = !m_IsPlaying;
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
                    string history = string.Join("\n", m_CurrentApp.sampleClips);
                    string draftLine = string.IsNullOrEmpty(m_ChatDraft)
                        ? "<color=#888888>Pisz na klawiaturze biurkowej...</color>"
                        : $"<b>Ty:</b> {m_ChatDraft}<color=#0084ff>|</color>";
                    m_ClipContentText.text = $"<b>[Czat Messenger]</b>\n\n{history}\n\n--------------------\n{draftLine}";
                    ShowLastPage(m_ClipContentText);
                    if (m_StatusText != null)
                        m_StatusText.text = "Pisz na klawiaturze | Enter wysyła wiadomość";
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
                Vector3 targetPos = m_Head.position + m_Head.forward * m_Distance;
                Quaternion targetRot = Quaternion.LookRotation(targetPos - m_Head.position, Vector3.up);

                transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * 10f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * 10f);
            }
        }
    }
}
