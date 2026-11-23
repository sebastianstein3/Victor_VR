using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // Turns a VirtualScreen into a view of a PC's or Mac's screen over the local Wi-Fi.
    //
    // The computer runs VisionPro/pc_stream/stream_server.py, which serves its screen as JPEG frames
    // (GET /frame.jpg) and, when started with --allow-input, takes clicks and typing (POST /input).
    // This polls one frame at a time, so the frame rate is what the network and JPEG decoding allow
    // (roughly 10-20 fps on good Wi-Fi), not a game-streaming 120 fps; Moonlight/WebRTC would be the
    // next step for that.
    //
    // The computer's address comes from Documents/pulpit.txt on the iPhone (first line: "192.168.1.20"
    // or "192.168.1.20:8765"); the Files app or Finder can edit it. It is created with instructions if
    // missing. iOS asks once for local network access.
    public class RemoteDesktopReceiver : MonoBehaviour
    {
        public const int k_DefaultPort = 8765;
        public const string k_HostFileName = "pulpit.txt";
        const string k_TokenHeader = "X-CXR-Token";
        const float k_RetrySeconds = 2f;
        const int k_TimeoutSeconds = 3;

        string m_HostAddress;
        int m_Port = k_DefaultPort;
        string m_App; // one app's window instead of the whole screen (stream_server.py ?app=)
        string m_Token; // stream_server.py's access token (X-CXR-Token), from pulpit.txt
        bool m_IsConnected;

        RectTransform m_DisplayRect;
        RawImage m_ScreenDisplay;
        TextMeshProUGUI m_StatusText;
        Texture2D m_DesktopTexture;
        Texture2D m_FrameTexture;
        float m_FrameAspect;
        int m_Frames;
        float m_FpsSince;

        public bool IsConnected => m_IsConnected;
        public string HostAddress => m_HostAddress;
        public string App => m_App;
        public static string HostFile => Path.Combine(Application.persistentDataPath, k_HostFileName);

        // host: "ip" or "ip:port"; null reads Documents/pulpit.txt. app: one app's window (as the
        // computer's /windows names it), or null for the whole main screen.
        VirtualScreen m_Screen;
        Vector2 BodyArea => m_Screen != null ? m_Screen.BodySizeNow : VirtualScreen.BodySize;

        public void Initialize(RectTransform parentRect, string host = null, string app = null)
        {
            m_Screen = parentRect.GetComponent<VirtualScreen>();
            if (m_Screen != null)
                m_Screen.Resized += OnWindowResized;
            m_App = string.IsNullOrEmpty(app) ? null : app;
            ParseHost(host ?? ReadHostFile());

            // The picture, inside the window's text area; resized to the computer's aspect once a frame comes
            var screenObj = new GameObject("DesktopRawImage", typeof(RectTransform));
            screenObj.transform.SetParent(parentRect, false);
            m_DisplayRect = (RectTransform)screenObj.transform;
            m_DisplayRect.anchoredPosition = VirtualScreen.BodyCenter;
            m_DisplayRect.sizeDelta = VirtualScreen.BodySize;

            m_ScreenDisplay = screenObj.AddComponent<RawImage>();
            // Only a backdrop for the gaze: it must not take the pinch from the window's buttons
            m_ScreenDisplay.raycastTarget = false;
            screenObj.transform.SetAsFirstSibling();
            m_DesktopTexture = CreatePlaceholderTexture((int)VirtualScreen.BodySize.x, (int)VirtualScreen.BodySize.y);
            m_ScreenDisplay.texture = m_DesktopTexture;

            m_StatusText = M3.Text("DesktopStatus", parentRect, "", M3Theme.Type.BodyMedium,
                VirtualScreen.BodyCenter + new Vector2(0f, -VirtualScreen.BodySize.y / 2f + 14f),
                new Vector2(VirtualScreen.BodySize.x, 20f), new Color(1f, 0.75f, 0.3f), TextAlignmentOptions.Center);
            VirtualScreen.AttachToBodyBottom(m_StatusText.rectTransform, 14f, 20f);
            m_DisplayRect.sizeDelta = BodyArea;

            if (string.IsNullOrEmpty(m_HostAddress))
            {
                SetStatus($"Wpisz IP komputera do Documents/{k_HostFileName} i otwórz ekran PC ponownie");
                return;
            }
            SetStatus($"Łączenie z {m_HostAddress}:{m_Port}{(m_App != null ? " (" + m_App + ")" : "")}...");
#if CXR_WEBRTC
            StartCoroutine(WebRtcThenFrames());
#else
            StartCoroutine(PollFrames());
#endif
        }

        // ---- Address ----

        static string ReadHostFile()
        {
            try
            {
                if (!File.Exists(HostFile))
                {
                    File.WriteAllText(HostFile,
                        "# Adres i token komputera z uruchomionym VisionPro/pc_stream/stream_server.py\n" +
                        "# (serwer wypisuje tę linię przy starcie), np.:\n# 192.168.1.20:8765 AbCdEf123456\n");
                    return null;
                }
                foreach (var raw in File.ReadAllLines(HostFile))
                {
                    var line = raw.Trim();
                    if (line.Length > 0 && !line.StartsWith("#"))
                        return line;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteDesktop] Nie można odczytać {HostFile}: {e.Message}");
            }
            return null;
        }

        void ParseHost(string text) => TryParseHost(text, out m_HostAddress, out m_Port, out m_Token);

        // "ip", "ip:port" or "ip:port token" -> host, port (k_DefaultPort when not given) and token
        // (null when not given); false for an empty text.
        public static bool TryParseHost(string text, out string host, out int port, out string token)
        {
            host = null;
            port = k_DefaultPort;
            token = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            int space = text.IndexOfAny(new[] { ' ', '\t' });
            if (space > 0)
            {
                token = text.Substring(space + 1).Trim();
                text = text.Substring(0, space);
            }
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out var parsed) && parsed > 0 && parsed < 65536)
            {
                port = parsed;
                text = text.Substring(0, colon);
            }
            host = text;
            return true;
        }

        string Url(string path) => $"http://{m_HostAddress}:{m_Port}{path}";

        string FrameUrl => Url("/frame.jpg") + (m_App != null ? "?app=" + UnityWebRequest.EscapeURL(m_App) : "");

        // ---- The computer's app windows (for the app list) ----

        [Serializable]
        class WindowInfo
        {
            public string app;
            public string title;
        }

        [Serializable]
        class WindowList
        {
            public List<WindowInfo> windows = new();
        }

        // The names of the apps with a visible window on the computer in Documents/pulpit.txt.
        public static IEnumerator FetchApps(Action<List<string>> done, Action<string> failed)
        {
            if (!TryParseHost(ReadHostFile(), out var host, out var port, out var token))
            {
                failed?.Invoke($"Wpisz IP komputera do Documents/{k_HostFileName}");
                yield break;
            }
            using (var request = UnityWebRequest.Get($"http://{host}:{port}/windows"))
            {
                request.timeout = k_TimeoutSeconds;
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader(k_TokenHeader, token);
                yield return request.SendWebRequest();
                if (request.responseCode == 401)
                {
                    failed?.Invoke("Zły lub brak tokenu: wpisz do pulpit.txt całą linię wypisaną przez serwer");
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed?.Invoke($"Brak połączenia z {host} (czy stream_server.py działa?)");
                    yield break;
                }
                var list = JsonUtility.FromJson<WindowList>(request.downloadHandler.text);
                var apps = new List<string>();
                if (list?.windows != null)
                {
                    foreach (var window in list.windows)
                    {
                        if (!string.IsNullOrEmpty(window.app) && !apps.Contains(window.app))
                            apps.Add(window.app);
                    }
                }
                done?.Invoke(apps);
            }
        }

