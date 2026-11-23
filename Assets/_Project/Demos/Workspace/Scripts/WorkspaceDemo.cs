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
        const float k_KeyboardDistance = 0.75f;

        CoreServices m_Core;
        readonly List<VirtualScreen> m_Screens = new();
        VirtualScreen m_ActiveScreen;
        DeskKeyboard m_Keyboard;
        DeskTrackpad m_Trackpad;
        SpatialPhoneScreen m_PhoneScreen;
        VoiceDictation m_Voice;
        CinemaDimmer m_Cinema;
        RectTransform m_ControlDock;
        VirtualScreen m_DraggedScreen;
        SpatialGestureDetector m_Gestures;
        M3Button m_VoiceBtn;
        M3Button m_CinemaBtn;
        bool m_WorkspaceVisible = true;

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

            // Cinema Dimmer
            m_Cinema = gameObject.AddComponent<CinemaDimmer>();
            m_Cinema.Initialize(m_Core.Head);

            // Voice Dictation
            m_Voice = gameObject.AddComponent<VoiceDictation>();
            m_Voice.SpeechRecognized += OnSpeechRecognized;
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

            if (m_PhoneScreen != null)
                Destroy(m_PhoneScreen.gameObject);

            if (m_Keyboard != null)
                Destroy(m_Keyboard.gameObject);

            if (m_Trackpad != null)
                Destroy(m_Trackpad.gameObject);

            if (m_ControlDock != null)
                Destroy(m_ControlDock.gameObject);

            // These components sit on this demo's own GameObject: destroy the components, not the object.
            if (m_Cinema != null)
                Destroy(m_Cinema);

            if (m_Voice != null)
            {
                m_Voice.SpeechRecognized -= OnSpeechRecognized;
                m_Voice.RecordingStarted -= OnVoiceStarted;
                m_Voice.RecordingEnded -= OnVoiceEnded;
                Destroy(m_Voice);
            }
        }

        void BuildControlDock()
        {
            // Floating control dock below eye level (pitch -22 deg, distance 1.2m)
            m_ControlDock = WorldUi.Panel("Workspace Dock", transform, new Vector2(680f, 64f), 1.2f,
                M3Theme.SurfaceContainerHighest, 32f);

            M3.Button("NewScreenBtn", m_ControlDock, "+ Monitor", M3Button.Variant.Filled,
                new Vector2(-260f, 0f), () => CreateNewScreen($"Ekran {m_Screens.Count + 1}"), "view_in_ar", 100f);

            M3.Button("PcScreenBtn", m_ControlDock, "+ Pulpit PC", M3Button.Variant.Tonal,
                new Vector2(-155f, 0f), CreatePcDesktopScreen, "aspect_ratio", 110f);

            M3.Button("NewPhoneBtn", m_ControlDock, "Smartfon", M3Button.Variant.Filled,
                new Vector2(-40f, 0f), SpawnPhoneScreen, "smartphone", 110f);

            m_VoiceBtn = M3.Button("VoiceBtn", m_ControlDock, "G\u0142os", M3Button.Variant.Outlined,
                new Vector2(65f, 0f), ToggleVoiceDictation, "edit", 90f);

            m_CinemaBtn = M3.Button("CinemaBtn", m_ControlDock, "Kino", M3Button.Variant.Outlined,
                new Vector2(160f, 0f), ToggleCinemaMode, "movie", 90f);

            M3.Button("ResetDeskBtn", m_ControlDock, "Reset Biurka", M3Button.Variant.Tonal,
                new Vector2(265f, 0f), ResetDeskPosition, "touch_app", 110f);

            WorldUi.PlaceInFront(m_ControlDock, m_Core.Head, 1.2f, -22f, 0f);
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
        }

        void ResetDeskPosition()
        {
            if (m_Keyboard == null || m_Core == null)
                return;

            Vector3 forwardFlat = Vector3.ProjectOnPlane(m_Core.Head.forward, Vector3.up).normalized;
            if (forwardFlat.sqrMagnitude < 0.01f)
                forwardFlat = Vector3.forward;

            Vector3 rightFlat = Vector3.Cross(Vector3.up, forwardFlat).normalized;

            // Keyboard: centered on desk
            Vector3 kbPos = m_Core.Head.position + forwardFlat * 0.65f - Vector3.up * 0.35f;
            Quaternion kbRot = Quaternion.LookRotation(forwardFlat, Vector3.up) * Quaternion.Euler(25f, 0f, 0f);
            m_Keyboard.SetTransform(kbPos, kbRot);

            // Trackpad: 28 cm to the right of keyboard
            if (m_Trackpad != null)
            {
                Vector3 padPos = kbPos + rightFlat * 0.28f;
                m_Trackpad.SetTransform(padPos, kbRot);
            }
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

            Quaternion yaw = FlatYaw();
            Vector3 phonePos = m_Core.Head.position + yaw * Quaternion.Euler(-5f, 26f, 0f) * Vector3.forward * 1.25f;
            Quaternion phoneRot = Quaternion.LookRotation(phonePos - m_Core.Head.position, Vector3.up);

            m_PhoneScreen = SpatialPhoneScreen.Create(transform, m_Core.Head, phonePos, phoneRot, 1.25f);
            m_PhoneScreen.Closed += _ => m_PhoneScreen = null;
        }

        public void CreateNewScreen(string title)
        {
            if (m_Core == null)
                return;

            float yawOffset = (m_Screens.Count % 3 - 1) * 28f;
            float pitchOffset = (m_Screens.Count / 3) * 12f;

            Quaternion yaw = FlatYaw();
            Vector3 screenPos = m_Core.Head.position + yaw * Quaternion.Euler(-pitchOffset, yawOffset, 0f) * Vector3.forward * k_ScreenDistance;
            Quaternion screenRot = Quaternion.LookRotation(screenPos - m_Core.Head.position, Vector3.up);

            var screen = VirtualScreen.Create(transform, m_Core.Head, title, screenPos, screenRot, k_ScreenDistance);

            screen.Focused += SetActiveScreen;
            screen.Closed += OnScreenClosed;

            m_Screens.Add(screen);
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

        void OnVoiceEnded()
        {
            if (m_VoiceBtn != null)
                m_VoiceBtn.Selected = false;
        }

        void ToggleVoiceDictation()
        {
            if (m_Voice != null)
                m_Voice.ToggleRecording();
        }

        void OnSpeechRecognized(string transcribedText)
        {
            if (m_PhoneScreen != null && m_PhoneScreen.IsChatFocused)
            {
                foreach (char c in transcribedText)
                    m_PhoneScreen.AppendChatChar(c);
            }
            else if (m_ActiveScreen != null)
            {
                m_ActiveScreen.AppendText($"\n[głos-demo] {transcribedText}");
            }
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

        void ToggleWorkspaceVisibility()
        {
            m_WorkspaceVisible = !m_WorkspaceVisible;
            if (m_Keyboard != null)
                m_Keyboard.gameObject.SetActive(m_WorkspaceVisible);
            if (m_Trackpad != null)
                m_Trackpad.gameObject.SetActive(m_WorkspaceVisible);
            if (m_ControlDock != null)
                m_ControlDock.gameObject.SetActive(m_WorkspaceVisible);
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
            }
        }

        // IPinchHandler: allows grabbing and repositioning virtual screens in 3D space
        public bool TryBeginPinch(GameObject gazeTarget, PinchSource source)
        {
            if (gazeTarget == null)
                return false;

            // Check if gaze is on Spatial Phone
            if (m_PhoneScreen != null && (gazeTarget.transform.IsChildOf(m_PhoneScreen.transform) || gazeTarget == m_PhoneScreen.gameObject))
            {
                if (gazeTarget.name.Contains("Grab") || gazeTarget.name.Contains("DynamicIsland"))
                {
                    m_PhoneScreen.StartDragging();
                    return true;
                }
            }

            // Check if gaze is on any screen's grab bar or header
            foreach (var screen in m_Screens)
            {
                if (screen != null && (gazeTarget.transform.IsChildOf(screen.transform) || gazeTarget == screen.gameObject))
                {
                    // Pinching at a screen focuses it, so the keyboard and trackpad go to it.
                    if (screen != m_ActiveScreen)
                        SetActiveScreen(screen);

                    if (gazeTarget.name.Contains("GrabBar") || gazeTarget.name.Contains("Header"))
                    {
                        m_DraggedScreen = screen;
                        screen.StartDragging();
                        return true;
                    }
                }
            }
            return false;
        }

        public void EndPinch()
        {
            if (m_DraggedScreen != null)
            {
                m_DraggedScreen.StopDragging();
                m_DraggedScreen = null;
            }
            if (m_PhoneScreen != null && m_PhoneScreen.IsDragging)
            {
                m_PhoneScreen.StopDragging();
            }
        }
    }
}
