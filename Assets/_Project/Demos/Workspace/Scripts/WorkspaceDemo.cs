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
    public partial class WorkspaceDemo : Demo, IPinchHandler
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
        LiveCaptions m_Captions;
        EnvironmentSphere m_Environment;
        readonly List<ModelViewer> m_Models = new();

        // ---- 3D models (glTFast) on the surface looked at ----
        public void OpenModelPicker()
        {
            var screen = CreateNewScreen("Modele 3D");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>();
            if (ModelViewer.Available)
            {
                foreach (var file in ModelViewer.FindFiles())
                {
                    string path = file;
                    choices.Add(new ChoiceList.Choice(System.IO.Path.GetFileNameWithoutExtension(file), () => PlaceModel(path)));
                }
            }
            string empty = ModelViewer.Available
                ? "Brak modeli. Wrzuć pliki .glb do folderu Modele aplikacji (Finder lub Pliki). Po wyborze spójrz na stół: model stanie tam, gdzie patrzysz."
                : "Modele 3D wymagają pakietu glTFast: Unity > Package Manager > Add package by name > com.unity.cloud.gltfast.";
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Modele 3D", choices, empty);
        }

        void PlaceModel(string path)
        {
            var point = ModelViewer.PlacementPoint(m_Core.Head);
            var model = new GameObject("Model " + System.IO.Path.GetFileNameWithoutExtension(path)).AddComponent<ModelViewer>();
            model.transform.SetParent(transform, true);
            model.Load(path, point, m_Core.Head);
            m_Models.Add(model);
            model.Closed += m => m_Models.Remove(m);

            // Its controls, beside it at about eye level
            var controls = WidgetPanel.Create<ModelControls>(transform, m_Core.Head, new Vector2(360f, 130f),
                System.IO.Path.GetFileNameWithoutExtension(path), 0.9f);
            controls.Model = model;
            var right = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(point - m_Core.Head.position, Vector3.up).normalized);
            var position = point + right * 0.35f + Vector3.up * 0.25f;
            controls.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_Core.Head.position, Vector3.up));
            m_Widgets.Add(controls);
            controls.Closed += w => m_Widgets.Remove(w);
        }

        // ---- Environments: the room replaced by a place all around ----
        public void OpenEnvironmentPicker()
        {
            var screen = CreateNewScreen("Środowisko");
            if (screen == null || m_Environment == null)
                return;
            var choices = new List<ChoiceList.Choice> { new(EnvironmentSphere.Room, () => m_Environment.Show(null)) };
            foreach (var (label, source) in EnvironmentSphere.Find())
            {
                string chosen = source;
                choices.Add(new ChoiceList.Choice(label, () => m_Environment.Show(chosen)));
            }
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Środowisko", choices);
        }

        public void ToggleCaptions()
        {
            if (m_Captions != null)
                m_Captions.SetOn(!m_Captions.IsOn);
        }
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
            m_Captions = gameObject.AddComponent<LiveCaptions>();
            m_Captions.Initialize(transform, m_Core.Head, m_Voice);
            m_Environment = gameObject.AddComponent<EnvironmentSphere>();
            m_Environment.Initialize(m_Core);
            m_Voice.PartialRecognized += OnVoicePartial;
            m_Voice.RecordingStarted += OnVoiceStarted;
            m_Voice.RecordingEnded += OnVoiceEnded;

            WorkspaceSounds.Pressed += OnButtonPressed;
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

            foreach (var widget in m_Widgets)
            {
                if (widget != null)
                    Destroy(widget.gameObject);
            }
            m_Widgets.Clear();
            foreach (var model in m_Models)
            {
                if (model != null)
                    Destroy(model.gameObject);
            }
            m_Models.Clear();
            if (m_Drawing != null)
                Destroy(m_Drawing.gameObject);
            foreach (var game in m_Games)
            {
                if (game != null)
                    Destroy(game.gameObject);
            }
            m_Games.Clear();

            if (m_Keyboard != null)
                Destroy(m_Keyboard.gameObject);

            if (m_Trackpad != null)
                Destroy(m_Trackpad.gameObject);

            if (m_ControlDock != null)
                Destroy(m_ControlDock.gameObject);

            SetCinemaScreen(false);
            if (m_Toast != null)
                Destroy(m_Toast.gameObject);

            WorkspaceSounds.Pressed -= OnButtonPressed;
            WorkspaceSounds.Release();
            if (m_AddedListener != null)
                Destroy(m_AddedListener);

            // These components sit on this demo's own GameObject: destroy the components, not the object.
            if (m_Cinema != null)
                Destroy(m_Cinema);

            if (m_Captions != null)
                Destroy(m_Captions); // before the recognizer it listens to
            if (m_Environment != null)
                Destroy(m_Environment); // puts passthrough back

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
            string[] labels = { "Ekran", "PC", "Aplikacje", "Telefon", "Głos", "PL", "Kino", "Na blat", "Wyśrodkuj", "Więcej" };
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
            Add(9, "MoreBtn", M3Button.Variant.Text, OpenMore);

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

            // A new window: the free slot nearest where the user looks (WorkspaceLayout.cs). A saved one: its slot.
            if (slot < 0 || m_ScreenSlots.ContainsValue(slot))
                slot = ChooseScreenSlot();
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
            public string pdf; // a PDF reader: the file and page
            public int pdfPage;
            public string film;    // a film window: the file and where it was
            public double filmTime;
            public string media;   // a spatial photo/video window: the file shown
            public int slot;
            public float size = 1f;
            public float width;  // the window's stretched size in dp (0: default)
            public float height;
            public bool pinned;
        }

        [Serializable]
        class SavedWidget
        {
            public string kind;
            public string data;
        }

        [Serializable]
        class SavedWorkspace
        {
            public List<SavedScreen> screens = new();
            public List<SavedWidget> widgets = new();
            public bool phone;
            public string locale;
            public string environment; // the place shown instead of the room (null: the room)
            public bool cinema;
        }

        static string SaveFile => Path.Combine(Application.persistentDataPath, "workspace.json");

        void SaveWorkspace()
        {
            if (m_Core != null)
                WriteWorkspace(SaveFile, CaptureWorkspace());
        }

        static bool WriteWorkspace(string path, SavedWorkspace saved)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(saved, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] Nie zapisano układu: {e.Message}");
                return false;
            }
        }

        static SavedWorkspace ReadWorkspace(string path)
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<SavedWorkspace>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] Nie wczytano układu: {e.Message}");
                return null;
            }
        }

        SavedWorkspace CaptureWorkspace()
        {
            var saved = new SavedWorkspace
            {
                phone = m_PhoneScreen != null,
                locale = m_Voice != null ? m_Voice.Locale : null,
                environment = m_Environment != null ? m_Environment.Current : null,
                cinema = m_Cinema != null && m_Cinema.IsCinemaMode,
            };
            foreach (var widget in m_Widgets)
            {
                if (widget != null && widget.Kind != "model" && widget.Kind != "rysowanie" && widget.Kind != "gra")
                    saved.widgets.Add(new SavedWidget { kind = widget.Kind, data = widget.SaveData });
            }
            foreach (var screen in m_Screens)
            {
                if (screen == null || screen.GetComponent<TutorialPanel>() != null || screen.GetComponent<AppPicker>() != null ||
                    screen.GetComponent<ChoiceList>() != null || screen.GetComponent<FileImport>() != null ||
                    screen.GetComponent<GalleryView>() != null || screen.GetComponent<AssistantView>() != null ||
                    (screen.IsBrowser && screen.Browser.IsPrivate)) // a private window leaves no trace
                    continue;
                saved.screens.Add(new SavedScreen
                {
                    title = screen.Title,
                    text = screen.IsRemoteDesktop || screen.IsBrowser ? "" : screen.Text,
                    pc = screen.IsRemoteDesktop,
                    app = screen.RemoteApp,
                    browser = screen.IsBrowser,
                    url = screen.IsBrowser ? screen.Browser.Url : null,
                    pdf = screen.GetComponent<PdfView>() != null ? screen.GetComponent<PdfView>().FilePath : null,
                    pdfPage = screen.GetComponent<PdfView>() != null ? screen.GetComponent<PdfView>().Page : 0,
                    film = screen.GetComponent<FilmPlayer>() != null ? screen.GetComponent<FilmPlayer>().FilePath : null,
                    filmTime = screen.GetComponent<FilmPlayer>() != null ? screen.GetComponent<FilmPlayer>().PlaybackTime : 0.0,
                    media = screen.GetComponent<SpatialMediaView>() != null ? screen.GetComponent<SpatialMediaView>().FilePath : null,
                    slot = m_ScreenSlots.TryGetValue(screen, out var slot) ? slot : -1,
                    size = screen.Size,
                    width = screen.LayoutSize.x,
                    height = screen.LayoutSize.y,
                    pinned = screen.Pinned,
                });
            }
            return saved;
        }

        bool RestoreWorkspace()
        {
            var saved = ReadWorkspace(SaveFile);
            if (saved == null || ((saved.screens == null || saved.screens.Count == 0) && (saved.widgets == null || saved.widgets.Count == 0)))
                return false;
            ApplyWorkspace(saved);
            return true;
        }

        // Opens the saved windows, widgets, phone and place around the user (into an empty workspace)
        void ApplyWorkspace(SavedWorkspace saved)
        {
            saved.screens ??= new List<SavedScreen>();

            foreach (var item in saved.screens)
            {
                // A viewer whose file is gone (deleted, or the app reinstalled) is not brought back as an empty note
                bool viewer = !string.IsNullOrEmpty(item.pdf) || !string.IsNullOrEmpty(item.film) || !string.IsNullOrEmpty(item.media);
                // (JsonUtility writes missing strings as "", not null)
                string file = !string.IsNullOrEmpty(item.pdf) ? item.pdf : !string.IsNullOrEmpty(item.film) ? item.film : item.media;
                if (viewer && !File.Exists(file))
                    continue;
                var screen = CreateNewScreen(string.IsNullOrEmpty(item.title) ? "Ekran" : item.title, item.slot);
                if (screen == null)
                    continue;
                if (!string.IsNullOrEmpty(item.film))
                {
                    var films = FilmPlayer.FindFiles();
                    int index = films.IndexOf(item.film);
                    if (index < 0) // a file outside the folder: shown first
                    {
                        films.Insert(0, item.film);
                        index = 0;
                    }
                    screen.gameObject.AddComponent<FilmPlayer>().Initialize(screen, films, index, ToggleCinemaMode,
                        item.filmTime, startPaused: true);
                }
                else if (!string.IsNullOrEmpty(item.media))
                {
                    var media = SpatialMediaView.FindFiles();
                    int index = media.IndexOf(item.media);
                    if (index < 0) // a file outside the folder: shown first
                    {
                        media.Insert(0, item.media);
                        index = 0;
                    }
                    screen.gameObject.AddComponent<SpatialMediaView>().Initialize(screen, media, index);
                }
                else if (!string.IsNullOrEmpty(item.pdf))
                    screen.gameObject.AddComponent<PdfView>().Initialize(screen, item.pdf, item.pdfPage);
                else if (item.browser)
                    screen.EnableBrowser(string.IsNullOrEmpty(item.url) ? null : item.url);
                else if (item.pc)
                    screen.EnableRemoteDesktop(null, string.IsNullOrEmpty(item.app) ? null : item.app);
                else
                    screen.SetText(item.text);
                if (item.width > 0f && item.height > 0f)
                    screen.SetLayoutSize(new Vector2(item.width, item.height));
                if (item.size > 0f && !Mathf.Approximately(item.size, 1f))
                    screen.SetSize(item.size);
                screen.SetPinned(item.pinned);
            }
            if (saved.phone)
                SpawnPhoneScreen();
            if (m_Environment != null && !string.IsNullOrEmpty(saved.environment) &&
                (Array.IndexOf(EnvironmentSphere.BuiltIn, saved.environment) >= 0 || File.Exists(saved.environment)))
                m_Environment.Show(saved.environment);
            if (saved.widgets != null)
            {
                foreach (var item in saved.widgets)
                {
                    var widget = SpawnWidget(item.kind);
                    if (widget != null)
                        widget.LoadData(item.data);
                }
            }
            if (m_Voice != null && !string.IsNullOrEmpty(saved.locale))
            {
                m_Voice.Locale = saved.locale;
                if (m_LanguageBtn != null)
                    m_LanguageBtn.Text = saved.locale == "en-US" ? "EN" : "PL";
            }
            if (m_Cinema != null && saved.cinema != m_Cinema.IsCinemaMode)
                ToggleCinemaMode();
            Debug.Log($"[Workspace] Przywrócono układ: {saved.screens.Count} okien");
        }

        // iOS may end a backgrounded app without OnDemoExit: save when going to the background
        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveWorkspace();
        }

        // ---- "Więcej": the tools that don't fit in the dock ----
        // The dock's "Więcej" button toggles it: one menu at a time. 3 x 3 pages, the most used first;
        // the widgets have their own menu.
        VirtualScreen m_MoreScreen;

        public void OpenMore()
        {
            if (m_MoreScreen != null)
            {
                m_MoreScreen.Close();
                return;
            }
            var screen = CreateNewScreen("Więcej");
            if (screen == null)
                return;
            m_MoreScreen = screen;
            var choices = new List<ChoiceList.Choice>
            {
                new("Asystent", () => OpenAssistant()),
                new("Galeria", OpenGallery),
                new("Filmy", OpenFilmPicker),
                new("Widżety", OpenWidgetMenu),
                new(m_Captions != null && m_Captions.IsOn ? "Bez napisów" : "Napisy", ToggleCaptions),
                new("Układy", OpenLayouts),
                new("Gry", OpenGames),
                new("Rysowanie", OpenDrawing),
                new("PDF", OpenPdfPicker),
                new("Zdjęcia 3D", OpenSpatialPicker),
                new("Środowisko", OpenEnvironmentPicker),
                new("Modele 3D", OpenModelPicker),
                new("Pliki", OpenFiles),
                new("Zrzut ekranu", TakeScreenshot),
                new("Samouczek", OpenTutorial),
            };
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Więcej", choices, columns: 3);
        }

        public void OpenWidgetMenu()
        {
            var screen = CreateNewScreen("Widżety");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>
            {
                new("Zegar", () => SpawnWidget("zegar"), "timer"),
                new("Minutnik", () => SpawnWidget("minutnik"), "timer"),
                new("Notatka", () => SpawnWidget("notatka"), "edit"),
                new("Pogoda", () => SpawnWidget("pogoda")),
                new("Kalendarz", () => SpawnWidget("kalendarz")),
            };
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Widżety", choices);
        }

        // ---- PDF reader ----
        public void OpenPdfPicker()
        {
            var screen = CreateNewScreen("PDF");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>();
            foreach (var file in PdfView.FindFiles())
            {
                string path = file;
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (name.Length > 26)
                    name = name.Substring(0, 25) + "…";
                choices.Add(new ChoiceList.Choice(name, () => screen.gameObject.AddComponent<PdfView>().Initialize(screen, path)));
            }
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Wybierz PDF", choices,
                "Brak plików PDF. Wrzuć je do folderu PDF aplikacji: Finder > iPhone > Pliki > CardboardXR, albo aplikacja Pliki.",
                closeOnPick: false);
        }

        // ---- Your films, in a window or on the cinema screen ----
        public void OpenFilmPicker()
        {
            var screen = CreateNewScreen("Filmy");
            if (screen == null)
                return;
            var files = FilmPlayer.FindFiles();
            var choices = new List<ChoiceList.Choice>();
            for (int i = 0; i < files.Count; i++)
            {
                int index = i;
                string name = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                if (name.Length > 26)
                    name = name.Substring(0, 25) + "…";
                choices.Add(new ChoiceList.Choice(name,
                    () => screen.gameObject.AddComponent<FilmPlayer>().Initialize(screen, files, index, ToggleCinemaMode)));
            }
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Wybierz film", choices,
                "Brak filmów. Wrzuć .mp4 / .mov do folderu Filmy aplikacji (Finder, aplikacja Pliki albo Więcej > Pliki).",
                closeOnPick: false);
        }

        // ---- Drawing in the room: while its panel is open, a pinch away from the windows draws ----
        SpaceDrawing m_Drawing;
        DrawControls m_DrawControls;
        bool m_DrawingPinch;

        public void OpenDrawing()
        {
            if (m_Core == null)
                return;
            if (m_DrawControls != null)
            {
                WorldUi.PlaceInFront((RectTransform)m_DrawControls.transform, m_Core.Head, 1.1f, -5f, 30f);
                return;
            }
            if (m_Drawing == null)
            {
                var root = new GameObject("Rysunki");
                root.transform.SetParent(transform, false);
                root.transform.position = m_Core.Head.position; // the room anchor for all strokes
                m_Drawing = root.AddComponent<SpaceDrawing>();
                m_Drawing.Initialize(m_Core);
            }
            m_Drawing.Active = true;
            var size = new Vector2(400f, 260f);
            m_DrawControls = WidgetPanel.Create<DrawControls>(transform, m_Core.Head, size, "Rysowanie", 1.1f);
            m_DrawControls.Drawing = m_Drawing;
            m_DrawControls.Refresh();
            PlaceWidget((RectTransform)m_DrawControls.transform, size, 1.1f);
            m_Widgets.Add(m_DrawControls);
            m_DrawControls.Closed += w =>
            {
                m_Widgets.Remove(w);
                m_DrawControls = null;
                if (m_Drawing != null)
                {
                    m_Drawing.EndStroke();
                    m_Drawing.Active = false;
                }
            };
            ShowToast("Uszczypnij w powietrzu, aby rysować");
        }

        // ---- The AI assistant (one window) ----
        public AssistantView OpenAssistant()
        {
            foreach (var existing in m_Screens)
            {
                var view = existing != null ? existing.GetComponent<AssistantView>() : null;
                if (view != null)
                {
                    SetActiveScreen(existing);
                    return view;
                }
            }
            var screen = CreateNewScreen("Asystent AI");
            if (screen == null)
                return null;
            var created = screen.gameObject.AddComponent<AssistantView>();
            created.Initialize(screen, m_Voice);
            return created;
        }

        // ---- Board games on the table ----
        readonly List<TableGame> m_Games = new();

        public void OpenGames()
        {
            var screen = CreateNewScreen("Gry");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>
            {
                new("Warcaby", () => StartGame<CheckersGame>()),
                new("Chińczyk", () => StartGame<LudoGame>()),
                new("Kółko i krzyżyk", () => StartGame<TicTacToeGame>()),
            };
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Gry na stole: spójrz na stół i wybierz", choices);
        }

        void StartGame<T>() where T : TableGame
        {
            if (m_Core == null)
                return;
            var game = new GameObject(typeof(T).Name).AddComponent<T>();
            game.transform.SetParent(transform, true);
            game.Core = m_Core;
            game.Initialize(m_Core.Head);
            game.PlaceAtGaze();
            m_Games.Add(game);
            game.Closed += g => m_Games.Remove(g);
            game.Moving += g => { if (m_RoomAnchors != null) m_RoomAnchors.Release(g.transform); };

            // Its panel, beside the board at about eye level
            var size = new Vector2(390f, 210f);
            var controls = WidgetPanel.Create<GameControls>(transform, m_Core.Head, size, game.Title, 0.9f);
            controls.Attach(game);
            Vector3 point = game.transform.position;
            var right = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(point - m_Core.Head.position, Vector3.up).normalized);
            var position = point + right * 0.4f + Vector3.up * 0.3f;
            controls.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_Core.Head.position, Vector3.up));
            m_Widgets.Add(controls);
            controls.Closed += w => m_Widgets.Remove(w);
            ShowToast(typeof(T) == typeof(LudoGame) ? "Chińczyk: rzuć kostką na panelu"
                : typeof(T) == typeof(TicTacToeGame) ? "Kółko i krzyżyk: narysuj krzyżyk w polu"
                : "Warcaby: Ty grasz białymi");
        }

        // ---- The iPhone's Photos library (one gallery window at a time: it shares the native list) ----
        public void OpenGallery()
        {
            foreach (var existing in m_Screens)
            {
                if (existing != null && existing.GetComponent<GalleryView>() != null)
                {
                    SetActiveScreen(existing);
                    return;
                }
            }
            var screen = CreateNewScreen("Galeria");
            if (screen != null)
                screen.gameObject.AddComponent<GalleryView>().Initialize(screen, OpenFromGallery);
        }

        // A photo or spatial photo/video in SpatialMediaView, any other video in FilmPlayer: a new window
        void OpenFromGallery(string path, bool video, bool spatial)
        {
            var screen = CreateNewScreen(System.IO.Path.GetFileNameWithoutExtension(path));
            if (screen == null)
                return;
            var files = new List<string> { path };
            if (video && !spatial)
                screen.gameObject.AddComponent<FilmPlayer>().Initialize(screen, files, 0, ToggleCinemaMode);
            else
                screen.gameObject.AddComponent<SpatialMediaView>().Initialize(screen, files, 0);
        }

        // ---- Files from iCloud Drive and other places, into the app's folders ----
        public void OpenFiles()
        {
            var screen = CreateNewScreen("Pliki");
            if (screen != null)
                screen.gameObject.AddComponent<FileImport>().Initialize(screen);
        }

        // ---- Spatial photos and videos (iPhone 15 Pro / 16 Pro) ----
        public void OpenSpatialPicker()
        {
            var screen = CreateNewScreen("Zdjęcia 3D");
            if (screen == null)
                return;
            var files = SpatialMediaView.FindFiles();
            var choices = new List<ChoiceList.Choice>();
            for (int i = 0; i < files.Count; i++)
            {
                int index = i;
                string name = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                if (name.Length > 26)
                    name = name.Substring(0, 25) + "…";
                choices.Add(new ChoiceList.Choice(name, () => screen.gameObject.AddComponent<SpatialMediaView>().Initialize(screen, files, index)));
            }
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Zdjęcia i filmy 3D", choices,
                "Brak plików. Wrzuć zdjęcia przestrzenne (.heic) i filmy przestrzenne (.mov) z iPhone'a do folderu Przestrzenne aplikacji (Finder lub Pliki). Zdjęcia obok siebie: .jpg z _sbs w nazwie.",
                closeOnPick: false);
        }

        // ---- Widgets ----
        readonly List<WidgetPanel> m_Widgets = new();
        NoteWidget m_WritingNote;

        // A widget of a kind ("zegar", "minutnik", "notatka", "pogoda", "kalendarz"), in front of the
        // user a little above the eyes; move it with its handle
        WidgetPanel SpawnWidget(string kind)
        {
            if (m_Core == null)
                return null;
            const float distance = 1.1f;
            Vector2 size = kind switch
            {
                "zegar" => new Vector2(300f, 190f),
                "minutnik" => new Vector2(340f, 220f),
                "notatka" => new Vector2(340f, 280f),
                "pogoda" => new Vector2(360f, 200f),
                _ => new Vector2(380f, 320f),
            };
            WidgetPanel widget = kind switch
            {
                "zegar" => WidgetPanel.Create<ClockWidget>(transform, m_Core.Head, size, "Zegar", distance),
                "minutnik" => WidgetPanel.Create<TimerWidget>(transform, m_Core.Head, size, "Minutnik", distance),
                "notatka" => WidgetPanel.Create<NoteWidget>(transform, m_Core.Head, size, "Notatka", distance),
                "pogoda" => WidgetPanel.Create<WeatherWidget>(transform, m_Core.Head, size, "Pogoda", distance),
                "kalendarz" => WidgetPanel.Create<CalendarWidget>(transform, m_Core.Head, size, "Kalendarz", distance),
                _ => null,
            };
            if (widget == null)
                return null;
            // Near the gaze but clear of every window and widget (WorkspaceLayout.cs)
            PlaceWidget((RectTransform)widget.transform, size, distance);
            widget.Closed += w =>
            {
                m_Widgets.Remove(w);
                if (w == m_WritingNote)
                    m_WritingNote = null;
            };
            if (widget is NoteWidget note)
                note.WriteRequested += n => SetWritingNote(n == m_WritingNote ? null : n);
            m_Widgets.Add(widget);
            return widget;
        }

        // Typing (keyboards, dictation) goes into this note until a window is chosen or "Pisz" is pressed again
        void SetWritingNote(NoteWidget note)
        {
            if (m_WritingNote != null)
                m_WritingNote.SetWriting(false);
            m_WritingNote = note;
            if (note != null)
                note.SetWriting(true);
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
        public void CreateBrowserScreen(string url = null, bool privateMode = false)
        {
            var screen = CreateNewScreen("Przeglądarka");
            if (screen != null)
                screen.EnableBrowser(url, privateMode);
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
            SetWritingNote(null); // a window chosen: typing goes to it, not to a note
            if (m_ActiveScreen != null && m_ActiveScreen != screen)
                m_ActiveScreen.SetHighlighted(false);
            m_ActiveScreen = screen;
            if (screen != null)
                screen.SetHighlighted(true);
            if (m_Trackpad != null)
                m_Trackpad.SetTargetScreen(screen);
        }

        // A button pressed in a window makes that window the active one (typing and the trackpad go to it)
        void OnButtonPressed(UnityEngine.UI.Button button)
        {
            if (button == null)
                return;
            foreach (var screen in m_Screens)
            {
                if (screen != null && screen != m_ActiveScreen && button.transform.IsChildOf(screen.transform))
                {
                    SetActiveScreen(screen);
                    return;
                }
            }
        }

        void OnScreenClosed(VirtualScreen screen)
        {
            m_Screens.Remove(screen);
            m_ScreenSlots.Remove(screen);
            if (m_MoreScreen == screen)
                m_MoreScreen = null;
            if (m_ActiveScreen == screen)
            {
                m_ActiveScreen = null;
                SetActiveScreen(ScreenNearestGaze());
            }
        }

        void OnKeyPressed(char c)
        {
            if (m_WritingNote != null)
                m_WritingNote.Type(c.ToString());
            else if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
                m_PhoneScreen.AppendChatChar(c);
            else if (m_ActiveScreen != null)
                m_ActiveScreen.AppendChar(c);
        }

        void OnBackspacePressed()
        {
            if (m_WritingNote != null)
                m_WritingNote.Backspace();
            else if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
                m_PhoneScreen.ChatBackspace();
            else if (m_ActiveScreen != null)
                m_ActiveScreen.Backspace();
        }

        void OnEnterPressed()
        {
            if (m_WritingNote != null)
                m_WritingNote.Type("\n");
            else if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
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
            if (m_Captions != null && m_Captions.IsOn)
                return;
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
            // The voice button also ends captions
            if (m_Captions != null && m_Captions.IsOn)
            {
                m_Captions.SetOn(false);
                return;
            }
            if (m_Voice != null)
                m_Voice.ToggleRecording();
        }

        void OnSpeechRecognized(string transcribedText)
        {
            if (string.IsNullOrWhiteSpace(transcribedText))
                return;
            // Captions on: speech is subtitled, not typed or obeyed, except the command to stop them
            if (m_Captions != null && m_Captions.IsOn)
            {
                string spoken = transcribedText.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
                if (spoken == "wyłącz napisy" || spoken == "koniec napisów" || spoken == "stop captions")
                    m_Captions.SetOn(false);
                return;
            }
            if (!VoiceDictation.IsMock && TryVoiceCommand(transcribedText))
                return;

            // The assistant's window is active: what was said is a question for it
            var assistant = m_ActiveScreen != null ? m_ActiveScreen.GetComponent<AssistantView>() : null;
            if (assistant != null && !VoiceDictation.IsMock)
            {
                assistant.Ask(transcribedText);
                return;
            }

            // Outside iOS nothing is recognized (a canned sentence comes back), so say so in the text.
            if (VoiceDictation.IsMock)
                transcribedText = "[głos-demo] " + transcribedText;

            if (m_WritingNote != null)
            {
                m_WritingNote.Type(" " + transcribedText);
            }
            else if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
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
            if (TryLayoutCommand(command))
                return true;
            // "zapytaj …", "asystencie …": a question for the assistant from anywhere
            foreach (var prefix in new[] { "zapytaj ", "asystencie ", "asystent ", "hej claude ", "claude " })
            {
                if (command.StartsWith(prefix, StringComparison.Ordinal) && command.Length > prefix.Length + 2)
                {
                    var view = OpenAssistant();
                    if (view != null)
                        view.Ask(spoken.Trim().Substring(prefix.Length));
                    return true;
                }
            }
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
                case "następna strona": case "dalej": case "next page": case "następne": case "następne zdjęcie":
                    m_ActiveScreen?.GetComponent<PdfView>()?.NextPage();
                    m_ActiveScreen?.GetComponent<SpatialMediaView>()?.Next();
                    if (m_ActiveScreen != null && m_ActiveScreen.GetComponent<FilmPlayer>() != null)
                        m_ActiveScreen.GetComponent<FilmPlayer>().Next();
                    break;
                case "poprzednia strona": case "previous page": case "poprzednie": case "poprzednie zdjęcie":
                    m_ActiveScreen?.GetComponent<PdfView>()?.PreviousPage();
                    m_ActiveScreen?.GetComponent<SpatialMediaView>()?.Previous();
                    break;
                case "zdjęcia 3d": case "zdjęcia": case "przestrzenne": case "filmy 3d": case "spatial":
                    OpenSpatialPicker();
                    break;
                case "filmy": case "film": case "wideo": case "movies":
                    OpenFilmPicker();
                    break;
                case "pauza": case "odtwórz": case "pause": case "play":
                {
                    var film = m_ActiveScreen != null ? m_ActiveScreen.GetComponent<FilmPlayer>() : null;
                    if (film == null)
                        film = FilmPlayer.Active;
                    if (film != null)
                        film.TogglePause();
                    break;
                }
                case "pliki": case "icloud": case "importuj": case "files":
                    OpenFiles();
                    break;
                case "środowisko": case "środowiska": case "environment":
                    OpenEnvironmentPicker();
                    break;
                case "modele": case "modele 3d": case "model": case "models":
                    OpenModelPicker();
                    break;
                case "wyłącz środowisko": case "pokój": case "room":
                    m_Environment?.Show(null);
                    break;
                case "pdf": case "czytnik": case "dokumenty":
                    OpenPdfPicker();
                    break;
                case "napisy": case "napisy na żywo": case "captions":
                    ToggleCaptions();
                    break;
                case "więcej": case "menu": case "more":
                    OpenMore();
                    break;
                case "zegar": case "minutnik": case "notatka": case "pogoda": case "kalendarz":
                    SpawnWidget(command);
                    break;
                case "widżety": case "widgets":
                    OpenWidgetMenu();
                    break;
                case "usuń klucz": case "usuń klucz ai": case "delete key":
                    AssistantView.DeleteKey();
                    ShowToast("Klucz asystenta usunięty z pęku kluczy");
                    break;
                case "asystent": case "asystent ai": case "claude": case "assistant":
                    OpenAssistant();
                    break;
                case "galeria": case "zdjęcia z telefonu": case "gallery": case "photos":
                    OpenGallery();
                    break;
                case "gry": case "gra": case "games":
                    OpenGames();
                    break;
                case "warcaby": case "checkers":
                    StartGame<CheckersGame>();
                    break;
                case "chińczyk": case "ludo":
                    StartGame<LudoGame>();
                    break;
                case "kółko i krzyżyk": case "kółko krzyżyk": case "krzyżyk": case "tic tac toe":
                    StartGame<TicTacToeGame>();
                    break;
                case "rzuć": case "rzuć kostką": case "kostka": case "roll":
                    foreach (var game in m_Games)
                    {
                        if (game != null && game.ActionLabel != null)
                            game.Action();
                    }
                    break;
                case "rysuj": case "rysowanie": case "draw":
                    OpenDrawing();
                    break;
                case "cofnij": case "undo":
                    if (m_Drawing != null)
                        m_Drawing.Undo();
                    break;
                case "układy": case "layouts":
                    OpenLayouts();
                    break;
                case "zrzut ekranu": case "zrzut": case "screenshot":
                    TakeScreenshot();
                    break;
                case "zamknij": case "zamknij okno": case "close": case "close window":
                    if (m_ActiveScreen != null)
                        m_ActiveScreen.Close();
                    break;
                case "przenieś": case "przenieś okno": case "przesuń okno": case "move": case "move window":
                    if (m_ActiveScreen != null)
                        m_ActiveScreen.StartDragging(); // follows the head; "upuść" or a pinch puts it down
                    break;
                case "upuść": case "zostaw": case "tutaj": case "drop":
                    DropCarried();
                    break;
                case "pomoc": case "samouczek": case "help": case "tutorial":
                    OpenTutorial();
                    break;
                case "przeglądarka": case "internet": case "browser":
                    CreateBrowserScreen();
                    break;
                case "prywatna przeglądarka": case "tryb prywatny": case "private browser":
                    CreateBrowserScreen(null, true);
                    break;
                case "rozciągnij": case "rozciągnij okno": case "stretch":
                    if (m_ActiveScreen != null)
                        m_ActiveScreen.StartStretching();
                    break;
                case "większe okno": case "powiększ": case "bigger":
                    if (m_ActiveScreen != null)
                        m_ActiveScreen.Grow();
                    break;
                case "mniejsze okno": case "pomniejsz": case "smaller":
                    if (m_ActiveScreen != null)
                        m_ActiveScreen.Shrink();
                    break;
                case "większy tekst": case "bigger text":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.ZoomBy(1.25f);
                    break;
                case "mniejszy tekst": case "smaller text":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.ZoomBy(0.8f);
                    break;
                case "wersja komputerowa": case "wersja na komputer": case "desktop site":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.SetDesktopMode(1);
                    break;
                case "wersja mobilna": case "mobile site":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.SetDesktopMode(2);
                    break;
                case "wersja automatyczna":
                    if (m_ActiveScreen != null && m_ActiveScreen.IsBrowser)
                        m_ActiveScreen.Browser.SetDesktopMode(0);
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
            m_CinemaHint = M3.Text("CinemaHint", m_CinemaPanel, "Włącz film (Więcej > Filmy) albo klip w telefonie",
                M3Theme.Type.BodyLarge, Vector2.zero, new Vector2(600f, 40f), M3Theme.OnSurfaceVariant);
            // In front of where the user looks now (not a slot: it's for watching, not working)
            WorldUi.PlaceInFront(m_CinemaPanel, m_Core.Head, k_CinemaDistance, k_ScreenPitch, 0f);
        }

        void UpdateCinemaScreen()
        {
            if (m_CinemaPanel == null)
                return;
            // Your film first (the most recently played), else the phone's clip
            var texture = FilmPlayer.Active != null ? FilmPlayer.Active.PlayingTexture : null;
            if (texture == null && m_PhoneScreen != null)
                texture = m_PhoneScreen.PlayingTexture;
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
                     m_ActiveScreen != null && m_ActiveScreen.GetComponent<PdfView>() != null)
            {
                var pdf = m_ActiveScreen.GetComponent<PdfView>();
                if (gesture == DynamicGesture.SwipeUp) pdf.NextPage(); else pdf.PreviousPage();
            }
            else if ((gesture == DynamicGesture.SwipeUp || gesture == DynamicGesture.SwipeDown) &&
                     m_ActiveScreen != null && m_ActiveScreen.GetComponent<SpatialMediaView>() != null)
            {
                var media = m_ActiveScreen.GetComponent<SpatialMediaView>();
                if (gesture == DynamicGesture.SwipeUp) media.Next(); else media.Previous();
            }
            else if ((gesture == DynamicGesture.SwipeUp || gesture == DynamicGesture.SwipeDown) &&
                     m_ActiveScreen != null && m_ActiveScreen.GetComponent<GalleryView>() != null)
            {
                var gallery = m_ActiveScreen.GetComponent<GalleryView>();
                if (gallery != null && gesture == DynamicGesture.SwipeUp) gallery.NextPage(); else if (gallery != null) gallery.PreviousPage();
            }
            else if ((gesture == DynamicGesture.SwipeUp || gesture == DynamicGesture.SwipeDown) &&
                     m_ActiveScreen != null && m_ActiveScreen.GetComponent<FilmPlayer>() != null)
            {
                // Up: 30 s further, down: 30 s back
                m_ActiveScreen.GetComponent<FilmPlayer>().Seek(gesture == DynamicGesture.SwipeUp ? 30.0 : -30.0);
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

        // Swipe right / "następny ekran": the window to the right of the active one (as seen from the head),
        // wrapping round to the leftmost; swipe left the other way
        void SelectNextScreen() => SelectScreenBeside(1);
        void SelectPrevScreen() => SelectScreenBeside(-1);

        void SelectScreenBeside(int direction)
        {
            if (m_Screens.Count <= 1 || m_Core == null)
                return;
            var from = m_ActiveScreen != null ? m_ActiveScreen : ScreenNearestGaze();
            // Left to right as seen now (the seam behind the user), a stack from the top down
            float headYaw = YawOf(m_Core.Head.forward);
            var ordered = new List<VirtualScreen>();
            foreach (var screen in m_Screens)
            {
                if (screen != null)
                    ordered.Add(screen);
            }
            ordered.Sort((a, b) =>
            {
                Vector3 toA = a.transform.position - m_Core.Head.position, toB = b.transform.position - m_Core.Head.position;
                float yawA = Mathf.DeltaAngle(headYaw, YawOf(toA)), yawB = Mathf.DeltaAngle(headYaw, YawOf(toB));
                if (Mathf.Abs(yawA - yawB) > 10f)
                    return yawA.CompareTo(yawB);
                return PitchOf(toB).CompareTo(PitchOf(toA));
            });
            int index = ordered.IndexOf(from);
            if (index < 0 || ordered.Count == 0)
                return;
            SetActiveScreen(ordered[(index + direction + ordered.Count) % ordered.Count]);
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
            foreach (var widget in m_Widgets)
            {
                if (widget == null)
                    continue;
                if (on) widget.StartDragging();
                else widget.StopDragging();
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
            foreach (var widget in m_Widgets)
            {
                if (widget != null)
                    m_RoomAnchors.Keep(widget.transform, widget.IsDragging);
            }
            foreach (var model in m_Models)
            {
                if (model != null)
                    m_RoomAnchors.Keep(model.transform, false);
            }
            if (m_Drawing != null && m_Drawing.StrokeCount > 0)
                m_RoomAnchors.Keep(m_Drawing.transform, false);
            foreach (var game in m_Games)
            {
                if (game != null)
                    m_RoomAnchors.Keep(game.transform, false);
            }
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
            UpdateToast();
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
            if (DropCarried())
                return true;

            // A board game's piece, field or dice
            var cell = gazeTarget != null ? gazeTarget.GetComponentInParent<GameCell>() : null;
            if (cell != null && cell.Game != null && cell.Game.BeginPinch(cell, source))
            {
                m_GamePinch = cell.Game;
                return true;
            }

            // Drawing: a pinch not on a window or panel draws (it needs the hand's position: 6DoF)
            if (m_Drawing != null && m_Drawing.Active &&
                (gazeTarget == null || gazeTarget.GetComponentInParent<Canvas>() == null))
            {
                if (source != PinchSource.Hand)
                {
                    ShowToast("Rysowanie wymaga śledzenia dłoni (6DoF)");
                    return false;
                }
                m_Drawing.BeginStroke();
                m_DrawingPinch = true;
                return true;
            }

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

        // Puts down whatever is carried or being stretched (windows, the phone, widgets, the fist's group)
        bool DropCarried()
        {
            if (m_GroupDrag)
            {
                SetGroupDrag(false);
                return true;
            }
            bool dropped = false;
            foreach (var screen in m_Screens)
            {
                if (screen != null && screen.IsDragging)
                {
                    screen.StopDragging();
                    dropped = true;
                }
                // Stretching ends the same way: a pinch anywhere sets the size
                if (screen != null && screen.IsStretching)
                {
                    screen.StopStretching();
                    dropped = true;
                }
            }
            if (m_PhoneScreen != null && m_PhoneScreen.IsDragging)
            {
                m_PhoneScreen.StopDragging();
                dropped = true;
            }
            foreach (var widget in m_Widgets)
            {
                if (widget != null && widget.IsDragging)
                {
                    widget.StopDragging();
                    dropped = true;
                }
            }
            return dropped;
        }

        TableGame m_GamePinch;

        public void EndPinch()
        {
            if (m_GamePinch != null)
            {
                m_GamePinch.EndPinch();
                m_GamePinch = null;
            }
            if (m_DrawingPinch)
            {
                m_DrawingPinch = false;
                if (m_Drawing != null)
                    m_Drawing.EndStroke();
            }
            // Dropping happens on the pinch itself (TryBeginPinch); nothing to do when it opens.
        }
    }
}
