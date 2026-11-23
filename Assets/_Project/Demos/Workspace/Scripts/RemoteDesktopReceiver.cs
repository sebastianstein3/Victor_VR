using System;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // Turns a VirtualScreen into an active Remote Desktop monitor for PC or Mac.
    // Receives video stream from PC over local Wi-Fi and maps mouse/keyboard input.
    public class RemoteDesktopReceiver : MonoBehaviour
    {
        string m_HostAddress = "192.168.1.100";
        int m_Port = 8080;
        bool m_IsConnected;

        RawImage m_ScreenDisplay;
        TextMeshProUGUI m_StatusText;
        Texture2D m_DesktopTexture;

        public bool IsConnected => m_IsConnected;
        public string HostAddress => m_HostAddress;

        public void Initialize(RectTransform parentRect, string host = "192.168.1.100")
        {
            m_HostAddress = host;

            // Display RawImage
            var screenObj = new GameObject("DesktopRawImage", typeof(RectTransform));
            screenObj.transform.SetParent(parentRect, false);
            var rect = (RectTransform)screenObj.transform;
            rect.anchoredPosition = new Vector2(0f, 10f);
            rect.sizeDelta = new Vector2(parentRect.sizeDelta.x - 20f, parentRect.sizeDelta.y - 70f);

            m_ScreenDisplay = screenObj.AddComponent<RawImage>();
            // The picture is only a backdrop: it must not take the gaze/pinch from the Close, Pin,
            // Clear and Grab buttons, and it is drawn under them.
            m_ScreenDisplay.raycastTarget = false;
            screenObj.transform.SetAsFirstSibling();
            m_DesktopTexture = CreateMockDesktopTexture((int)rect.sizeDelta.x, (int)rect.sizeDelta.y);
            m_ScreenDisplay.texture = m_DesktopTexture;

            // Connection Status Overlay
            m_StatusText = M3.Text("DesktopStatus", parentRect, $"TRYB DEMO: brak strumienia z PC ({m_HostAddress}:{m_Port})",
                M3Theme.Type.BodyMedium, new Vector2(0f, -(parentRect.sizeDelta.y / 2f) + 40f),
                new Vector2(400f, 20f), new Color(1f, 0.75f, 0.3f), TextAlignmentOptions.Center);

            // No real stream is implemented yet (see HANDOVER.md, roadmap), so never report "connected".
            m_IsConnected = false;
            Debug.Log($"[RemoteDesktop] Zainicjalizowano odbiornik pulpitu dla PC: {m_HostAddress}");
        }

        void OnDestroy()
        {
            if (m_DesktopTexture != null)
                Destroy(m_DesktopTexture);
        }

        public void SendInputEvent(string type, float x, float y)
        {
            if (!m_IsConnected)
                return;
            // Ready for sending UDP / WebSocket input packets to PC companion app
            Debug.Log($"[RemoteDesktop] Wys\u0142ano zdarzenie: {type} ({x:F1}, {y:F1}) do PC");
        }

        static Texture2D CreateMockDesktopTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var bgColor = new Color(0.11f, 0.13f, 0.18f);
            var taskbarColor = new Color(0.08f, 0.09f, 0.12f);
            var windowColor = new Color(0.18f, 0.20f, 0.27f);

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y < 35) // Taskbar
                        pixels[y * width + x] = taskbarColor;
                    else if (x > 30 && x < width - 30 && y > 50 && y < height - 30) // Active Window
                        pixels[y * width + x] = windowColor;
                    else
                        pixels[y * width + x] = bgColor;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
