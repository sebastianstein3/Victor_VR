using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace CardboardXR.Demos.Workspace
{
    // Spatial widgets, as visionOS pins them to walls: small panels that stay where they are put (room
    // anchors, like the windows), with a title, a move handle and a close button. Clock, timer, note,
    // weather and calendar.
    public abstract class WidgetPanel : MonoBehaviour
    {
        protected RectTransform m_Canvas;
        protected Transform m_Head;
        Vector3 m_DragOffset;
        M3Button m_MoveButton;

        public bool IsDragging { get; private set; }
        public event Action<WidgetPanel> Closed;
        public abstract string Kind { get; }

        public static T Create<T>(Transform parent, Transform head, Vector2 size, string title, float distance = 1.1f)
            where T : WidgetPanel
        {
            var panel = WorldUi.Panel($"Widget_{typeof(T).Name}", parent, size, distance, M3Theme.SurfaceContainerHigh, 24f);
            var widget = panel.gameObject.AddComponent<T>();
            widget.m_Canvas = panel;
            widget.m_Head = head;
            float top = size.y / 2f, right = size.x / 2f;
            M3.Text("WidgetTitle", panel, title, M3Theme.Type.TitleMedium, new Vector2(-right + 20f + 90f, top - 28f),
                new Vector2(180f, 32f), M3Theme.OnSurfaceVariant, TextAlignmentOptions.Left);
            widget.m_MoveButton = M3.IconButton("WidgetMove", panel, "open_with", M3Button.Variant.Text,
                new Vector2(right - 80f, top - 28f), widget.ToggleDragging, 48f);
            M3.IconButton("WidgetClose", panel, "close", M3Button.Variant.Text, new Vector2(right - 30f, top - 28f),
                widget.Close, 48f);
            widget.Build(size);
            WorkspaceSounds.Attach(panel);
            return widget;
        }

        // Content below the 56 dp title row
        protected abstract void Build(Vector2 size);

        // Saved with the workspace (WorkspaceDemo)
        public virtual string SaveData => "";
        public virtual void LoadData(string data) { }

        public void Close()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        void ToggleDragging()
        {
            if (IsDragging)
                StopDragging();
            else
                StartDragging();
        }

        public void StartDragging()
        {
            IsDragging = true;
            m_DragOffset = Quaternion.Inverse(m_Head.rotation) * (transform.position - m_Head.position);
            if (m_MoveButton != null)
                m_MoveButton.Selected = true;
        }

        public void StopDragging()
        {
            IsDragging = false;
            if (m_MoveButton != null)
                m_MoveButton.Selected = false;
        }

        protected virtual void Update()
        {
            if (!IsDragging || m_Head == null)
                return;
            var target = m_Head.position + m_Head.rotation * m_DragOffset;
            transform.position = Vector3.Lerp(transform.position, target, Time.unscaledDeltaTime * 10f);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(target - m_Head.position, Vector3.up), Time.unscaledDeltaTime * 10f);
        }

        // Polish names without depending on the runtime's culture data (IL2CPP may strip it)
        protected static readonly string[] k_Days = { "niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota" };
        protected static readonly string[] k_Months =
            { "stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca", "lipca", "sierpnia", "września", "października", "listopada", "grudnia" };
        protected static string PolishDate(DateTime d) => $"{k_Days[(int)d.DayOfWeek]}, {d.Day} {k_Months[d.Month - 1]}";
    }

    // ---- Clock ----
    public class ClockWidget : WidgetPanel
    {
        TextMeshProUGUI m_Time, m_Date;
        public override string Kind => "zegar";

        protected override void Build(Vector2 size)
        {
            m_Time = M3.Text("ClockTime", m_Canvas, "", M3Theme.Type.HeadlineSmall, new Vector2(0f, -10f), new Vector2(size.x - 20f, 60f));
            m_Time.fontSize = 48f;
            m_Date = M3.Text("ClockDate", m_Canvas, "", M3Theme.Type.BodyLarge, new Vector2(0f, -size.y / 2f + 30f),
                new Vector2(size.x - 20f, 24f), M3Theme.OnSurfaceVariant);
        }

        protected override void Update()
        {
            base.Update();
            var now = DateTime.Now;
            m_Time.text = now.ToString("HH:mm:ss");
            m_Date.text = PolishDate(now);
        }
    }

    // ---- Timer ----
    public class TimerWidget : WidgetPanel
    {
        TextMeshProUGUI m_Display;
        M3Button m_StartBtn;
        float m_Remaining;
        bool m_Running;
        AudioSource m_Audio;
        AudioClip m_Alarm;
        public override string Kind => "minutnik";

        protected override void Build(Vector2 size)
        {
            m_Display = M3.Text("TimerDisplay", m_Canvas, "00:00", M3Theme.Type.HeadlineSmall, new Vector2(0f, 10f), new Vector2(size.x - 20f, 60f));
            m_Display.fontSize = 48f;
            float y = -size.y / 2f + 36f;
            M3.Button("Timer1", m_Canvas, "+1 min", M3Button.Variant.Tonal, new Vector2(-110f, y), () => Add(60f), null, 96f);
            M3.Button("Timer5", m_Canvas, "+5 min", M3Button.Variant.Tonal, new Vector2(-6f, y), () => Add(300f), null, 96f);
            m_StartBtn = M3.Button("TimerStart", m_Canvas, "Start", M3Button.Variant.Filled, new Vector2(98f, y), Toggle, null, 96f);
            M3.IconButton("TimerReset", m_Canvas, "refresh", M3Button.Variant.Text, new Vector2(size.x / 2f - 30f, 10f), ResetTimer, 48f);
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Alarm = MakeAlarm();
        }

        void Add(float seconds) { m_Remaining = Mathf.Min(m_Remaining + seconds, 99 * 60f); Show(); }
        void ResetTimer() { m_Running = false; m_Remaining = 0f; m_StartBtn.Text = "Start"; Show(); }

        void Toggle()
        {
            if (m_Remaining <= 0f)
                return;
            m_Running = !m_Running;
            m_StartBtn.Text = m_Running ? "Pauza" : "Start";
        }

        void Show()
        {
            int total = Mathf.CeilToInt(m_Remaining);
            m_Display.text = $"{total / 60:00}:{total % 60:00}";
        }

        protected override void Update()
        {
            base.Update();
            if (!m_Running)
                return;
            m_Remaining -= Time.unscaledDeltaTime;
            if (m_Remaining <= 0f)
            {
                m_Remaining = 0f;
                m_Running = false;
                m_StartBtn.Text = "Start";
                m_Display.text = "Koniec!";
                m_Audio.PlayOneShot(m_Alarm, 0.8f);
                return;
            }
            Show();
        }

        void OnDestroy()
        {
            if (m_Alarm != null)
                Destroy(m_Alarm);
        }

        // Three short beeps
        static AudioClip MakeAlarm()
        {
            const int rate = 44100;
            var samples = new float[(int)(rate * 1.1f)];
            for (int beep = 0; beep < 3; beep++)
            {
                int start = (int)(rate * 0.35f * beep);
                for (int i = 0; i < rate * 0.18f && start + i < samples.Length; i++)
                    samples[start + i] = Mathf.Sin(2f * Mathf.PI * 880f * i / rate) * 0.6f * (1f - i / (rate * 0.18f));
            }
            var clip = AudioClip.Create("TimerAlarm", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    // ---- Note: a sticky note; pinch "Pisz" and the keyboard/dictation go into it ----
    public class NoteWidget : WidgetPanel
    {
        TextMeshProUGUI m_Text;
        M3Button m_WriteBtn;
        string m_Content = "";
        public bool IsWriting { get; private set; }
        public event Action<NoteWidget> WriteRequested;
        public override string Kind => "notatka";
        public override string SaveData => m_Content;
        public override void LoadData(string data) { m_Content = data ?? ""; Refresh(); }

        protected override void Build(Vector2 size)
        {
            m_Canvas.GetComponent<UnityEngine.UI.Image>().color = new Color(0.98f, 0.9f, 0.45f);
            m_Text = M3.Text("NoteText", m_Canvas, "", M3Theme.Type.BodyLarge, new Vector2(0f, 4f), new Vector2(size.x - 40f, size.y - 130f),
                new Color(0.15f, 0.12f, 0.05f), TextAlignmentOptions.TopLeft);
            m_Text.overflowMode = TextOverflowModes.Page;
            m_WriteBtn = M3.Button("NoteWrite", m_Canvas, "Pisz", M3Button.Variant.Filled, new Vector2(-size.x / 2f + 70f, -size.y / 2f + 36f),
                () => WriteRequested?.Invoke(this), "edit", 110f);
            M3.Button("NoteClear", m_Canvas, "Wyczyść", M3Button.Variant.Text, new Vector2(size.x / 2f - 66f, -size.y / 2f + 36f),
                () => { m_Content = ""; Refresh(); }, null, 110f);
            Refresh();
        }

        public void SetWriting(bool writing)
        {
            IsWriting = writing;
            m_WriteBtn.Selected = writing;
            Refresh();
        }

        public void Type(string text) { m_Content += text; Refresh(); }
        public void Backspace() { if (m_Content.Length > 0) m_Content = m_Content.Substring(0, m_Content.Length - 1); Refresh(); }

        void Refresh()
        {
            if (m_Text == null)
                return;
            m_Text.text = "<noparse>" + m_Content.Replace("</noparse>", "") + "</noparse>" + (IsWriting ? "|" : "");
            m_Text.ForceMeshUpdate();
            m_Text.pageToDisplay = Mathf.Max(1, m_Text.textInfo.pageCount);
        }
    }

    // ---- Weather: Open-Meteo (free, no account or key); the city from Documents/pogoda.txt ----
    public class WeatherWidget : WidgetPanel
    {
        const string k_DefaultCity = "Warszawa";
        const float k_RefreshSeconds = 1800f;
        TextMeshProUGUI m_Temp, m_Line, m_Place;
        float m_NextFetch;
        public override string Kind => "pogoda";
        public static string CityFile => Path.Combine(Application.persistentDataPath, "pogoda.txt");

        [Serializable] class GeoResult { public string name; public float latitude; public float longitude; }
        [Serializable] class GeoResponse { public List<GeoResult> results; }
        [Serializable] class Current { public float temperature_2m; public int weather_code; public float wind_speed_10m; }
        [Serializable] class Daily { public float[] temperature_2m_max; public float[] temperature_2m_min; }
        [Serializable] class Forecast { public Current current; public Daily daily; }

        protected override void Build(Vector2 size)
        {
            m_Temp = M3.Text("WeatherTemp", m_Canvas, "–", M3Theme.Type.HeadlineSmall, new Vector2(-size.x / 2f + 80f, 6f), new Vector2(140f, 60f));
            m_Temp.fontSize = 44f;
            m_Line = M3.Text("WeatherLine", m_Canvas, "Wczytywanie...", M3Theme.Type.BodyLarge, new Vector2(60f, 16f),
                new Vector2(size.x - 160f, 60f), M3Theme.OnSurface, TextAlignmentOptions.Left);
            m_Place = M3.Text("WeatherPlace", m_Canvas, "", M3Theme.Type.BodyMedium, new Vector2(0f, -size.y / 2f + 26f),
                new Vector2(size.x - 30f, 22f), M3Theme.OnSurfaceVariant);
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime < m_NextFetch)
                return;
            m_NextFetch = Time.unscaledTime + k_RefreshSeconds;
            StartCoroutine(Fetch());
        }

        static string ReadCity()
        {
            try
            {
                if (!File.Exists(CityFile))
                {
                    File.WriteAllText(CityFile, "# Miasto dla widżetu pogody (pierwsza linia), np.:\n" + k_DefaultCity + "\n");
                    return k_DefaultCity;
                }
                foreach (var raw in File.ReadAllLines(CityFile))
                {
                    var line = raw.Trim();
                    if (line.Length > 0 && !line.StartsWith("#"))
                        return line;
                }
            }
            catch (Exception) { }
            return k_DefaultCity;
        }

        IEnumerator Fetch()
        {
            string city = ReadCity();
            GeoResult place = null;
            using (var geo = UnityWebRequest.Get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=pl&name=" +
                                                 UnityWebRequest.EscapeURL(city)))
            {
                geo.timeout = 10;
                yield return geo.SendWebRequest();
                if (geo.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<GeoResponse>(geo.downloadHandler.text);
                    if (response?.results != null && response.results.Count > 0)
                        place = response.results[0];
                }
            }
            if (place == null)
            {
                m_Line.text = "Brak połączenia albo nie znaleziono miasta (pogoda.txt)";
                m_NextFetch = Time.unscaledTime + 60f;
                yield break;
            }
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + place.latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                         "&longitude=" + place.longitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                         "&current=temperature_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    m_Line.text = "Brak połączenia";
                    m_NextFetch = Time.unscaledTime + 60f;
                    yield break;
                }
                var forecast = JsonUtility.FromJson<Forecast>(request.downloadHandler.text);
                if (forecast?.current == null)
                    yield break;
                m_Temp.text = $"{Mathf.RoundToInt(forecast.current.temperature_2m)}°";
                string tomorrow = forecast.daily?.temperature_2m_max != null && forecast.daily.temperature_2m_max.Length > 1
                    ? $"\nJutro {Mathf.RoundToInt(forecast.daily.temperature_2m_min[1])}…{Mathf.RoundToInt(forecast.daily.temperature_2m_max[1])}°"
                    : "";
                m_Line.text = Describe(forecast.current.weather_code) + $", wiatr {Mathf.RoundToInt(forecast.current.wind_speed_10m)} km/h" + tomorrow;
                m_Place.text = "<noparse>" + place.name + "</noparse> · Open-Meteo";
            }
        }

        // WMO weather codes
        public static string Describe(int code) => code switch
        {
            0 => "Bezchmurnie",
            1 or 2 => "Częściowe zachmurzenie",
            3 => "Pochmurno",
            45 or 48 => "Mgła",
            51 or 53 or 55 or 56 or 57 => "Mżawka",
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Deszcz",
            71 or 73 or 75 or 77 or 85 or 86 => "Śnieg",
            95 or 96 or 99 => "Burza",
            _ => "Pogoda"
        };
    }

    // ---- Calendar: today's and tomorrow's events from the iPhone's Calendar (EventKit) ----
    public class CalendarWidget : WidgetPanel
    {
        TextMeshProUGUI m_List;
        float m_NextFetch;
        public override string Kind => "kalendarz";
        static readonly List<CalendarWidget> s_Open = new();

        [Serializable] class Item { public string title; public string start; public bool allDay; public bool tomorrow; }
        [Serializable] class Items { public List<Item> events; public string error; }

        protected override void Build(Vector2 size)
        {
            m_List = M3.Text("CalendarList", m_Canvas, "Wczytywanie...", M3Theme.Type.BodyMedium, new Vector2(0f, -14f),
                new Vector2(size.x - 40f, size.y - 90f), M3Theme.OnSurface, TextAlignmentOptions.TopLeft);
            s_Open.Add(this);
        }

        void OnDestroy() => s_Open.Remove(this);

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime < m_NextFetch)
                return;
            m_NextFetch = Time.unscaledTime + 300f;
