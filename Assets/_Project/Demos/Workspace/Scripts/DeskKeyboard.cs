using System;
using System.Collections.Generic;
using CardboardXR.Hands;
using CardboardXR.Interaction;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // A virtual keyboard that can rest on a detected table surface or float at desk height.
    // Supports gaze-and-pinch typing, as well as natural finger tapping via hand tracking (IndexTip).
    public class DeskKeyboard : MonoBehaviour
    {
        const float k_KeyWidth = 48f;
        const float k_KeyHeight = M3.k_ButtonHeight; // the buttons are this tall, so the touch area matches them
        const float k_KeyGap = 6f;
        const float k_DebounceTime = 0.22f;

        static readonly string[] s_Row1 = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
        static readonly string[] s_Row2 = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
        static readonly string[] s_Row3 = { "Z", "X", "C", "V", "B", "N", "M", ",", "." };

        struct KeyHitbox
        {
            public Rect rect;
            public string key;
            public Action action;
        }

        HandTracker m_Tracker;
        RectTransform m_CanvasRect;
        AudioSource m_Audio;
        AudioClip m_ClickClip;
        bool m_Shift;
        float m_LastPressTime;
        // The key the fingertip is resting on, so one tap types once (no key repeat while the finger stays in the plane).
        string m_HeldKey;
        readonly List<KeyHitbox> m_Hitboxes = new();
        readonly List<M3Button> m_KeyButtons = new();

        public event Action<char> KeyPressed;
        public event Action BackspacePressed;
        public event Action EnterPressed;
        public event Action ClearPressed;

        // When the fingertip last pressed a key (Time.unscaledTime), so other input can stay quiet while typing.
        public float LastTouchTime => m_LastPressTime;

        public bool Shift
        {
            get => m_Shift;
            set
            {
                m_Shift = value;
                UpdateKeyLabels();
            }
        }

        public static DeskKeyboard Create(Transform parent, HandTracker tracker, float distance = 0.85f)
        {
            var panel = WorldUi.Panel("Desk Keyboard", parent, new Vector2(640f, 240f), distance,
                M3Theme.SurfaceContainerHigh, 24f);
            var kb = panel.gameObject.AddComponent<DeskKeyboard>();
            kb.Initialize(panel, tracker);
            return kb;
        }

        public void Initialize(RectTransform canvasRect, HandTracker tracker)
        {
            m_CanvasRect = canvasRect;
            m_Tracker = tracker;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_ClickClip = CreateClickSound();

            BuildKeyboard();
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        void BuildKeyboard()
        {
            m_Hitboxes.Clear();
            m_KeyButtons.Clear();

            // Row 1: Q..P + Backspace
            float y = 90f;
            float startX = -((11 * (k_KeyWidth + k_KeyGap)) - k_KeyGap) / 2f + (k_KeyWidth / 2f);
            for (int i = 0; i < s_Row1.Length; i++)
            {
                var k = s_Row1[i];
                AddKey(m_Shift ? k : k.ToLowerInvariant(), new Vector2(startX + i * (k_KeyWidth + k_KeyGap), y), k_KeyWidth, () => OnKeyClick(k[0]));
            }
            // Backspace
            AddKey("Del", new Vector2(startX + 10 * (k_KeyWidth + k_KeyGap), y), k_KeyWidth, OnBackspaceClick,
                M3Button.Variant.Tonal);

            // Row 2: A..L + Enter
            y = 30f;
            float row2Width = 10 * (k_KeyWidth + k_KeyGap) + 16f;
            startX = -(row2Width / 2f) + (k_KeyWidth / 2f);
            for (int i = 0; i < s_Row2.Length; i++)
            {
                var k = s_Row2[i];
                AddKey(m_Shift ? k : k.ToLowerInvariant(), new Vector2(startX + i * (k_KeyWidth + k_KeyGap), y), k_KeyWidth, () => OnKeyClick(k[0]));
            }
            // Enter
            float enterWidth = k_KeyWidth * 1.35f;
            AddKey("Enter", new Vector2(startX + 9 * (k_KeyWidth + k_KeyGap) + 8f, y), enterWidth, OnEnterClick,
                M3Button.Variant.Filled);

            // Row 3: Shift + Z..M + . + ?
            y = -30f;
            float shiftWidth = k_KeyWidth * 1.2f;
            startX = -270f + (shiftWidth / 2f);
            AddKey("Shift", new Vector2(startX, y), shiftWidth, () => Shift = !Shift,
                m_Shift ? M3Button.Variant.Filled : M3Button.Variant.Outlined);
            float currentX = startX + (shiftWidth / 2f) + k_KeyGap + (k_KeyWidth / 2f);
            for (int i = 0; i < s_Row3.Length; i++)
            {
                var k = s_Row3[i];
                AddKey(m_Shift ? k : k.ToLowerInvariant(), new Vector2(currentX + i * (k_KeyWidth + k_KeyGap), y), k_KeyWidth, () => OnKeyClick(k[0]));
            }

            // Row 4: Space + Clear
            y = -90f;
            float spaceWidth = 320f;
            AddKey("SPACJA", new Vector2(0f, y), spaceWidth, () => OnKeyClick(' '), M3Button.Variant.Tonal);
            AddKey("Wyczyść", new Vector2(230f, y), 90f, OnClearClick, M3Button.Variant.Outlined);
        }

        void AddKey(string label, Vector2 pos, float width, Action action,
            M3Button.Variant variant = M3Button.Variant.Filled)
        {
            var btn = M3.Button($"Key_{label}", m_CanvasRect, label, variant, pos, () => action(), null, width);
            m_KeyButtons.Add(btn);

            m_Hitboxes.Add(new KeyHitbox
            {
                rect = new Rect(pos.x - width / 2f, pos.y - k_KeyHeight / 2f, width, k_KeyHeight),
                key = label,
                action = action
            });
        }

        void UpdateKeyLabels()
        {
            // Rebuild keyboard with appropriate shift casing
            foreach (Transform child in m_CanvasRect)
            {
                if (child.name.StartsWith("Key_"))
                    Destroy(child.gameObject);
            }
            BuildKeyboard();
        }

        void Update()
        {
            if (m_Tracker == null || !m_Tracker.isActiveAndEnabled || !m_Tracker.IsTracking)
            {
                m_HeldKey = null; // the hand is gone, so it isn't resting on a key any more
                return;
            }

            // Finger tap detection with IndexTip
            var indexTipWorld = m_Tracker.Joints[HandGeometry.IndexTip];
            var localTip = m_CanvasRect.InverseTransformPoint(indexTipWorld);

            // In local canvas units (dp)
            float canvasScale = m_CanvasRect.lossyScale.x;
            if (canvasScale <= 0f)
                return;

            float zDistanceMeters = Mathf.Abs(localTip.z * canvasScale);

            // The fingertip has to be touching/penetrating within 3.5 cm of the keyboard plane...
            string touched = null;
            Action touchedAction = null;
            if (zDistanceMeters < 0.035f)
            {
                var point = new Vector2(localTip.x, localTip.y);
                foreach (var hb in m_Hitboxes)
                {
                    if (hb.rect.Contains(point))
                    {
                        touched = hb.key;
                        touchedAction = hb.action;
                        break;
                    }
                }
            }

            // ...and it types only when it arrives on a key, not while it rests there.
            if (touched == null)
            {
                m_HeldKey = null;
                return;
            }
            if (touched == m_HeldKey || Time.unscaledTime - m_LastPressTime < k_DebounceTime)
                return;

            m_HeldKey = touched;
            m_LastPressTime = Time.unscaledTime;
            touchedAction?.Invoke();
        }

        void OnDestroy()
        {
            if (m_ClickClip != null)
                Destroy(m_ClickClip);
        }

        void OnKeyClick(char c)
        {
            char output = m_Shift ? char.ToUpper(c) : char.ToLower(c);
            PlayClick();
            KeyPressed?.Invoke(output);
        }

        void OnBackspaceClick()
        {
            PlayClick();
            BackspacePressed?.Invoke();
        }

        void OnEnterClick()
        {
            PlayClick();
            EnterPressed?.Invoke();
        }

        void OnClearClick()
        {
            PlayClick();
            ClearPressed?.Invoke();
        }

        void PlayClick()
        {
            if (m_Audio != null && m_ClickClip != null)
                m_Audio.PlayOneShot(m_ClickClip, 0.45f);
        }

        // Procedural crisp mechanical click sound
        static AudioClip CreateClickSound()
        {
            const int rate = 44100;
            const float seconds = 0.025f; // 25ms crisp click
            int count = (int)(rate * seconds);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float freq = Mathf.Lerp(1200f, 400f, t);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)rate)) * Mathf.Exp(-t * 12f);
            }
            var clip = AudioClip.Create("KeyClick", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