#if CXR_WEBRTC
        // Smooth, encrypted video when the computer offers it; JPEG frames otherwise or if it drops
        IEnumerator WebRtcThenFrames()
        {
            var rtc = gameObject.AddComponent<WebRtcScreen>();
            string failure = null;
            yield return rtc.Connect(Url(""), m_Token, m_App, ShowVideo, error => failure = error);
            if (rtc.IsConnected)
            {
                SetStatus($"{m_App ?? "PC " + m_HostAddress} | WebRTC");
                while (rtc.IsConnected)
                    yield return new WaitForSecondsRealtime(1f);
                failure = "WebRTC rozłączone";
            }
            Destroy(rtc);
            m_IsConnected = false;
            Debug.Log($"[RemoteDesktop] {failure}; przechodzę na klatki JPEG");
            yield return PollFrames();
        }

        void ShowVideo(Texture texture)
        {
            if (texture == null)
                return;
            m_IsConnected = true;
            m_ScreenDisplay.texture = texture;
            float aspect = texture.width / (float)Mathf.Max(1, texture.height);
            if (!Mathf.Approximately(aspect, m_FrameAspect))
            {
                m_FrameAspect = aspect;
                m_DisplayRect.sizeDelta = FitInside(aspect, BodyArea);
            }
        }