#if UNITY_IOS && !UNITY_EDITOR
            EnsureReceiver();
            CardboardXR_CalendarFetch();
#else
            m_List.text = "Kalendarz działa na iPhonie (iOS).";
#endif
        }

        void Show(Items items)
        {
            if (!string.IsNullOrEmpty(items.error))
            {
                m_List.text = "<noparse>" + items.error + "</noparse>";
                return;
            }
            if (items.events == null || items.events.Count == 0)
            {
                m_List.text = "Brak wydarzeń dziś i jutro.";
                return;
            }
            var text = new System.Text.StringBuilder();
            bool tomorrowHeader = false;
            foreach (var item in items.events)
            {
                if (item.tomorrow && !tomorrowHeader)
                {
                    text.Append("<b>Jutro</b>\n");
                    tomorrowHeader = true;
                }
                text.Append(item.allDay ? "cały dzień" : item.start).Append("  <noparse>")
                    .Append((item.title ?? "").Replace("</noparse>", "")).Append("</noparse>\n");
            }
            m_List.text = text.ToString();
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CardboardXR_CalendarFetch();

        static void EnsureReceiver()
        {
            if (GameObject.Find("CalendarNative") != null)
                return;
            var go = new GameObject("CalendarNative");
            DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
        }

        [Preserve]
        class Receiver : MonoBehaviour
        {
            [Preserve]
            void OnCalendar(string json)
            {
                var items = JsonUtility.FromJson<Items>(json) ?? new Items { error = "Błąd kalendarza" };
                foreach (var widget in s_Open)
                {
                    if (widget != null)
                        widget.Show(items);
                }
            }
        }
#endif
    }
}
