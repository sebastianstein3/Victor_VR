using System.Collections.Generic;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // The status panel beside the hologram, in the style of a Jarvis console: corner brackets, orange
    // section markers, thin bars and a sparkline. All of it is real:
    //   SYSTEMY  the app's frame rate, its memory against the phone's, the battery
    //   SIEĆ     the time of each of the assistant's last answers (a bar per answer)
    //   STATUS   what the assistant is doing, the model, the voice language, how long the app has run
    // Updated four times a second. "Pokaż hud" / "ukryj hud" shows and hides it.
    public class HologramHud : MonoBehaviour
    {
        const float k_Width = 300f;
        const float k_Height = 440f;
        const float k_TrackLeft = -52f;   // where the bars start (dp from the panel's centre)
        const float k_TrackWidth = 120f;
        const int k_Bars = 30;
        static readonly Color k_Cyan = new(0.25f, 0.85f, 1f);
        static readonly Color k_Orange = new(1f, 0.55f, 0.15f);
        static readonly Color k_Dim = new(0.55f, 0.7f, 0.78f);
        static readonly Color k_Text = new(0.82f, 0.95f, 1f);

        public RectTransform Panel { get; private set; }

        AssistantSession m_Session;
        Image m_FpsFill, m_MemoryFill, m_BatteryFill;
        TextMeshProUGUI m_FpsText, m_MemoryText, m_BatteryText;
        TextMeshProUGUI m_StateText, m_ModelText, m_VoiceText, m_UptimeText, m_NetText, m_NotesText;
        readonly List<Image> m_Spark = new();
        float m_Fps = 60f;
        float m_Next;
        float m_FpsY, m_MemoryY, m_BatteryY;

        public static HologramHud Create(Transform parent, AssistantSession session)
        {
            var panel = WorldUi.Panel("Hologram HUD", parent, new Vector2(k_Width, k_Height), 1.0f, new Color(0.02f, 0.07f, 0.1f, 0.5f), 10f);
            var hud = panel.gameObject.AddComponent<HologramHud>();
            hud.Panel = panel;
            hud.m_Session = session;
            hud.Build();
            return hud;
        }

        // ---- Building (dp from the panel's centre, y up) ----

        void Build()
        {
            float left = -k_Width / 2f, top = k_Height / 2f;
            Corners();

            Section("SYSTEMY", top - 28f, "NA ŻYWO");
            m_FpsY = top - 62f;
            m_MemoryY = top - 92f;
            m_BatteryY = top - 122f;
            Row("FPS", m_FpsY, out m_FpsFill, out m_FpsText);
            Row("RAM", m_MemoryY, out m_MemoryFill, out m_MemoryText);
            Row("BATERIA", m_BatteryY, out m_BatteryFill, out m_BatteryText);

            Section("SIEĆ", top - 160f, null);
            m_NetText = Label("NetState", "", new Vector2(left + 28f, top - 184f), 200f, TextAlignmentOptions.Left, k_Dim);
            for (int i = 0; i < k_Bars; i++)
            {
                var bar = M3.Surface($"Spark_{i}", Panel, new Vector2(left + 22f + i * 8.7f, top - 226f), new Vector2(5f, 3f), k_Cyan, 1f);
                m_Spark.Add(bar);
            }

            Section("STATUS", top - 262f, null);
            m_StateText = Pair("STAN", top - 292f);
            m_ModelText = Pair("MODEL", top - 318f);
            m_VoiceText = Pair("GŁOS", top - 344f);
            m_UptimeText = Pair("CZAS PRACY", top - 370f);
            m_NotesText = Pair("NOTATKI", top - 396f);
            Refresh();
        }

        // Four corner brackets and a thin frame, like the reference's panels
        void Corners()
        {
            float x = k_Width / 2f - 2f, y = k_Height / 2f - 2f;
            var color = new Color(k_Cyan.r, k_Cyan.g, k_Cyan.b, 0.9f);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sy in new[] { -1f, 1f })
                {
                    M3.Surface("BracketH", Panel, new Vector2(sx * (x - 13f), sy * y), new Vector2(28f, 2f), color, 0f);
                    M3.Surface("BracketV", Panel, new Vector2(sx * x, sy * (y - 13f)), new Vector2(2f, 28f), color, 0f);
                }
            }
        }

        // An orange marker, the title, a faint note on the right and a thin rule under it
        void Section(string title, float y, string note)
        {
            float left = -k_Width / 2f;
            M3.Surface("Marker", Panel, new Vector2(left + 16f, y), new Vector2(3f, 16f), k_Orange, 0f);
            Label("Title", title, new Vector2(left + 28f + 70f, y), 140f, TextAlignmentOptions.Left, k_Text).fontStyle = FontStyles.Bold;
            if (note != null)
                Label("Note", note, new Vector2(k_Width / 2f - 16f - 50f, y), 100f, TextAlignmentOptions.Right, k_Dim);
            M3.Surface("Rule", Panel, new Vector2(0f, y - 16f), new Vector2(k_Width - 32f, 1f), new Color(k_Cyan.r, k_Cyan.g, k_Cyan.b, 0.3f), 0f);
        }

        // A label, a bar (track and fill) and a value
        void Row(string label, float y, out Image fill, out TextMeshProUGUI value)
        {
            float left = -k_Width / 2f;
            Label("RowLabel", label, new Vector2(left + 22f + 40f, y), 80f, TextAlignmentOptions.Left, k_Dim);
            M3.Surface("Track", Panel, new Vector2(k_TrackLeft + k_TrackWidth / 2f, y), new Vector2(k_TrackWidth, 6f), new Color(k_Cyan.r, k_Cyan.g, k_Cyan.b, 0.18f), 3f);
            fill = M3.Surface("Fill", Panel, new Vector2(k_TrackLeft + 1f, y), new Vector2(2f, 6f), k_Cyan, 3f);
            value = Label("RowValue", "", new Vector2(k_Width / 2f - 16f - 36f, y), 72f, TextAlignmentOptions.Right, k_Text);
        }

        // A dim label on the left and a bright value on the right
        TextMeshProUGUI Pair(string label, float y)
        {
            Label("PairLabel", label, new Vector2(-k_Width / 2f + 22f + 55f, y), 110f, TextAlignmentOptions.Left, k_Dim);
            return Label("PairValue", "", new Vector2(k_Width / 2f - 16f - 80f, y), 160f, TextAlignmentOptions.Right, k_Text);
        }

        TextMeshProUGUI Label(string name, string text, Vector2 center, float width, TextAlignmentOptions alignment, Color color)
        {
            var label = M3.Text(name, Panel, text, M3Theme.Type.LabelLarge, center, new Vector2(width, 24f), color, alignment);
            label.fontSize = 13f;
            return label;
        }

        // ---- Life ----

        void Update()
        {
            m_Fps = Mathf.Lerp(m_Fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (Time.unscaledTime < m_Next)
                return;
            m_Next = Time.unscaledTime + 0.25f;
            Refresh();
        }

        static void SetBar(Image fill, float y, float ratio, bool warn)
        {
            float width = Mathf.Max(2f, Mathf.Clamp01(ratio) * k_TrackWidth);
            fill.rectTransform.sizeDelta = new Vector2(width, 6f);
            fill.rectTransform.anchoredPosition = new Vector2(k_TrackLeft + width / 2f, y);
            fill.color = warn ? k_Orange : k_Cyan;
        }

        void Refresh()
        {
            SetBar(m_FpsFill, m_FpsY, m_Fps / 60f, m_Fps < 40f);
            m_FpsText.text = Mathf.RoundToInt(m_Fps).ToString();

            long used = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            if (used <= 0)
                used = System.GC.GetTotalMemory(false);
            float total = Mathf.Max(1f, SystemInfo.systemMemorySize) * 1024f * 1024f;
            float memory = used / total;
            SetBar(m_MemoryFill, m_MemoryY, memory, memory > 0.5f);
            m_MemoryText.text = $"{memory * 100f:0}%";

            float battery = SystemInfo.batteryLevel; // -1 when the platform doesn't say
            SetBar(m_BatteryFill, m_BatteryY, Mathf.Max(0f, battery), battery >= 0f && battery < 0.2f);
            m_BatteryText.text = battery < 0f ? "N/D" : $"{battery * 100f:0}%";

            // One bar per answer: how long it took (a full bar is 6 s)
            var times = m_Session != null ? m_Session.Latencies : null;
            int count = times != null ? times.Count : 0;
            for (int i = 0; i < k_Bars; i++)
            {
                int index = count - k_Bars + i;
                float seconds = index >= 0 && index < count ? times[index] : -1f;
                var bar = m_Spark[i];
                bar.rectTransform.sizeDelta = new Vector2(5f, seconds < 0f ? 3f : Mathf.Lerp(4f, 40f, Mathf.Clamp01(seconds / 6f)));
                bar.rectTransform.anchoredPosition = new Vector2(bar.rectTransform.anchoredPosition.x,
                    k_Height / 2f - 226f + (bar.rectTransform.sizeDelta.y - 3f) / 2f);
                bool newest = i == k_Bars - 1 && seconds >= 0f;
                bar.color = newest ? k_Orange : seconds < 0f ? new Color(k_Cyan.r, k_Cyan.g, k_Cyan.b, 0.25f) : k_Cyan;
            }
            bool online = Application.internetReachability != NetworkReachability.NotReachable;
            m_NetText.text = !online ? "BRAK POŁĄCZENIA"
                : count > 0 ? $"OSTATNIA ODPOWIEDŹ  {times[count - 1]:0.0} s" : "POŁĄCZONO";

            if (m_Session != null)
            {
                m_StateText.text = m_Session.State switch
                {
                    AssistantState.Listening => "SŁUCHAM",
                    AssistantState.Thinking => "MYŚLĘ",
                    AssistantState.Speaking => "MÓWIĘ",
                    _ => "SPOCZYNEK",
                };
                m_VoiceText.text = m_Session.Locale.ToUpperInvariant();
            }
            string model = m_Session != null ? m_Session.ModelLabel.ToUpperInvariant() : "";
            m_ModelText.text = model.Length > 22 ? model.Substring(0, 21) + "…" : model;
            m_NotesText.text = m_Session == null ? "" : !m_Session.MemoryEnabled ? "WYŁĄCZONE"
                : m_Session.Notes.Length == 0 ? "PUSTE" : $"{m_Session.Notes.Length} ZN.";
            var up = System.TimeSpan.FromSeconds(Time.realtimeSinceStartup);
            m_UptimeText.text = $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}";
        }
    }
}
