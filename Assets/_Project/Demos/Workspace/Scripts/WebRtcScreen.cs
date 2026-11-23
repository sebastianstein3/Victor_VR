#if CXR_WEBRTC
using System;
using System.Collections;
using System.Text;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;

namespace CardboardXR.Demos.Workspace
{
    // The computer's screen (or one app's window) as live WebRTC video from stream_server.py's /offer:
    // smooth (~30 fps) and encrypted (DTLS-SRTP), instead of polling JPEG frames.
    //
    // Compiled only when the com.unity.webrtc package is installed (the Workspace asmdef defines
    // CXR_WEBRTC then); RemoteDesktopReceiver falls back to JPEG frames when it isn't, or when the
    // computer has no aiortc, or the connection fails. Local network only: no STUN/TURN servers.
    public class WebRtcScreen : MonoBehaviour
    {
        const float k_GatherSeconds = 5f;
        const float k_ConnectSeconds = 10f;

        static GameObject s_Updater; // WebRTC.Update must run once, for every video track

        RTCPeerConnection m_Peer;

        public bool IsConnected => m_Peer != null && m_Peer.ConnectionState == RTCPeerConnectionState.Connected;
        public bool HasFailed { get; private set; }

        [Serializable]
        class Offer
        {
            public string sdp;
            public string type;
            public string app;
        }

        [Serializable]
        class Answer
        {
            public string sdp;
            public string type;
        }

        class Updater : MonoBehaviour
        {
            void Start() => StartCoroutine(WebRTC.Update());
        }

        // Connects; calls onVideo with the texture each time the received video's texture changes.
        public IEnumerator Connect(string baseUrl, string token, string app, Action<Texture> onVideo, Action<string> onError)
        {
            if (s_Updater == null)
            {
                s_Updater = new GameObject("WebRTC Update");
                DontDestroyOnLoad(s_Updater);
                s_Updater.AddComponent<Updater>();
            }

            var configuration = default(RTCConfiguration);
            configuration.iceServers = Array.Empty<RTCIceServer>();
            m_Peer = new RTCPeerConnection(ref configuration);
            var transceiver = m_Peer.AddTransceiver(TrackKind.Video);
            transceiver.Direction = RTCRtpTransceiverDirection.RecvOnly;
            m_Peer.OnTrack = trackEvent =>
            {
                if (trackEvent.Track is VideoStreamTrack video)
                    video.OnVideoReceived += texture => onVideo?.Invoke(texture);
            };

            var create = m_Peer.CreateOffer();
            yield return create;
            if (create.IsError)
            {
                Fail(onError, "WebRTC: nie utworzono oferty");
                yield break;
            }
            var offer = create.Desc;
            var setLocal = m_Peer.SetLocalDescription(ref offer);
            yield return setLocal;
            if (setLocal.IsError)
            {
                Fail(onError, "WebRTC: błąd opisu lokalnego");
                yield break;
            }

            // The computer doesn't trickle candidates: send the offer with all of ours
            float gatherUntil = Time.realtimeSinceStartup + k_GatherSeconds;
            while (m_Peer.GatheringState != RTCIceGatheringState.Complete && Time.realtimeSinceStartup < gatherUntil)
                yield return null;

            string body = JsonUtility.ToJson(new Offer { sdp = m_Peer.LocalDescription.sdp, type = "offer", app = app ?? "" });
            using (var request = new UnityWebRequest(baseUrl + "/offer", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader("X-CXR-Token", token);
                request.timeout = 20;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail(onError, request.responseCode == 501
                        ? "Na komputerze brak WebRTC (pip install aiortc numpy): tryb JPEG"
                        : $"WebRTC: {request.error}");
                    yield break;
                }

                var answer = JsonUtility.FromJson<Answer>(request.downloadHandler.text);
                if (answer == null || string.IsNullOrEmpty(answer.sdp))
                {
                    Fail(onError, "WebRTC: pusta odpowiedź");
                    yield break;
                }
                var remote = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answer.sdp };
                var setRemote = m_Peer.SetRemoteDescription(ref remote);
                yield return setRemote;
                if (setRemote.IsError)
                {
                    Fail(onError, "WebRTC: odpowiedź odrzucona");
                    yield break;
                }
            }

            float connectUntil = Time.realtimeSinceStartup + k_ConnectSeconds;
            while (!IsConnected && Time.realtimeSinceStartup < connectUntil)
                yield return null;
            if (!IsConnected)
                Fail(onError, "WebRTC: nie połączono (sieć blokuje połączenia między urządzeniami?)");
        }

        void Fail(Action<string> onError, string message)
        {
            HasFailed = true;
            Debug.LogWarning($"[RemoteDesktop] {message}");
            onError?.Invoke(message);
            Close();
        }

        public void Close()
        {
            if (m_Peer == null)
                return;
            m_Peer.Close();
            m_Peer.Dispose();
            m_Peer = null;
        }

        void OnDestroy() => Close();
    }
}
#endif
