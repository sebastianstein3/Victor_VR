using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Live captions, like visionOS's: what people around say appears as subtitles below the eyes,
    // following the head lazily, and each finished sentence is translated (Polish <-> English) on the
    // phone (Plugins/iOS/CardboardXRTranslate.swift, iOS 26.4+, languages downloaded in Settings).
    // Speech comes from the same recognizer as dictation (VoiceDictation), restarted after each pause
    // since Apple ends a request after about a minute; while captions are on, nothing is typed.
    public class LiveCaptions : MonoBehaviour
    {
        const float k_Distance = 1.2f;
        const float k_Pitch = -12f;
        const float k_FollowDegrees = 20f;
        const int k_Lines = 2;

        Transform m_Head;
        VoiceDictation m_Voice;
        RectTransform m_Panel;
        TextMeshProUGUI m_Caption;
        TextMeshProUGUI m_Translation;
        readonly List<string> m_Said = new();
        readonly List<string> m_Translated = new();
        string m_Partial = "";
        bool m_On;
        bool m_Following;

        public bool IsOn => m_On;
        public bool TranslateEnabled { get; set; } = true;

        public void Initialize(Transform parent, Transform head, VoiceDictation voice)
        {
            m_Head = head;
            m_Voice = voice;
            m_Panel = WorldUi.Panel("Live Captions", parent, new Vector2(760f, 170f), k_Distance,
                new Color(0.05f, 0.05f, 0.07f, 0.85f), 20f);
            m_Caption = M3.Text("CaptionText", m_Panel, "", M3Theme.Type.BodyLarge, new Vector2(0f, 30f),
                new Vector2(720f, 90f), Color.white, TextAlignmentOptions.BottomLeft);
            m_Caption.fontSize = 20f;
            m_Translation = M3.Text("CaptionTranslation", m_Panel, "", M3Theme.Type.BodyLarge, new Vector2(0f, -52f),
                new Vector2(720f, 56f), new Color(1f, 0.85f, 0.4f), TextAlignmentOptions.TopLeft);
            m_Panel.gameObject.SetActive(false);
        }

        public void SetOn(bool on)
        {
            if (on == m_On)
                return;
            m_On = on;
            m_Panel.gameObject.SetActive(on);
            if (on)
            {
                m_Said.Clear();
                m_Translated.Clear();
                m_Partial = "";
                WorldUi.PlaceInFront(m_Panel, m_Head, k_Distance, k_Pitch);
                m_Voice.PartialRecognized += OnPartial;
                m_Voice.SpeechRecognized += OnFinal;
                m_Voice.RecordingEnded += OnEnded;
                m_Voice.Failed += OnFailed;
                Show("Słucham… (" + (Source == "pl" ? "polski → angielski" : "angielski → polski") + ")");
                if (!m_Voice.IsRecording)
                    m_Voice.StartRecording();
            }
            else if (m_Voice != null)
            {
                m_Voice.PartialRecognized -= OnPartial;
                m_Voice.SpeechRecognized -= OnFinal;
                m_Voice.RecordingEnded -= OnEnded;
                m_Voice.Failed -= OnFailed;
                if (m_Voice.IsRecording)
                    m_Voice.StopRecording();
            }
        }

        string Source => m_Voice.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "pl";
        string Target => Source == "pl" ? "en" : "pl";

        void OnPartial(string text)
        {
            m_Partial = text;
            ShowCaption();
        }

        void OnFinal(string text)
        {
            m_Partial = "";
            if (string.IsNullOrWhiteSpace(text))
                return;
            Push(m_Said, text);
            ShowCaption();
            if (TranslateEnabled)
                RequestTranslation(text);
        }

        // Each request ends after a pause or ~a minute: listen again
        void OnEnded()
        {
            if (m_On)
                StartCoroutine(Restart());
        }

        IEnumerator Restart()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (m_On && !m_Voice.IsRecording)
                m_Voice.StartRecording();
        }

        void OnFailed(string message)
        {
            Show("<noparse>" + message + "</noparse>");
        }

        void ShowCaption()
        {
            var text = string.Join("\n", m_Said);
            if (!string.IsNullOrEmpty(m_Partial))
                text += (text.Length > 0 ? "\n" : "") + "<color=#BBBBBB>" + Escape(m_Partial) + "</color>";
            m_Caption.text = text;
        }

        void Show(string message) => m_Caption.text = message;

        static void Push(List<string> lines, string text)
        {
            lines.Add(Escape(text));
            while (lines.Count > k_Lines)
                lines.RemoveAt(0);
        }

        static string Escape(string text) => "<noparse>" + text.Replace("</noparse>", "") + "</noparse>";

        void Update()
        {
            if (!m_On || m_Head == null)
                return;
            // Lazy follow: stays put while the head turns a little, glides back when it turns further
            var flat = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            var toPanel = Vector3.ProjectOnPlane(m_Panel.position - m_Head.position, Vector3.up);
            if (!m_Following && Vector3.Angle(flat, toPanel) > k_FollowDegrees)
                m_Following = true;
            if (m_Following)
            {
                var yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized) : Quaternion.identity;
                var target = m_Head.position + yaw * Quaternion.Euler(-k_Pitch, 0f, 0f) * Vector3.forward * k_Distance;
                m_Panel.position = Vector3.Lerp(m_Panel.position, target, Time.unscaledDeltaTime * 4f);
                m_Panel.rotation = Quaternion.LookRotation(m_Panel.position - m_Head.position, Vector3.up);
                if (Vector3.Distance(m_Panel.position, target) < 0.01f)
                    m_Following = false;
            }
        }

        void OnDestroy()
        {
            SetOn(false);
            if (m_Panel != null)
                Destroy(m_Panel.gameObject);
            s_Pending.Remove(this);
        }

        // ---- Translation ----

        delegate void TranslateCallback(int requestId, IntPtr text, int ok);
        static readonly Dictionary<int, LiveCaptions> s_Requests = new();
        static readonly HashSet<LiveCaptions> s_Pending = new();
        static int s_NextRequest = 1;
        // Kept in a static field so the delegate handed to native code is never collected
        static readonly TranslateCallback s_Callback = OnTranslated;

        void RequestTranslation(string text)
        {
#if UNITY_IOS && !UNITY_EDITOR
            int id = s_NextRequest++;
            s_Requests[id] = this;
            s_Pending.Add(this);
            CardboardXR_Translate(id, text, Source, Target, s_Callback);
#else
            Push(m_Translated, "(tłumaczenie działa na iPhonie)");
            m_Translation.text = string.Join("\n", m_Translated);
#endif
        }

        [AOT.MonoPInvokeCallback(typeof(TranslateCallback))]
        static void OnTranslated(int requestId, IntPtr text, int ok)
        {
            if (!s_Requests.TryGetValue(requestId, out var owner))
                return;
            s_Requests.Remove(requestId);
            if (owner == null || !owner.m_On)
                return;
            string result = Marshal.PtrToStringUTF8(text) ?? "";
            Push(owner.m_Translated, ok == 1 ? result : "(" + result + ")");
            owner.m_Translation.text = string.Join("\n", owner.m_Translated);
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CardboardXR_Translate(int requestId, string text, string source, string target, TranslateCallback callback);
#endif
    }
}