#endif

        // The window was stretched: fit the picture into its new text area (centred at the same place)
        void OnWindowResized(VirtualScreen screen) =>
            m_DisplayRect.sizeDelta = m_FrameAspect > 0f ? FitInside(m_FrameAspect, BodyArea) : BodyArea;

        // ---- Frames ----

        IEnumerator PollFrames()
        {
            m_FpsSince = Time.unscaledTime;
            bool warned = false;
            while (true)
            {
                bool ok;
                string error;
                long code;
                using (var request = UnityWebRequest.Get(FrameUrl))
                {
                    request.timeout = k_TimeoutSeconds;
                    AddToken(request);
                    yield return request.SendWebRequest();
                    ok = request.result == UnityWebRequest.Result.Success && ShowFrame(request.downloadHandler.data);
                    error = request.error;
                    code = request.responseCode;
                }

                if (ok)
                {
                    if (!m_IsConnected)
                        Debug.Log($"[RemoteDesktop] Połączono z {m_HostAddress}:{m_Port}");
                    m_IsConnected = true;
                    warned = false;
                    CountFrame();
                    continue; // next frame right away
                }

                if (!warned)
                    Debug.LogWarning($"[RemoteDesktop] {FrameUrl}: {error}");
                warned = true;
                m_IsConnected = false;
                if (code == 401)
                {
                    SetStatus("Zły lub brak tokenu: wpisz do pulpit.txt całą linię wypisaną przez serwer");
                    yield return new WaitForSecondsRealtime(k_RetrySeconds);
                    continue;
                }
                SetStatus(m_App != null
                    ? $"Brak okna {m_App} na {m_HostAddress} (zamknięte, zminimalizowane lub brak połączenia)"
                    : $"Brak połączenia z {m_HostAddress}:{m_Port} (czy stream_server.py działa?)");
                yield return new WaitForSecondsRealtime(k_RetrySeconds);
            }
        }

        bool ShowFrame(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0)
                return false;
            if (m_FrameTexture == null)
                m_FrameTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!m_FrameTexture.LoadImage(jpeg, false))
                return false;

            m_ScreenDisplay.texture = m_FrameTexture;
            float aspect = m_FrameTexture.width / (float)Mathf.Max(1, m_FrameTexture.height);
            if (!Mathf.Approximately(aspect, m_FrameAspect))
            {
                m_FrameAspect = aspect;
                m_DisplayRect.sizeDelta = FitInside(aspect, BodyArea);
            }
            return true;
        }

        // The largest size of the given aspect that fits in the area (letterboxed, never stretched).
        public static Vector2 FitInside(float aspect, Vector2 area)
        {
            if (aspect <= 0f)
                return area;
            return area.x / area.y > aspect ? new Vector2(area.y * aspect, area.y) : new Vector2(area.x, area.x / aspect);
        }

        void CountFrame()
        {
            m_Frames++;
            float elapsed = Time.unscaledTime - m_FpsSince;
            if (elapsed >= 1f)
            {
                SetStatus($"{m_App ?? "PC " + m_HostAddress} | {m_Frames / elapsed:F0} kl/s");
                m_Frames = 0;
                m_FpsSince = Time.unscaledTime;
            }
        }

        void SetStatus(string text)
        {
            if (m_StatusText != null)
                m_StatusText.text = "<noparse>" + text + "</noparse>";
        }

        // ---- Input (works when the server runs with --allow-input) ----

        // A click at a point of the window in dp (the VirtualScreen's cursor); false outside the picture.
        public bool Click(Vector2 windowDp, bool right = false)
        {
            if (!TryToScreen(windowDp, out var uv))
                return false;
            Post($"{{\"type\":\"{(right ? "rightclick" : "click")}\",\"x\":{F(uv.x)},\"y\":{F(uv.y)}{AppField}}}");
            return true;
        }

        public void SendText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Post($"{{\"type\":\"text\",\"text\":\"{Escape(text)}\"{AppField}}}");
        }

        public void SendKey(string key) => Post($"{{\"type\":\"key\",\"key\":\"{Escape(key.ToLowerInvariant())}\"{AppField}}}");

        string AppField => m_App != null ? $",\"app\":\"{Escape(m_App)}\"" : "";

        // Kept for older callers: a click at window dp coordinates.
        public void SendInputEvent(string type, float x, float y) => Click(new Vector2(x, y), type == "rightclick");

        // Window dp -> 0..1 across the computer's screen (y from the top)
        public bool TryToScreen(Vector2 windowDp, out Vector2 uv)
        {
            var center = m_DisplayRect.anchoredPosition;
            var size = m_DisplayRect.sizeDelta;
            uv = new Vector2((windowDp.x - (center.x - size.x / 2f)) / size.x,
                1f - (windowDp.y - (center.y - size.y / 2f)) / size.y);
            return m_IsConnected && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        void Post(string json)
        {
            if (!m_IsConnected)
                return;
            StartCoroutine(PostRoutine(json));
        }

        IEnumerator PostRoutine(string json)
        {
            using (var request = new UnityWebRequest(Url("/input"), "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                AddToken(request);
                request.timeout = k_TimeoutSeconds;
                yield return request.SendWebRequest();
                if (request.responseCode == 403)
                    SetStatus("PC tylko podgląd: uruchom stream_server.py z --allow-input");
            }
        }

        void AddToken(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(m_Token))
                request.SetRequestHeader(k_TokenHeader, m_Token);
        }

        static string F(float value) => value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        void OnDestroy()
        {
            if (m_Screen != null)
                m_Screen.Resized -= OnWindowResized;
            if (m_DesktopTexture != null)
                Destroy(m_DesktopTexture);
            if (m_FrameTexture != null)
                Destroy(m_FrameTexture);
        }

        static Texture2D CreatePlaceholderTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var bgColor = new Color(0.11f, 0.13f, 0.18f);
            var taskbarColor = new Color(0.08f, 0.09f, 0.12f);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = y < 24 ? taskbarColor : bgColor;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
