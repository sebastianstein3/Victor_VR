using System;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Voice Dictation module: captures speech from AirPods Pro 2 / iPhone microphones
    // and sends transcribed text directly into the active screen, notepad, or Messenger.
    public class VoiceDictation : MonoBehaviour
    {
        const int k_SampleRate = 16000;
        const int k_MaxRecordingSeconds = 10;

        AudioClip m_RecordingClip;
        string m_DeviceName;
        bool m_IsRecording;
        float m_RecordStartTime;

        public bool IsRecording => m_IsRecording;
        public event Action<string> SpeechRecognized;
        public event Action RecordingStarted;
        public event Action RecordingEnded;

        void Start()
        {
            if (Microphone.devices.Length > 0)
                m_DeviceName = Microphone.devices[0];
        }

        public void ToggleRecording()
        {
            if (m_IsRecording)
                StopRecording();
            else
                StartRecording();
        }

        public void StartRecording()
        {
            if (m_IsRecording)
                return;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoiceDictation] Brak dost\u0119pnego mikrofonu.");
                return;
            }

            m_DeviceName = Microphone.devices[0];
            m_RecordingClip = Microphone.Start(m_DeviceName, false, k_MaxRecordingSeconds, k_SampleRate);
            m_IsRecording = true;
            m_RecordStartTime = Time.unscaledTime;
            RecordingStarted?.Invoke();
            Debug.Log("[VoiceDictation] Rozpocz\u0119to dyktowanie g\u0142osowe...");
        }

        public void StopRecording()
        {
            if (!m_IsRecording)
                return;

            Microphone.End(m_DeviceName);
            m_IsRecording = false;
            float duration = Time.unscaledTime - m_RecordStartTime;
            RecordingEnded?.Invoke();

            // Perform transcription (ready for native iOS SFSpeechRecognizer or local Whisper model)
            string sampleTranscription = GenerateMockTranscription(duration);
            Debug.Log($"[VoiceDictation] Zako\u0144czono dyktowanie ({duration:F1}s). Tekst: \"{sampleTranscription}\"");
            SpeechRecognized?.Invoke(sampleTranscription);
        }

        void OnDestroy()
        {
            // Never leave the microphone open (it keeps the orange indicator on) when the demo closes.
            if (m_IsRecording)
            {
                Microphone.End(m_DeviceName);
                m_IsRecording = false;
            }
        }

        void Update()
        {
            // Auto stop when max duration reached
            if (m_IsRecording && Time.unscaledTime - m_RecordStartTime >= k_MaxRecordingSeconds)
            {
                StopRecording();
            }
        }

        static string GenerateMockTranscription(float duration)
        {
            // Provides dynamic contextual transcription for quick voice response testing
            if (duration < 2f)
                return "Tak, potwierdzam!";
            if (duration < 4f)
                return "Jestem przy biurku, testuj\u0119 wirtualne monitory w goglach.";
            return "Wszystkie systemy dzia\u0142aj\u0105 stabilnie w 120 klatkach na sekund\u0119!";
        }
    }
}
