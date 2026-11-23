using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // A virtual glass trackpad projected on the desk surface next to the keyboard.
    // Supports moving the cursor on virtual screens, single finger tap (Left Click),
    // and two-finger tap (Right Click / Context Action) with physical desk haptics.
    public class DeskTrackpad : MonoBehaviour
    {
        const float k_Width = 180f;
        const float k_Height = 130f;
        const float k_Sensitivity = 1.4f;
        const float k_TapThresholdTime = 0.22f;

        HandTracker m_Tracker;
        RectTransform m_CanvasRect;
        VirtualScreen m_TargetScreen;
        AudioSource m_Audio;
        AudioClip m_ClickClip;

        Vector2 m_LastLocalPos;
        bool m_IsTouching;
        float m_TouchStartTime;
        Vector2 m_TouchStartPos;

        public event Action<Vector2> CursorMoved;
        public event Action LeftClicked;
        public event Action RightClicked;

        public static DeskTrackpad Create(Transform parent, HandTracker tracker, VirtualScreen targetScreen, float distance = 0.85f)
        {
            var panel = WorldUi.Panel("Desk Trackpad", parent, new Vector2(k_Width, k_Height), distance,
                M3Theme.SurfaceContainerHighest, 16f);
            var pad = panel.gameObject.AddComponent<DeskTrackpad>();
            pad.Initialize(panel, tracker, targetScreen);
            return pad;
        }

        public void Initialize(RectTransform canvasRect, HandTracker tracker, VirtualScreen targetScreen)
        {
            m_CanvasRect = canvasRect;
            m_Tracker = tracker;
            m_TargetScreen = targetScreen;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_ClickClip = CreateTrackpadClickSound();

            // Trackpad Label & Divider
            M3.Surface("TrackpadOutline", m_CanvasRect, Vector2.zero, new Vector2(k_Width - 4f, k_Height - 4f),
                M3Theme.SurfaceContainer, 14f);
            M3.Text("TrackpadHint", m_CanvasRect, "Gładzik biurkowy", M3Theme.Type.BodyMedium,
                new Vector2(0f, -44f), new Vector2(160f, 20f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Center);
        }

        public void SetTargetScreen(VirtualScreen screen) => m_TargetScreen = screen;

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        void Update()
        {
            if (m_Tracker == null || !m_Tracker.isActiveAndEnabled || !m_Tracker.IsTracking || m_CanvasRect == null)
                return;

            float canvasScale = m_CanvasRect.lossyScale.x;
            if (canvasScale <= 0f)
                return;

            Vector3 indexTipWorld = m_Tracker.Joints[HandGeometry.IndexTip];
            Vector3 localTip = m_CanvasRect.InverseTransformPoint(indexTipWorld);

            float localX = localTip.x;
            float localY = localTip.y;
            float zDistanceMeters = Mathf.Abs(localTip.z * canvasScale);

            // Check if finger is within 3.5cm of the trackpad surface and within its X/Y bounds
            bool isInside = zDistanceMeters < 0.035f &&
                            Mathf.Abs(localX) <= (k_Width / 2f) &&
                            Mathf.Abs(localY) <= (k_Height / 2f);

            if (isInside)
            {
                Vector2 currentPos = new Vector2(localX, localY);

                if (!m_IsTouching)
                {
                    m_IsTouching = true;
                    m_TouchStartTime = Time.unscaledTime;
                    m_TouchStartPos = currentPos;
                    m_LastLocalPos = currentPos;
                }
                else
                {
                    Vector2 delta = (currentPos - m_LastLocalPos) * k_Sensitivity;
                    m_LastLocalPos = currentPos;

                    if (delta.sqrMagnitude > 0.01f)
                    {
                        CursorMoved?.Invoke(delta);
                        if (m_TargetScreen != null)
                            m_TargetScreen.MoveCursorDelta(delta);
                    }
                }
            }
            else
            {
                if (m_IsTouching)
                {
                    // Finger lifted: evaluate tap
                    float touchDuration = Time.unscaledTime - m_TouchStartTime;
                    float touchDistance = Vector2.Distance(m_LastLocalPos, m_TouchStartPos);

                    if (touchDuration < k_TapThresholdTime && touchDistance < 18f)
                    {
                        // Check if two fingers were touching (middle finger tip distance to index finger tip)
                        Vector3 middleTipWorld = m_Tracker.Joints[12]; // Middle finger tip
                        float fingerDistance = Vector3.Distance(indexTipWorld, middleTipWorld);

                        if (fingerDistance < 0.04f)
                        {
                            PlayClick(1.2f);
                            RightClicked?.Invoke();
                            Debug.Log("[DeskTrackpad] Prawy klik (Tap dwoma palcami)");
                        }
                        else
                        {
                            PlayClick(1.0f);
                            LeftClicked?.Invoke();
                            Debug.Log("[DeskTrackpad] Lewy klik (Tap jednym palcem)");
                        }
                    }
                    m_IsTouching = false;
                }
            }
        }

        void PlayClick(float pitch)
        {
            if (m_Audio != null && m_ClickClip != null)
            {
                m_Audio.pitch = pitch;
                m_Audio.PlayOneShot(m_ClickClip, 0.5f);
            }
        }

        static AudioClip CreateTrackpadClickSound()
        {
            const int rate = 44100;
            const float seconds = 0.015f;
            int count = (int)(rate * seconds);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                samples[i] = Mathf.Sin(2f * Mathf.PI * 650f * (i / (float)rate)) * Mathf.Exp(-t * 20f);
            }
            var clip = AudioClip.Create("TrackpadClick", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
