using System.Collections.Generic;
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
        const float k_PhoneYaw = 40f;
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

            // Nothing in the app plays sound, so no scene has an AudioListener, and without one Unity
            // plays nothing: the key and trackpad clicks and the windows' spatial audio would be silent.
            // The listener goes on the head, so 3D sounds follow where the user looks (and AirPods
            // head tracking can do the rest).
            if (Object.FindFirstObjectByType<AudioListener>() == null)
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
            CreateNewScreen("Główny Notatnik");
            SpawnPhoneScreen(); // Spawn TikTok/Messenger phone

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
            string[] labels = { "Monitor", "Pulpit PC", "Telefon", "Głos", "Kino", "Reset biurka" };
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
            Add(2, "NewPhoneBtn", M3Button.Variant.Filled, SpawnPhoneScreen);
            m_VoiceBtn = Add(3, "VoiceBtn", M3Button.Variant.Outlined, ToggleVoiceDictation);
            m_CinemaBtn = Add(4, "CinemaBtn", M3Button.Variant.Outlined, ToggleCinemaMode);
            Add(5, "ResetDeskBtn", M3Button.Variant.Tonal, ResetDeskPosition);

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

            // Brings the keyboard and trackpad back (also after the STOP gesture hid them) under the
            // current heading, each facing the eyes.
            SetDeskVisible(true);
            WorldUi.PlaceInFront((RectTransform)m_Keyboard.transform, m_Core.Head, k_KeyboardDistance, k_DeskPitch, 0f);
            if (m_Trackpad != null)
                WorldUi.PlaceInFront((RectTransform)m_Trackpad.transform, m_Core.Head, k_KeyboardDistance, k_DeskPitch, k_TrackpadYaw);
        }

        // Puts a panel at yaw/pitch degrees from the demo's home heading, facing the head.
        void PlaceAround(Transform panel, float distance, float yawDegrees, float pitchDegrees)
        {
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

        public void CreateNewScreen(string title)
        {
            if (m_Core == null)
                return;

            // The first free slot: left and right of the first screen, then a row above.
            int slot = 0;
            while (m_ScreenSlots.ContainsValue(slot))
                slot++;
            float yaw = k_ScreenSlotYaws[slot % k_ScreenSlotYaws.Length];
            float pitch = k_ScreenPitch + (slot / k_ScreenSlotYaws.Length) * k_ScreenRowPitch;

            var screen = VirtualScreen.Create(transform, m_Core.Head, title, Vector3.zero, Quaternion.identity, k_ScreenDistance);
            PlaceAround(screen.transform, k_ScreenDistance, yaw, pitch);

            screen.Focused += SetActiveScreen;
            screen.Closed += OnScreenClosed;

            m_Screens.Add(screen);
            m_ScreenSlots[screen] = slot;
            SetActiveScreen(screen);
        }

        public void CreatePcDesktopScreen()
        {
            CreateNewScreen("Pulpit PC (Monitor 4K)");
            if (m_ActiveScreen != null)
            {
                m_ActiveScreen.EnableRemoteDesktop("192.168.1.100");
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

        void ToggleVoiceDictation()
        {
            if (m_Voice != null)
                m_Voice.ToggleRecording();
        }

        void OnSpeechRecognized(string transcribedText)
        {
            if (string.IsNullOrWhiteSpace(transcribedText))
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

        void ToggleWorkspaceVisibility() => SetDeskVisible(!m_WorkspaceVisible);

        // The STOP gesture hides only the keyboard and trackpad; the dock stays, and its "Reset biurka"
        // brings them back, so an accidental palm can't leave the user without controls.
        void SetDeskVisible(bool visible)
        {
            m_WorkspaceVisible = visible;
            if (m_Keyboard != null)
                m_Keyboard.gameObject.SetActive(visible);
            if (m_Trackpad != null)
                m_Trackpad.gameObject.SetActive(visible);
        }

        // Support for physical Bluetooth Keyboard & Mouse paired with iPhone 16 Pro
        void Update()
        {
            UpdateHeldPose();

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
                if (mouse.rightButton.wasPressedThisFrame)
                    m_ActiveScreen.ContextClickAtCursor();
            }
        }

        // IPinchHandler: allows grabbing and repositioning virtual screens in 3D space
        public bool TryBeginPinch(GameObject gazeTarget, PinchSource source)
        {
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
