using System;
using System.Collections.Generic;
using System.IO;
using CardboardXR.Demos;
using CardboardXR.Hands;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CardboardXR.Demos.Workspace
{
    // Spatial Workspace: Multi-window spatial computing environment.
    // Includes floating monitors, remote PC desktop streaming, TikTok/Messenger smartphone,
    // invisible desk keyboard and trackpad with physical haptics, AirPods Pro 2 3D spatial audio,
    // voice dictation, cinema dimming mode, and Bluetooth physical keyboard/mouse input.
    public class WorkspaceDemo : Demo, IPinchHandler
    {
        const float k_ScreenDistance = 1.4f;
        const float k_KeyboardDistance = 0.6f;

        // Layout, in degrees around the head (1 dp is about 0.08 degrees at any distance, M3Theme):
        //   a 520 x 440 dp screen is about 41 x 35 deg, the phone 22 x 45 deg, the keyboard 51 x 19 deg,
        //   the trackpad 14 x 10 deg, the dock about 57 x 7 deg. Nothing may overlap, or the nearer panel
        //   takes the gaze (GazeCanvas picks the nearest hit) and hides the other's buttons.
        //   screens   pitch +3, 46 deg apart (41 wide + 5 gap); a second row 37 deg higher
        //   phone     yaw +40, between the first screen and the right-hand slot
        //   dock      pitch -20, under the screens
        //   keyboard  pitch -38, below the dock; trackpad at its right
        // The hand camera only sees +-21 deg up/down around where the head points (KNOWN_ISSUES.md), so
        // fingertip typing works while looking down at the keyboard, not while looking at a screen;
        // for blind typing use a Bluetooth keyboard.
        static readonly float[] k_ScreenSlotYaws = { 0f, -46f, -92f, 86f, 132f };
        const float k_ScreenPitch = 3f;
        const float k_ScreenRowPitch = 37f;
        const float k_PhoneYaw = 54f; // clear of the 10-button dock (~±41 deg) and of the +86 deg slot (from 65 deg)
        const float k_PhoneDistance = 1.25f;
        const float k_DockPitch = -20f;
        const float k_DeskPitch = -38f;
        // At 38 deg down, yaw turns less than its value: 44 deg of yaw is ~34 deg of arc, enough to
        // clear the keyboard's half width (25.5) plus half the trackpad (7) and a gap.
        const float k_TrackpadYaw = 44f;

        // Where "ahead" was when the demo started: screens keep their slots around it.
        Quaternion m_HomeYaw = Quaternion.identity;
        readonly Dictionary<VirtualScreen, int> m_ScreenSlots = new();

        CoreServices m_Core;
        readonly List<VirtualScreen> m_Screens = new();
        VirtualScreen m_ActiveScreen;
        DeskKeyboard m_Keyboard;
        DeskTrackpad m_Trackpad;
        SpatialPhoneScreen m_PhoneScreen;
        VoiceDictation m_Voice;
        CinemaDimmer m_Cinema;
        RectTransform m_ControlDock;
        SpatialGestureDetector m_Gestures;
        M3Button m_VoiceBtn;
        M3Button m_CinemaBtn;
        M3Button m_LanguageBtn;
        bool m_GroupDrag; // a held fist is carrying all the windows
        WorkspaceAnchors m_RoomAnchors;
        M3Button m_DeskBtn;
        bool m_PlacingDesk; // "Na blat": the next pinch puts the keyboard on the table being looked at

        // Detected real surfaces: the base project's plane prefab is on layer 30 (StereoBuilder.BoundaryLayer),
        // with a BoxCollider whose top face is the surface. Only horizontal planes are detected.
        const int k_SurfaceLayer = 30;
        const float k_DeskMinBelowEyes = 0.15f; // a table is between 15 and 95 cm below the eyes...
        const float k_DeskMaxBelowEyes = 0.95f; // ...the floor is farther down
        const float k_DeskReach = 1.5f;
        bool m_WorkspaceVisible = true;
        AudioListener m_AddedListener;

        // A pose acts only after it has been held for k_PoseHoldSeconds, once per hold: the hand
        // tracker's pose flickers, and a toggle on every change hid and showed the dock at random.
        const float k_PoseHoldSeconds = 0.5f;
        HandPose m_HeldPose = HandPose.None;
        float m_HeldPoseSince;
        bool m_HeldPoseHandled;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            PinchRouter.Handler = this;
            m_HomeYaw = FlatYaw();
            m_RoomAnchors = gameObject.AddComponent<WorkspaceAnchors>();
            m_RoomAnchors.Initialize(transform);

            // Nothing in the app plays sound, so no scene has an AudioListener, and without one Unity
            // plays nothing: the key and trackpad clicks and the windows' spatial audio would be silent.
            // The listener goes on the head, so 3D sounds follow where the user looks (and AirPods
            // head tracking can do the rest).
            if (FindFirstObjectByType<AudioListener>() == null)
                m_AddedListener = m_Core.Head.gameObject.AddComponent<AudioListener>();

            // Cinema Dimmer
            m_Cinema = gameObject.AddComponent<CinemaDimmer>();
            m_Cinema.Initialize(m_Core.Head);

            // Voice Dictation
            m_Voice = gameObject.AddComponent<VoiceDictation>();
            m_Voice.SpeechRecognized += OnSpeechRecognized;
            m_Voice.Failed += OnVoiceFailed;
            m_Voice.PartialRecognized += OnVoicePartial;
            m_Voice.RecordingStarted += OnVoiceStarted;
            m_Voice.RecordingEnded += OnVoiceEnded;

            BuildControlDock();
            SpawnKeyboardAndTrackpad();
            // The windows of the last session, or a first notepad and the phone
            if (!RestoreWorkspace())
            {
                CreateNewScreen("Główny Notatnik");
                SpawnPhoneScreen();
                if (!File.Exists(SaveFile))
                    OpenTutorial(); // the very first time
            }

            // Attach spatial gesture detector
            m_Gestures = gameObject.AddComponent<SpatialGestureDetector>();
            m_Gestures.Initialize(m_Core.HandTracker, m_Core.Head);
            m_Gestures.GestureDetected += OnDynamicGesture;
            m_Gestures.PoseChanged += OnHandPoseChanged;

            Debug.Log("[Workspace] Spatial Workspace uruchomiony. Ekrany, gładzik, dźwięk 3D, dyktowanie i BT gotowe.");
        }

        public override void OnDemoExit(CoreServices core)
        {
            if (PinchRouter.Handler == (IPinchHandler)this)
                PinchRouter.Handler = null;

            SaveWorkspace();

            // Anchored panels are children of anchors in the Core scene: bring them home first, or they
            // would outlive the demo
            if (m_RoomAnchors != null)
            {
                m_RoomAnchors.ReleaseAll();
                Destroy(m_RoomAnchors);
            }

            if (m_Gestures != null)
            {
                m_Gestures.GestureDetected -= OnDynamicGesture;
                m_Gestures.PoseChanged -= OnHandPoseChanged;
                Destroy(m_Gestures);
            }

            foreach (var screen in m_Screens)
            {
                if (screen != null)
                    Destroy(screen.gameObject);
            }
            m_Screens.Clear();
            m_ScreenSlots.Clear();

            if (m_PhoneScreen != null)
                Destroy(m_PhoneScreen.gameObject);

            if (m_Keyboard != null)
                Destroy(m_Keyboard.gameObject);

            if (m_Trackpad != null)
                Destroy(m_Trackpad.gameObject);

            if (m_ControlDock != null)
                Destroy(m_ControlDock.gameObject);

            SetCinemaScreen(false);

            WorkspaceSounds.Release();
            if (m_AddedListener != null)
                Destroy(m_AddedListener);

            // These components sit on this demo's own GameObject: destroy the components, not the object.
            if (m_Cinema != null)
                Destroy(m_Cinema);

            if (m_Voice != null)
            {
                m_Voice.SpeechRecognized -= OnSpeechRecognized;
                m_Voice.Failed -= OnVoiceFailed;
                m_Voice.PartialRecognized -= OnVoicePartial;
                m_Voice.RecordingStarted -= OnVoiceStarted;
                m_Voice.RecordingEnded -= OnVoiceEnded;
                Destroy(m_Voice);
            }
        }

        void BuildControlDock()
        {
            // Floating control dock below eye level (pitch -22 deg, distance 1.2m). The buttons are as
            // wide as their labels need (no icons, to keep the dock narrow), 8 dp apart as the Material
            // kit recommends, and the panel is as wide as they are, plus a 16 dp margin each side.
            const float gap = M3Theme.k_TargetGap;
            const float margin = 16f;
            string[] labels = { "Ekran", "PC", "Aplikacje", "Telefon", "Głos", "PL", "Kino", "Na blat", "Wyśrodkuj", "?" };
            var widths = new float[labels.Length];
            float total = gap * (labels.Length - 1);
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = M3.ButtonWidth(labels[i], null);
                total += widths[i];
            }

            m_ControlDock = WorldUi.Panel("Workspace Dock", transform, new Vector2(total + margin * 2f, M3.k_ButtonHeight + margin * 2f),
                1.2f, M3Theme.SurfaceContainerHighest, 36f);

            float x = -total / 2f;
            M3Button Add(int i, string name, M3Button.Variant variant, UnityEngine.Events.UnityAction onClick)
            {
                var button = M3.Button(name, m_ControlDock, labels[i], variant, new Vector2(x + widths[i] / 2f, 0f), onClick, null, widths[i]);
                x += widths[i] + gap;
                return button;
            }

            Add(0, "NewScreenBtn", M3Button.Variant.Filled, () => CreateNewScreen($"Ekran {m_Screens.Count + 1}"));
            Add(1, "PcScreenBtn", M3Button.Variant.Tonal, CreatePcDesktopScreen);
            Add(2, "AppScreenBtn", M3Button.Variant.Tonal, CreateAppScreen);
            Add(3, "NewPhoneBtn", M3Button.Variant.Filled, SpawnPhoneScreen);
            m_VoiceBtn = Add(4, "VoiceBtn", M3Button.Variant.Outlined, ToggleVoiceDictation);
            m_LanguageBtn = Add(5, "LanguageBtn", M3Button.Variant.Text, ToggleDictationLanguage);
            m_CinemaBtn = Add(6, "CinemaBtn", M3Button.Variant.Outlined, ToggleCinemaMode);
            m_DeskBtn = Add(7, "DeskBtn", M3Button.Variant.Tonal, BeginDeskPlacement);
            Add(8, "RecenterBtn", M3Button.Variant.Tonal, RecenterAll);
            Add(9, "HelpBtn", M3Button.Variant.Text, OpenTutorial);

            WorkspaceSounds.Attach(m_ControlDock);
            PlaceAround(m_ControlDock, 1.2f, 0f, k_DockPitch);
        }

        void SpawnKeyboardAndTrackpad()
        {
            // Keyboard
            m_Keyboard = DeskKeyboard.Create(transform, m_Core.HandTracker, k_KeyboardDistance);

            // Trackpad
            m_Trackpad = DeskTrackpad.Create(transform, m_Core.HandTracker, m_ActiveScreen, k_KeyboardDistance);

            ResetDeskPosition();

            // Hook up typing events
            m_Keyboard.KeyPressed += OnKeyPressed;
            m_Keyboard.BackspacePressed += OnBackspacePressed;
            m_Keyboard.EnterPressed += OnEnterPressed;
            m_Keyboard.ClearPressed += OnClearPressed;

            // The trackpad's taps click what is under the active screen's cursor
            m_Trackpad.LeftClicked += () => { if (m_ActiveScreen != null) m_ActiveScreen.ClickAtCursor(); };
            m_Trackpad.RightClicked += () => { if (m_ActiveScreen != null) m_ActiveScreen.ContextClickAtCursor(); };
        }

        void ResetDeskPosition()
        {
            if (m_Keyboard == null || m_Core == null)
                return;
            if (m_PlacingDesk)
                EndDeskPlacement();

            // Brings the keyboard and trackpad back (also after the STOP gesture hid them) under the
            // current heading, each facing the eyes.
            SetDeskVisible(true);
            if (m_RoomAnchors != null)
            {
                m_RoomAnchors.Release(m_Keyboard.transform);
                if (m_Trackpad != null)
                    m_RoomAnchors.Release(m_Trackpad.transform);
            }
            WorldUi.PlaceInFront((RectTransform)m_Keyboard.transform, m_Core.Head, k_KeyboardDistance, k_DeskPitch, 0f);
            if (m_Trackpad != null)
                WorldUi.PlaceInFront((RectTransform)m_Trackpad.transform, m_Core.Head, k_KeyboardDistance, k_DeskPitch, k_TrackpadYaw);
        }

        // ---- "Na blat": the keyboard and trackpad lie flat on a real, detected table ----
        // Then the fingertip taps the table itself: it stops the finger (real "haptics"), and a tap is
        // the fingertip coming within 3.5 cm of the keyboard plane, which now is the table top.

        void BeginDeskPlacement()
        {
            if (m_Keyboard == null)
                return;
            m_PlacingDesk = true;
            // Hidden while choosing, so a pinch at the table can't land on a floating key instead
            SetDeskVisible(false);
            if (m_DeskBtn != null)
            {
                m_DeskBtn.Text = "Wskaż stół";
                m_DeskBtn.Selected = true;
            }
        }

        void EndDeskPlacement()
        {
            m_PlacingDesk = false;
            if (m_DeskBtn != null)
            {
                m_DeskBtn.Text = "Na blat";
                m_DeskBtn.Selected = false;
            }
        }

        // The table under the gaze, if there is a detected one at desk height within reach.
        bool TryFindDesk(out Vector3 point)
        {
            point = default;
            var head = m_Core.Head;
            if (!Physics.Raycast(head.position, head.forward, out var hit, k_DeskReach, 1 << k_SurfaceLayer,
                    QueryTriggerInteraction.Ignore))
                return false;
            float below = head.position.y - hit.point.y;
            if (hit.normal.y < 0.9f || below < k_DeskMinBelowEyes || below > k_DeskMaxBelowEyes)
                return false;
            point = hit.point;
            return true;
        }

        void PlaceDeskAtGaze()
        {
            EndDeskPlacement();
            if (!TryFindDesk(out var point))
            {
                ResetDeskPosition(); // back to floating in front
                if (m_ActiveScreen != null)
                    m_ActiveScreen.AppendText("\n[Nie znaleziono stołu: rozejrzyj się chwilę po blacie, by go wykryć, i spróbuj ponownie]\n");
                return;
            }

            SetDeskVisible(true);
            var keyboard = (RectTransform)m_Keyboard.transform;
            m_RoomAnchors?.Release(keyboard);
            // Flat, readable from above: the canvas faces down into the table (+Z), its top row away from the user
            Vector3 forward = FlatYaw() * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Quaternion flat = Quaternion.LookRotation(Vector3.down, forward);
            Vector3 lift = Vector3.up * 0.004f; // just above the plane's own mesh
            keyboard.SetPositionAndRotation(point + lift, flat);

            if (m_Trackpad != null)
            {
                var pad = (RectTransform)m_Trackpad.transform;
                m_RoomAnchors?.Release(pad);
                float keyboardHalf = keyboard.rect.width * keyboard.lossyScale.x / 2f;
                float padHalf = pad.rect.width * pad.lossyScale.x / 2f;
                pad.SetPositionAndRotation(point + lift + right * (keyboardHalf + 0.03f + padHalf), flat);
            }
            Debug.Log($"[Workspace] Klawiatura na blacie {m_Core.Head.position.y - point.y:F2} m pod oczami");
        }

        // Like holding the Digital Crown on Vision Pro: everything comes back around where the user now
        // looks, each window to its slot (pinned ones too), the phone, the dock and the desk; drags end.
        void RecenterAll()
        {
            if (m_Core == null)
                return;
            if (m_GroupDrag)
                SetGroupDrag(false);
            m_HomeYaw = FlatYaw();
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                screen.StopDragging();
                int slot = m_ScreenSlots.TryGetValue(screen, out var s) ? s : 0;
                PlaceAround(screen.transform, k_ScreenDistance, k_ScreenSlotYaws[slot % k_ScreenSlotYaws.Length],
                    k_ScreenPitch + (slot / k_ScreenSlotYaws.Length) * k_ScreenRowPitch);
            }
            if (m_PhoneScreen != null)
            {
                m_PhoneScreen.StopDragging();
                PlaceAround(m_PhoneScreen.transform, k_PhoneDistance, k_PhoneYaw, 0f);
            }
            if (m_ControlDock != null)
                PlaceAround(m_ControlDock, 1.2f, 0f, k_DockPitch);
            ResetDeskPosition();
        }

        // Puts a panel at yaw/pitch degrees from the demo's home heading, facing the head.
        void PlaceAround(Transform panel, float distance, float yawDegrees, float pitchDegrees)
        {
            if (m_RoomAnchors != null)
                m_RoomAnchors.Release(panel);
            Vector3 position = m_Core.Head.position +
                               m_HomeYaw * Quaternion.Euler(-pitchDegrees, yawDegrees, 0f) * Vector3.forward * distance;
            panel.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_Core.Head.position, Vector3.up));
        }

        // The head's heading on the floor plane; safe when looking straight up or down.
        Quaternion FlatYaw()
        {
            Vector3 flat = Vector3.ProjectOnPlane(m_Core.Head.forward, Vector3.up);
            return flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized) : Quaternion.identity;
        }

        void SpawnPhoneScreen()
        {
            if (m_PhoneScreen != null)
                return;

            m_PhoneScreen = SpatialPhoneScreen.Create(transform, m_Core.Head, Vector3.zero, Quaternion.identity, k_PhoneDistance);
            PlaceAround(m_PhoneScreen.transform, k_PhoneDistance, k_PhoneYaw, 0f);
            m_PhoneScreen.Closed += _ => m_PhoneScreen = null;
        }

        public VirtualScreen CreateNewScreen(string title) => CreateNewScreen(title, -1);

        // slot: where to put it (from a saved workspace), or -1 for the first free one
        VirtualScreen CreateNewScreen(string title, int slot)
        {
            if (m_Core == null)
                return null;

            // The first free slot: left and right of the first screen, then a row above.
            if (slot < 0 || m_ScreenSlots.ContainsValue(slot))
            {
                slot = 0;
                while (m_ScreenSlots.ContainsValue(slot))
                    slot++;
            }
            float yaw = k_ScreenSlotYaws[slot % k_ScreenSlotYaws.Length];
            float pitch = k_ScreenPitch + (slot / k_ScreenSlotYaws.Length) * k_ScreenRowPitch;

            var screen = VirtualScreen.Create(transform, m_Core.Head, title, Vector3.zero, Quaternion.identity, k_ScreenDistance);
            PlaceAround(screen.transform, k_ScreenDistance, yaw, pitch);

            screen.Focused += SetActiveScreen;
            screen.Closed += OnScreenClosed;

            m_Screens.Add(screen);
            m_ScreenSlots[screen] = slot;
            SetActiveScreen(screen);
            return screen;
        }

        // ---- Saved workspace: Documents/workspace.json ----
        // The windows (title, notes, PC or not, slot, size, pin), whether the phone was open and the
        // dictation language, written when the demo closes or the app goes to the background, and
        // restored around the user the next time. Slots, not room positions: those would need ARKit's
        // ARWorldMap.

        [Serializable]
        class SavedScreen
        {
            public string title;
            public string text;
            public bool pc;
            public string app; // with pc: one app's window on the computer
            public bool browser;
            public string url; // with browser: the page
            public int slot;
            public float size = 1f;
            public bool pinned;
        }

        [Serializable]
        class SavedWorkspace
        {
            public List<SavedScreen> screens = new();
            public bool phone;
            public string locale;
        }

        static string SaveFile => Path.Combine(Application.persistentDataPath, "workspace.json");

        void SaveWorkspace()
        {
            if (m_Core == null)
                return;
            var saved = new SavedWorkspace { phone = m_PhoneScreen != null, locale = m_Voice != null ? m_Voice.Locale : null };
            foreach (var screen in m_Screens)
            {
                if (screen == null || screen.GetComponent<TutorialPanel>() != null || screen.GetComponent<AppPicker>() != null)
                    continue;
                saved.screens.Add(new SavedScreen
                {
                    title = screen.Title,
                    text = screen.IsRemoteDesktop || screen.IsBrowser ? "" : screen.Text,
                    pc = screen.IsRemoteDesktop,
                    app = screen.RemoteApp,
                    browser = screen.IsBrowser,
                    url = screen.IsBrowser ? screen.Browser.Url : null,
                    slot = m_ScreenSlots.TryGetValue(screen, out var slot) ? slot : -1,
                    size = screen.Size,
                    pinned = screen.Pinned,
                });
            }
            try
            {
                File.WriteAllText(SaveFile, JsonUtility.ToJson(saved, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] Nie zapisano układu: {e.Message}");
            }
        }

        bool RestoreWorkspace()
        {
            SavedWorkspace saved;
            try
            {
                if (!File.Exists(SaveFile))
                    return false;
                saved = JsonUtility.FromJson<SavedWorkspace>(File.ReadAllText(SaveFile));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] Nie wczytano układu: {e.Message}");
                return false;
            }
            if (saved == null || saved.screens == null || saved.screens.Count == 0)
                return false;

            foreach (var item in saved.screens)
            {
                var screen = CreateNewScreen(string.IsNullOrEmpty(item.title) ? "Ekran" : item.title, item.slot);
                if (screen == null)
                    continue;
                if (item.browser)
                    screen.EnableBrowser(string.IsNullOrEmpty(item.url) ? null : item.url);
                else if (item.pc)
                    screen.EnableRemoteDesktop(null, string.IsNullOrEmpty(item.app) ? null : item.app);
                else
                    screen.SetText(item.text);
                if (item.size > 0f && !Mathf.Approximately(item.size, 1f))
                    screen.SetSize(item.size);
                screen.SetPinned(item.pinned);
            }
            if (saved.phone)
                SpawnPhoneScreen();
            if (m_Voice != null && !string.IsNullOrEmpty(saved.locale))
            {
                m_Voice.Locale = saved.locale;
                if (m_LanguageBtn != null)
                    m_LanguageBtn.Text = saved.locale == "en-US" ? "EN" : "PL";
            }
            Debug.Log($"[Workspace] Przywrócono układ: {saved.screens.Count} okien");
            return true;
        }

        // iOS may end a backgrounded app without OnDemoExit: save when going to the background
        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveWorkspace();
        }

        // The tutorial (Mac and iPad screens) in a window; only one at a time
        public void OpenTutorial()
        {
            foreach (var existing in m_Screens)
            {
                if (existing != null && existing.GetComponent<TutorialPanel>() != null)
                {
                    SetActiveScreen(existing);
                    return;
                }
            }
            var screen = CreateNewScreen("Samouczek");
            if (screen != null)
                screen.gameObject.AddComponent<TutorialPanel>().Initialize(screen, screen.Rect);
        }

        // A web browser window running on the iPhone itself (BrowserView)
        public void CreateBrowserScreen(string url = null)
        {
            var screen = CreateNewScreen("Przeglądarka");
            if (screen != null)
                screen.EnableBrowser(url);
        }

        // A screen that lists the computer's apps; the one picked becomes this screen (AppPicker)
        public void CreateAppScreen()
        {
            var screen = CreateNewScreen("Wybierz aplikację");
            if (screen != null)
                screen.gameObject.AddComponent<AppPicker>().Initialize(screen, screen.Rect);
        }

        public void CreatePcDesktopScreen()
        {
            CreateNewScreen("Pulpit PC (Monitor 4K)");
            if (m_ActiveScreen != null)
            {
                m_ActiveScreen.EnableRemoteDesktop(); // address from Documents/pulpit.txt
            }
        }

        void SetActiveScreen(VirtualScreen screen)
        {
            m_ActiveScreen = screen;
            if (m_Trackpad != null)
                m_Trackpad.SetTargetScreen(screen);
        }

        void OnScreenClosed(VirtualScreen screen)
        {
            m_Screens.Remove(screen);
            m_ScreenSlots.Remove(screen);
            if (m_ActiveScreen == screen)
                SetActiveScreen(m_Screens.Count > 0 ? m_Screens[m_Screens.Count - 1] : null);
        }

        void OnKeyPressed(char c)
        {
            if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
                m_PhoneScreen.AppendChatChar(c);
            else if (m_ActiveScreen != null)
                m_ActiveScreen.AppendChar(c);
        }

        void OnBackspacePressed()
        {
            if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
                m_PhoneScreen.ChatBackspace();
            else if (m_ActiveScreen != null)
                m_ActiveScreen.Backspace();
        }

        void OnEnterPressed()
        {
            if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
                m_PhoneScreen.SendChatMessage();
            else if (m_ActiveScreen != null)
                m_ActiveScreen.AppendNewline();
        }

        void OnClearPressed()
        {
            if (m_ActiveScreen != null)
                m_ActiveScreen.Clear();
        }

        void ClearActiveScreen()
        {
            if (m_ActiveScreen != null)
                m_ActiveScreen.Clear();
        }

        void OnVoiceStarted()
        {
            if (m_VoiceBtn != null)
                m_VoiceBtn.Selected = true;
        }

        // While talking, the Voice button shows the end of what has been heard so far
        void OnVoicePartial(string text)
        {
            if (m_VoiceBtn == null || string.IsNullOrEmpty(text))
                return;
            const int max = 12;
            m_VoiceBtn.Text = text.Length > max ? "…" + text.Substring(text.Length - max) : text;
        }

        void OnVoiceEnded()
        {
            if (m_VoiceBtn != null)
            {
                m_VoiceBtn.Selected = false;
                m_VoiceBtn.Text = "Głos";
            }
        }

        // Dictation language: Polish or English (the button shows the current one)
        void ToggleDictationLanguage()
        {
            if (m_Voice == null || m_Voice.IsRecording)
                return;
            bool english = m_Voice.Locale == "en-US";
            m_Voice.Locale = english ? VoiceDictation.k_DefaultLocale : "en-US";
            if (m_LanguageBtn != null)
                m_LanguageBtn.Text = english ? "PL" : "EN";
        }

        void ToggleVoiceDictation()
        {
            if (m_Voice != null)
                m_Voice.ToggleRecording();
        }

        void OnSpeechRecognized(string transcribedText)
        {
            if (string.IsNullOrWhiteSpace(transcribedText))
                return;
            if (!VoiceDictation.IsMock && TryVoiceCommand(transcribedText))
                return;

            // Outside iOS nothing is recognized (a canned sentence comes back), so say so in the text.
            if (VoiceDictation.IsMock)
                transcribedText = "[głos-demo] " + transcribedText;

            if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
            {
                foreach (char c in transcribedText)
                    m_PhoneScreen.AppendChatChar(c);
            }
            else if (m_ActiveScreen != null)
            {
                string text = m_ActiveScreen.Text;
                bool atLineStart = string.IsNullOrEmpty(text) || text.EndsWith("\n");
                m_ActiveScreen.AppendText((atLineStart ? "" : " ") + transcribedText);
            }
        }

        // Voice commands. Only a whole utterance that is just the command counts (said on its own), so
        // the same words inside a dictated sentence are typed as usual.
        bool TryVoiceCommand(string spoken)
        {
            string command = spoken.Trim().TrimEnd('.', '!', '?', ',').ToLowerInvariant();
            switch (command)
            {
                case "nowy ekran": case "nowy monitor": case "new screen":
                    CreateNewScreen($"Ekran {m_Screens.Count + 1}");
                    break;
                case "wyczyść": case "wyczyść ekran": case "clear":
                    ClearActiveScreen();
                    break;
                case "tryb kinowy": case "kino": case "cinema":
                    ToggleCinemaMode();
                    break;
                case "pomoc": case "samouczek": case "help": case "tutorial":
                    OpenTutorial();
                    break;
                case "przeglądarka": case "internet": case "browser":
                    CreateBrowserScreen();
                    break;
                case "wstecz": case "back":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.GoBack();
                    break;
                case "odśwież": case "reload":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.DoReload();
                    break;
                case "aplikacje": case "aplikacja": case "apps":
                    CreateAppScreen();
                    break;
                case "telefon": case "smartfon": case "phone":
                    SpawnPhoneScreen();
                    break;
                case "reset biurka": case "klawiatura": case "keyboard":
                    ResetDeskPosition();
                    break;
                case "na blat": case "na biurko": case "na stół": case "desk":
                    // Spoken while looking at the table: place right away
                    m_PlacingDesk = true;
                    PlaceDeskAtGaze();
                    break;
                case "wyśrodkuj": case "wyśrodkuj wszystko": case "recenter":
                    RecenterAll();
                    break;
                case "następny ekran": case "next screen":
                    SelectNextScreen();
                    break;
                case "poprzedni ekran": case "previous screen":
                    SelectPrevScreen();
                    break;
                default:
                    return false;
            }
            Debug.Log($"[Workspace] Komenda głosowa: {command}");
            return true;
        }

        void OnVoiceFailed(string message)
        {
            if (m_VoiceBtn != null)
            {
                m_VoiceBtn.Selected = false;
                m_VoiceBtn.Text = "Głos";
            }
            if (m_ActiveScreen != null)
                m_ActiveScreen.AppendText($"\n[głos: {message}]\n");
        }

        void ToggleCinemaMode()
        {
            if (m_Cinema != null)
            {
                m_Cinema.ToggleCinemaMode();
                if (m_CinemaBtn != null)
                    m_CinemaBtn.Selected = m_Cinema.IsCinemaMode;
                SetCinemaScreen(m_Cinema.IsCinemaMode);
            }
        }

        // ---- Cinema screen: in cinema mode, the phone's clip also plays big in front of the user ----
        // 16:9, ~57 deg wide, 1.3 m away: in front of the windows (1.4 m) and the room dimmer (2.2 m).
        const float k_CinemaDistance = 1.3f;
        static readonly Vector2 k_CinemaSize = new(720f, 405f);
        RectTransform m_CinemaPanel;
        UnityEngine.UI.RawImage m_CinemaImage;
        TMPro.TextMeshProUGUI m_CinemaHint;

        void SetCinemaScreen(bool on)
        {
            if (!on)
            {
                if (m_CinemaPanel != null)
                    Destroy(m_CinemaPanel.gameObject);
                m_CinemaPanel = null;
                return;
            }
            if (m_CinemaPanel != null)
                return;
            m_CinemaPanel = WorldUi.Panel("Cinema Screen", transform, k_CinemaSize, k_CinemaDistance, Color.black, 12f);
            var imageRect = M3.Group("CinemaImage", m_CinemaPanel, Vector2.zero, k_CinemaSize);
            m_CinemaImage = imageRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            m_CinemaImage.raycastTarget = false;
            m_CinemaImage.enabled = false;
            m_CinemaHint = M3.Text("CinemaHint", m_CinemaPanel, "Włącz klip w telefonie (TikTok, Shorts, Reels, Twitch)",
                M3Theme.Type.BodyLarge, Vector2.zero, new Vector2(600f, 40f), M3Theme.OnSurfaceVariant);
            // In front of where the user looks now (not a slot: it's for watching, not working)
            WorldUi.PlaceInFront(m_CinemaPanel, m_Core.Head, k_CinemaDistance, k_ScreenPitch, 0f);
        }

        void UpdateCinemaScreen()
        {
            if (m_CinemaPanel == null)
                return;
            var texture = m_PhoneScreen != null ? m_PhoneScreen.PlayingTexture : null;
            bool playing = texture != null;
            m_CinemaImage.enabled = playing;
            m_CinemaHint.gameObject.SetActive(!playing);
            if (playing && m_CinemaImage.texture != texture)
            {
                m_CinemaImage.texture = texture;
                m_CinemaImage.rectTransform.sizeDelta =
                    RemoteDesktopReceiver.FitInside(texture.width / (float)Mathf.Max(1, texture.height), k_CinemaSize);
            }
        }

        void OnDynamicGesture(DynamicGesture gesture)
        {
            if (!SwipesAllowed())
                return;

            if (gesture == DynamicGesture.SwipeRight)
            {
                SelectNextScreen();
                Debug.Log("[Workspace] Gest: SwipeRight -> Następny ekran");
            }
            else if (gesture == DynamicGesture.SwipeLeft)
            {
                SelectPrevScreen();
                Debug.Log("[Workspace] Gest: SwipeLeft -> Poprzedni ekran");
            }
            else if ((gesture == DynamicGesture.SwipeUp || gesture == DynamicGesture.SwipeDown) &&
                     m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
            {
                // Hand up = page moves up = read further down
                m_ActiveScreen.Browser.Scroll(gesture == DynamicGesture.SwipeUp ? 0.7f : -0.7f);
            }
            else if (gesture == DynamicGesture.SwipeUp || gesture == DynamicGesture.SwipeDown)
            {
                if (m_PhoneScreen != null)
                {
                    m_PhoneScreen.HandleSwipeGesture(gesture);
                }
            }
        }

        void OnHandPoseChanged(HandPose pose)
        {
            if (m_GroupDrag && pose != HandPose.Fist)
                SetGroupDrag(false);

            m_HeldPose = pose;
            m_HeldPoseSince = Time.unscaledTime;
            m_HeldPoseHandled = false;
        }

        void UpdateHeldPose()
        {
            if (m_HeldPoseHandled || m_HeldPose == HandPose.None ||
                Time.unscaledTime - m_HeldPoseSince < k_PoseHoldSeconds)
                return;

            m_HeldPoseHandled = true;
            if (m_HeldPose == HandPose.ThumbsUp)
            {
                if (m_ActiveScreen != null)
                {
                    m_ActiveScreen.AppendText("\n[Zatwierdzono]");
                    Debug.Log("[Workspace] Gest: ThumbsUp -> Dodano potwierdzenie");
                }
            }
            else if (m_HeldPose == HandPose.Fist)
            {
                SetGroupDrag(true);
            }
            else if (m_HeldPose == HandPose.OpenPalmForward)
            {
                ToggleWorkspaceVisibility();
                Debug.Log($"[Workspace] Gest: OpenPalmForward (STOP) -> Widoczność: {m_WorkspaceVisible}");
            }
        }

        // A swipe is meant in front of the face. Ignore it just after the desk was touched (typing) and
        // while the hand is well below the eyes (reaching for the desk), so keystrokes don't land in
        // another window.
        bool SwipesAllowed()
        {
            const float quietAfterTouch = 1f;
            float lastTouch = Mathf.Max(m_Keyboard != null ? m_Keyboard.LastTouchTime : float.NegativeInfinity,
                m_Trackpad != null ? m_Trackpad.LastTouchTime : float.NegativeInfinity);
            if (Time.unscaledTime - lastTouch < quietAfterTouch)
                return false;
            var tracker = m_Core != null ? m_Core.HandTracker : null;
            if (tracker != null && tracker.IsTracking)
            {
                var wrist = m_Core.Head.InverseTransformPoint(tracker.Joints[0]);
                if (wrist.y < -0.25f)
                    return false;
            }
            return true;
        }

        void SelectNextScreen()
        {
            if (m_Screens.Count <= 1)
                return;
            int idx = m_Screens.IndexOf(m_ActiveScreen);
            int nextIdx = (idx + 1) % m_Screens.Count;
            SetActiveScreen(m_Screens[nextIdx]);
        }

        void SelectPrevScreen()
        {
            if (m_Screens.Count <= 1)
                return;
            int idx = m_Screens.IndexOf(m_ActiveScreen);
            int prevIdx = (idx - 1 + m_Screens.Count) % m_Screens.Count;
            SetActiveScreen(m_Screens[prevIdx]);
        }

        // Every window and the phone follow the head (each keeps its place in the view, like a single
        // drag), so the whole layout can be turned to face another way.
        void SetGroupDrag(bool on)
        {
            m_GroupDrag = on;
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                if (on) screen.StartDragging();
                else screen.StopDragging();
            }
            if (m_PhoneScreen != null)
            {
                if (on) m_PhoneScreen.StartDragging();
                else m_PhoneScreen.StopDragging();
            }
            Debug.Log($"[Workspace] Gest pięści: przenoszenie całego układu {(on ? "start" : "koniec")}");
        }

        void ToggleWorkspaceVisibility() => SetDeskVisible(!m_WorkspaceVisible);

        // The STOP gesture hides only the keyboard and trackpad; the dock stays, and its "Wyśrodkuj"
        // brings them back, so an accidental palm can't leave the user without controls.
        void SetDeskVisible(bool visible)
        {
            m_WorkspaceVisible = visible;
            if (m_Keyboard != null)
                m_Keyboard.gameObject.SetActive(visible);
            if (m_Trackpad != null)
                m_Trackpad.gameObject.SetActive(visible);
        }

        // Still panels are anchored in the room; moving ones let go of their anchor (WorkspaceAnchors)
        void UpdateRoomAnchors()
        {
            if (m_RoomAnchors == null)
                return;
            foreach (var screen in m_Screens)
            {
                if (screen != null)
                    m_RoomAnchors.Keep(screen.transform, screen.IsDragging);
            }
            if (m_PhoneScreen != null)
                m_RoomAnchors.Keep(m_PhoneScreen.transform, m_PhoneScreen.IsDragging);
            if (m_Keyboard != null && m_Keyboard.gameObject.activeSelf)
                m_RoomAnchors.Keep(m_Keyboard.transform, false);
            if (m_Trackpad != null && m_Trackpad.gameObject.activeSelf)
                m_RoomAnchors.Keep(m_Trackpad.transform, false);
        }

        // Support for physical Bluetooth Keyboard & Mouse paired with iPhone 16 Pro
        void Update()
        {
            UpdateHeldPose();
            UpdateRoomAnchors();
            UpdateCinemaScreen();

            // 1. Physical Bluetooth Keyboard Input
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.backspaceKey.wasPressedThisFrame)
                    OnBackspacePressed();
                if (kb.enterKey.wasPressedThisFrame)
                    OnEnterPressed();

                // Any printable ASCII key press
                for (var key = Key.A; key <= Key.Z; key++)
                {
                    if (kb[key].wasPressedThisFrame)
                    {
                        bool shift = kb.shiftKey.isPressed;
                        char baseChar = (char)('a' + (key - Key.A));
                        OnKeyPressed(shift ? char.ToUpper(baseChar) : baseChar);
                    }
                }
                if (kb.spaceKey.wasPressedThisFrame)
                    OnKeyPressed(' ');
            }

            // 2. Physical Bluetooth Mouse Input
            var mouse = Mouse.current;
            if (mouse != null && m_ActiveScreen != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.01f)
                    m_ActiveScreen.MoveCursorDelta(delta * 0.8f);
                if (mouse.leftButton.wasPressedThisFrame)
                    m_ActiveScreen.ClickAtCursor();
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && m_ActiveScreen.IsBrowser)
                    m_ActiveScreen.Browser.Scroll(-Mathf.Sign(wheel) * 0.25f);
                if (mouse.rightButton.wasPressedThisFrame)
                    m_ActiveScreen.ContextClickAtCursor();
            }
        }

        // IPinchHandler: allows grabbing and repositioning virtual screens in 3D space
        public bool TryBeginPinch(GameObject gazeTarget, PinchSource source)
        {
            if (m_PlacingDesk)
            {
                PlaceDeskAtGaze();
                return true;
            }

            // Something is being carried (started with its grab button): a pinch anywhere puts it down.
            // (A pinch on a button is routed to the button first, PinchRouter.Order, so this is a pinch
            // at the window itself or at the room.)
            bool dropped = false;
            foreach (var screen in m_Screens)
            {
                if (screen != null && screen.IsDragging)
                {
                    screen.StopDragging();
                    dropped = true;
                }
            }
            if (m_PhoneScreen != null && m_PhoneScreen.IsDragging)
            {
                m_PhoneScreen.StopDragging();
                dropped = true;
            }
            if (dropped)
                return true;

            if (gazeTarget == null)
                return false;

            // Pinching at a screen focuses it, so the keyboard and trackpad go to it. Let the pinch go on
            // (false), so nothing else changes.
            foreach (var screen in m_Screens)
            {
                if (screen != null && screen != m_ActiveScreen &&
                    (gazeTarget.transform.IsChildOf(screen.transform) || gazeTarget == screen.gameObject))
                {
                    SetActiveScreen(screen);
                    break;
                }
            }
            return false;
        }

        public void EndPinch()
        {
            // Dropping happens on the pinch itself (TryBeginPinch); nothing to do when it opens.
        }
    }
}
