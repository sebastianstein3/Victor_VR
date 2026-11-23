using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Voice dictation for the Spatial Workspace.
    //
    // On an iPhone it is real: Apple's speech recognizer (SFSpeechRecognizer) listens through the
    // iPhone's or the AirPods' microphone (Plugins/iOS/CardboardXRSpeech.mm), Polish by default, and
    // the finished text goes to SpeechRecognized. Everywhere else (the Unity Editor, Android) there is
    // no recognizer, so it records nothing and returns a canned sentence, and IsMock says so.
    public class VoiceDictation : MonoBehaviour
    {
        // The language of the speech to recognize (an iOS locale such as "en-US").
        public const string k_DefaultLocale = "pl-PL";

        const int k_MaxRecordingSeconds = 55; // Apple ends a recognition request after about a minute
        const string k_ReceiverName = "VoiceDictationNative"; // the GameObject the native plugin messages

        bool m_IsRecording;
        float m_RecordStartTime;
        string m_Locale = k_DefaultLocale;

        public bool IsRecording => m_IsRecording;
        public string Locale { get => m_Locale; set => m_Locale = string.IsNullOrEmpty(value) ? k_DefaultLocale : value; }

        // Allow Apple's servers to help (better accuracy, needs internet) or keep everything on the
        // phone (offline, needs the language downloaded).
        public bool OnDeviceOnly { get; set; }

        // True when nothing is really recognized (see the class comment).
        public static bool IsMock =>
#if UNITY_IOS && !UNITY_EDITOR
            false;
#else
            true;
#endif

        public event Action<string> SpeechRecognized;   // the finished text
        public event Action<string> PartialRecognized;  // the text so far, while still talking
        public event Action<string> Failed;             // a short message the person can read
        public event Action RecordingStarted;
        public event Action RecordingEnded;

        public void ToggleRecording()
        {
            if (m_IsRecording)
                StopRecording();
            else
                StartRecording();
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CardboardXR_SpeechRequestAuthorization();
        [DllImport("__Internal")] static extern int CardboardXR_SpeechStart(string localeId, int onDeviceOnly);
        [DllImport("__Internal")] static extern void CardboardXR_SpeechStop();

        // iOS asks for the speech and microphone permissions once; until it has said yes, a start waits.
        enum Auth { Unknown, Asking, Granted, Denied }
        static Auth s_Auth = Auth.Unknown;
        bool m_StartWhenGranted;
        bool m_Stopping;
        string m_LastText = "";
        bool m_GotFinal;
        Receiver m_Receiver;

        void Awake()
        {
            // The native plugin finds its receiver by this GameObject's name.
            var existing = GameObject.Find(k_ReceiverName);
            if (existing != null)
                Destroy(existing);
            var go = new GameObject(k_ReceiverName);
            go.transform.SetParent(transform, false);
            m_Receiver = go.AddComponent<Receiver>();
            m_Receiver.Owner = this;
        }

        public void StartRecording()
        {
            if (m_IsRecording || m_Stopping)
                return;

            if (s_Auth == Auth.Denied)
            {
                Failed?.Invoke("Brak zgody na mikrofon lub rozpoznawanie mowy (Ustawienia > CardboardXR).");
                return;
            }
            if (s_Auth != Auth.Granted)
            {
                m_StartWhenGranted = true;
                if (s_Auth != Auth.Asking)
                {
                    s_Auth = Auth.Asking;
                    CardboardXR_SpeechRequestAuthorization();
                }
                return;
            }
            BeginNative();
        }

        void BeginNative()
        {
            m_LastText = "";
            m_GotFinal = false;
            if (CardboardXR_SpeechStart(m_Locale, OnDeviceOnly ? 1 : 0) == 0)
                return; // the plugin reported why through OnNativeError
        }

        public void StopRecording()
        {
            if (!m_IsRecording || m_Stopping)
                return;
            m_Stopping = true;
            CardboardXR_SpeechStop(); // the final text and "ended" arrive in the receiver
        }

        void Update()
        {
            if (m_IsRecording && !m_Stopping && Time.unscaledTime - m_RecordStartTime >= k_MaxRecordingSeconds)
                StopRecording();
        }

        void OnDestroy()
        {
            if (m_IsRecording)
                CardboardXR_SpeechStop();
        }

        // Called by the receiver for what the native plugin sends.
        void HandleState(string state)
        {
            switch (state)
            {
                case "auth:ok":
                    s_Auth = Auth.Granted;
                    if (m_StartWhenGranted)
                    {
                        m_StartWhenGranted = false;
                        BeginNative();
                    }
                    break;
                case "auth:denied":
                    s_Auth = Auth.Denied;
                    m_StartWhenGranted = false;
                    Failed?.Invoke("Brak zgody na mikrofon lub rozpoznawanie mowy (Ustawienia > CardboardXR).");
                    break;
                case "started":
                    m_IsRecording = true;
                    m_Stopping = false;
                    m_RecordStartTime = Time.unscaledTime;
                    RecordingStarted?.Invoke();
                    Debug.Log($"[VoiceDictation] Dyktowanie ({m_Locale}) rozpoczęte.");
                    break;
                case "ended":
                    var wasRecording = m_IsRecording;
                    m_IsRecording = false;
                    m_Stopping = false;
                    // If no final result came (the request was cut short), use the last partial text.
                    if (!m_GotFinal && !string.IsNullOrWhiteSpace(m_LastText))
                        SpeechRecognized?.Invoke(m_LastText);
                    m_GotFinal = true;
                    if (wasRecording)
                        RecordingEnded?.Invoke();
                    break;
            }
        }

        void HandlePartial(string text)
        {
            m_LastText = text;
            PartialRecognized?.Invoke(text);
        }

        void HandleFinal(string text)
        {
            m_GotFinal = true;
            m_LastText = text;
            Debug.Log($"[VoiceDictation] Tekst: \"{text}\"");
            if (!string.IsNullOrWhiteSpace(text))
                SpeechRecognized?.Invoke(text);
        }

        void HandleError(string message)
        {
            Debug.LogWarning($"[VoiceDictation] {message}");
            Failed?.Invoke(message);
        }

        // UnitySendMessage can only reach a MonoBehaviour by GameObject name, so the plugin talks to this.
        class Receiver : MonoBehaviour
        {
            public VoiceDictation Owner;
            void OnNativeState(string state) { if (Owner != null) Owner.HandleState(state); }
            void OnNativePartial(string text) { if (Owner != null) Owner.HandlePartial(text); }
            void OnNativeFinal(string text) { if (Owner != null) Owner.HandleFinal(text); }
            void OnNativeError(string message) { if (Owner != null) Owner.HandleError(message); }
        }
#else
        string m_DeviceName;

        public void StartRecording()
        {
            if (m_IsRecording)
                return;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoiceDictation] Brak dostępnego mikrofonu.");
                Failed?.Invoke("Brak mikrofonu.");
                return;
            }

            m_DeviceName = Microphone.devices[0];
            Microphone.Start(m_DeviceName, false, k_MaxRecordingSeconds, 16000);
            m_IsRecording = true;
            m_RecordStartTime = Time.unscaledTime;
            RecordingStarted?.Invoke();
            Debug.Log("[VoiceDictation] Rozpoczęto dyktowanie (atrapa: poza iOS nic nie jest rozpoznawane).");
        }

        public void StopRecording()
        {
            if (!m_IsRecording)
                return;

            Microphone.End(m_DeviceName);
            m_IsRecording = false;
            float duration = Time.unscaledTime - m_RecordStartTime;
            RecordingEnded?.Invoke();

            string canned = duration < 2f
                ? "Tak, potwierdzam!"
                : duration < 4f
                    ? "Jestem przy biurku, testuję wirtualne monitory w goglach."
                    : "Wszystkie systemy działają stabilnie.";
            SpeechRecognized?.Invoke(canned);
        }

        void Update()
        {
            if (m_IsRecording && Time.unscaledTime - m_RecordStartTime >= k_MaxRecordingSeconds)
                StopRecording();
        }

        void OnDestroy()
        {
            // Never leave the microphone open when the demo closes.
            if (m_IsRecording)
            {
                Microphone.End(m_DeviceName);
                m_IsRecording = false;
            }
        }
#endif
    }
}
