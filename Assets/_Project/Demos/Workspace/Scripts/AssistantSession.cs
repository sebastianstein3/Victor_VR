using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace CardboardXR.Demos.Workspace
{
    public enum AssistantState
    {
        Idle,
        Listening, // waiting for the question, by voice
        Thinking,  // the question is with Claude
        Speaking,  // the answer is being read (or shown at reading pace)
    }

    // The AI assistant itself, shared by its window (AssistantView) and the hologram (HologramAssistant):
    // the conversation with Claude (the Messages API over HTTPS, model claude-opus-5-5), the API key in the
    // iOS Keychain, and reading the answers aloud through the AirPods (Plugins/iOS/CardboardXRAssistant.mm).
    //
    // The API key: put it in a file "klucz_ai.txt" in the app's Documents folder (Finder or the Files app).
    // On the next start the app moves it into the iOS Keychain and deletes the file; it is never stored
    // anywhere else, logged, or shown. Saying "usuń klucz" removes it from the Keychain.
    //
    // The conversation is sent whole each time, assistant turns exactly as the API returned them (thinking
    // blocks included, as the API requires), and never edited; NewConversation starts over.
    public class AssistantSession : MonoBehaviour
    {
        const string k_Url = "https://api.anthropic.com/v1/messages";
        const string k_Model = "claude-opus-5-5";
        const string k_ApiVersion = "2026-10-21";
        // On a policy decline the API answers with another model in the same call
        const string k_FallbackBeta = "server-side-fallback-2026-07-01";
        const string k_KeyAccount = "claude-api-key";
        const string k_KeyFile = "klucz_ai.txt";
        const int k_MaxTurns = 40; // then a fresh conversation (the request grows with every turn)
        const float k_ExpectSeconds = 20f;

        static AssistantSession s_Active;

        public event Action<string> UserSaid;     // a question was sent
        public event Action<string> Answered;     // the answer's text
        public event Action<string> Notice;       // something for the user to read: an error, a hint
        public event Action<int, int> WordSpoken; // the range (start, length) of the word now spoken in the answer
        public event Action Changed;              // the state or a setting changed

        VoiceDictation m_Voice;
        readonly List<string> m_Messages = new(); // each a JSON message object, in order
        string m_Key;
        bool m_Thinking;
        bool m_Speaking;
        bool m_ReadAloud = true;
        float m_ExpectUntil;
        float m_LastWord = float.NegativeInfinity;
        Coroutine m_Simulation;

        public bool HasKey => !string.IsNullOrEmpty(m_Key);
        public bool Busy => m_Thinking;
        public string Answer { get; private set; } = "";
        public string Locale => m_Voice != null ? m_Voice.Locale : "pl-PL";
        // How long the last answers took, in seconds, oldest first (the hologram's status panel graphs them)
        public readonly List<float> Latencies = new();
        public static string KeyFileName => k_KeyFile;

        public bool ReadAloud
        {
            get => m_ReadAloud;
            set
            {
                m_ReadAloud = value;
                if (!value)
                    StopSpeaking();
                Changed?.Invoke();
            }
        }

        public AssistantState State
        {
            get
            {
                if (m_Thinking)
                    return AssistantState.Thinking;
                if (m_Speaking)
                    return AssistantState.Speaking;
                if (m_Voice != null && m_Voice.IsRecording && Time.unscaledTime < m_ExpectUntil)
                    return AssistantState.Listening;
                return AssistantState.Idle;
            }
        }

        // 0..1 while speaking: a floor, and a pulse on each word that fades (what the hologram moves with)
        public float Level => m_Speaking ? 0.3f + 0.7f * Mathf.Exp(-(Time.unscaledTime - m_LastWord) * 7f) : 0f;

        public void Initialize(VoiceDictation voice)
        {
            m_Voice = voice;
            s_Active = this;
            RefreshKey();
        }

        // Reads the key file or the Keychain again (the user may have just added the file)
        public bool RefreshKey()
        {
            m_Key = LoadKey();
            return HasKey;
        }

        // ---- The key: Documents/klucz_ai.txt -> Keychain, once ----

        static string LoadKey()
        {
            string file = Path.Combine(Application.persistentDataPath, k_KeyFile);
            try
            {
                if (File.Exists(file))
                {
                    string key = File.ReadAllText(file).Trim();
                    File.Delete(file); // the file must not stay readable in Documents
                    if (key.Length > 20 && StoreKey(key))
                        return key;
                    return key.Length > 20 ? key : null; // outside iOS: this session only
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Assistant] Nie odczytano pliku z kluczem: " + e.GetType().Name);
            }
#if UNITY_IOS && !UNITY_EDITOR
            return CardboardXR_KeychainGet(k_KeyAccount);
#else
            return null;
#endif
        }

        static bool StoreKey(string key)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return CardboardXR_KeychainSet(k_KeyAccount, key) == 1;
#else
            return false;
#endif
        }

        public static void DeleteKey()
        {
#if UNITY_IOS && !UNITY_EDITOR
            CardboardXR_KeychainDelete(k_KeyAccount);
#endif
            if (s_Active != null)
                s_Active.m_Key = null;
        }

        // ---- Listening ----

        // Start recording; the next thing said is the question (for 20 s), whatever it sounds like
        public void Listen()
        {
            if (m_Voice == null || m_Thinking)
                return;
            StopSpeaking();
            m_ExpectUntil = Time.unscaledTime + k_ExpectSeconds;
            if (!m_Voice.IsRecording)
                m_Voice.StartRecording();
            Changed?.Invoke();
        }

        // True once after Listen: the phrase just recognised is the question
        public bool TakeExpectation()
        {
            if (Time.unscaledTime >= m_ExpectUntil)
                return false;
            m_ExpectUntil = 0f;
            Changed?.Invoke();
            return true;
        }

        // ---- Asking ----

        public void NewConversation()
        {
            if (m_Thinking)
                return;
            StopSpeaking();
            m_Messages.Clear();
            Answer = "";
            Changed?.Invoke();
        }

        public void Ask(string question)
        {
            question = question?.Trim();
            if (string.IsNullOrEmpty(question) || m_Thinking)
                return;
            if (!HasKey && !RefreshKey())
            {
                Notice?.Invoke($"Najpierw dodaj klucz: plik „{k_KeyFile}” w folderze aplikacji.");
                return;
            }
            if (m_Messages.Count >= 2 * k_MaxTurns)
            {
                m_Messages.Clear();
                Notice?.Invoke("(rozmowa była długa – zaczynam nową)");
            }
            StopSpeaking();
            m_Messages.Add("{\"role\":\"user\",\"content\":" + JsonString(question) + "}");
            UserSaid?.Invoke(question);
            StartCoroutine(Send());
        }

        string RequestBody()
        {
            bool english = m_Voice != null && m_Voice.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            string system = english
                ? "You are Jarvis, a voice assistant in the user's mixed reality headset. Your answer is read aloud: reply in 1-3 short " +
                  "sentences of plain speech, in the language of the question, with no Markdown, lists, links or emoji."
                : "Jesteś Jarvisem, asystentem głosowym w goglach mixed reality użytkownika. Odpowiedź jest czytana na głos: odpowiadaj " +
                  "w 1–3 krótkich zdaniach zwykłej mowy, w języku pytania (zwykle po polsku), bez Markdown, list, linków i emoji.";
            var body = new StringBuilder();
            body.Append("{\"model\":\"").Append(k_Model).Append("\",\"max_tokens\":16000,");
            body.Append("\"output_config\":{\"effort\":\"low\"},"); // quick spoken answers
            body.Append("\"fallbacks\":\"default\",");
            body.Append("\"system\":").Append(JsonString(system)).Append(",\"messages\":[");
            body.Append(string.Join(",", m_Messages));
            body.Append("]}");
            return body.ToString();
        }

        IEnumerator Send()
        {
            m_Thinking = true;
            Changed?.Invoke();
            float started = Time.unscaledTime;
            string result = null, error = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var request = new UnityWebRequest(k_Url, "POST");
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(RequestBody()));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 120;
                request.SetRequestHeader("content-type", "application/json");
                request.SetRequestHeader("x-api-key", m_Key);
                request.SetRequestHeader("anthropic-version", k_ApiVersion);
                request.SetRequestHeader("anthropic-beta", k_FallbackBeta);
                yield return request.SendWebRequest();

                long status = request.responseCode;
                if (request.result == UnityWebRequest.Result.Success && status == 200)
                {
                    result = request.downloadHandler.text;
                    error = null;
                    break;
                }
                // Busy or a passing network fault: wait and try again; anything else is final
                bool retry = status == 429 || status == 529 || status >= 500 || request.result == UnityWebRequest.Result.ConnectionError;
                error = status switch
                {
                    401 => "Nieprawidłowy klucz API (usuń go i dodaj nowy plik z kluczem).",
                    403 => "Klucz nie ma dostępu do tego modelu.",
                    429 => "Limit zapytań – spróbuj za chwilę.",
                    529 => "Serwery są przeciążone – spróbuj za chwilę.",
                    0 => "Brak połączenia z internetem.",
                    _ => $"Błąd {status}: " + ErrorMessage(request.downloadHandler?.text),
                };
                if (!retry || attempt == 2)
                    break;
                yield return new WaitForSecondsRealtime(2f * (attempt + 1));
            }
            m_Thinking = false;
            Changed?.Invoke();

            if (result == null)
            {
                m_Messages.RemoveAt(m_Messages.Count - 1); // the question wasn't answered: not part of the conversation
                Notice?.Invoke(error ?? "Błąd");
                yield break;
            }

            var response = JsonUtility.FromJson<Response>(result);
            string content = TopLevelValue(result, "content");
            if (response == null || content == null)
            {
                m_Messages.RemoveAt(m_Messages.Count - 1);
                Notice?.Invoke("Nieczytelna odpowiedź.");
                yield break;
            }
            Latencies.Add(Time.unscaledTime - started);
            while (Latencies.Count > 40)
                Latencies.RemoveAt(0);
            // The assistant turn goes back exactly as it came (thinking and fallback blocks included)
            m_Messages.Add("{\"role\":\"assistant\",\"content\":" + content + "}");

            var answer = new StringBuilder();
            if (response.content != null)
            {
                foreach (var block in response.content)
                {
                    if (block != null && block.type == "text" && !string.IsNullOrEmpty(block.text))
                        answer.Append(answer.Length > 0 ? " " : "").Append(block.text.Trim());
                }
            }
            if (response.stop_reason == "refusal")
            {
                Notice?.Invoke("Asystent nie może odpowiedzieć na to pytanie.");
                yield break;
            }
            string text = answer.Length > 0 ? answer.ToString() : "(brak odpowiedzi)";
            if (response.stop_reason == "max_tokens")
                text += " …";
            Answer = text;
            Answered?.Invoke(text);
            Speak(text);
        }

        // ---- Speaking ----

        void Speak(string text)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (m_ReadAloud)
            {
                EnsureReceiver();
                CardboardXR_Speak(text, m_Voice != null ? m_Voice.Locale : "pl-PL");
                return; // the phone reports start, each word and the end
            }
#endif
            // Muted, or not on an iPhone: the same events at reading pace, so the hologram still speaks
            if (m_Simulation != null)
                StopCoroutine(m_Simulation);
            m_Simulation = StartCoroutine(SimulateSpeech(text));
        }

        IEnumerator SimulateSpeech(string text)
        {
            m_Speaking = true;
            Changed?.Invoke();
            int index = 0;
            while (index < text.Length)
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
                int start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    index++;
                if (index > start)
                {
                    m_LastWord = Time.unscaledTime;
                    WordSpoken?.Invoke(start, index - start);
                    yield return new WaitForSecondsRealtime(0.28f + 0.03f * (index - start));
                }
            }
            m_Speaking = false;
            m_Simulation = null;
            Changed?.Invoke();
        }

        public void StopSpeaking()
        {
            if (m_Simulation != null)
            {
                StopCoroutine(m_Simulation);
                m_Simulation = null;
            }
#if UNITY_IOS && !UNITY_EDITOR
            CardboardXR_StopSpeaking();
#endif
            if (m_Speaking)
            {
                m_Speaking = false;
                Changed?.Invoke();
            }
        }

        // From the phone: "start", "word:<location>:<length>", "end"
        static void DispatchSpeech(string message)
        {
            var session = s_Active;
            if (session == null || string.IsNullOrEmpty(message))
                return;
            if (message == "start")
            {
                session.m_Speaking = true;
                session.m_LastWord = Time.unscaledTime;
                session.Changed?.Invoke();
            }
            else if (message == "end")
            {
                session.m_Speaking = false;
                session.Changed?.Invoke();
            }
            else if (message.StartsWith("word:", StringComparison.Ordinal))
            {
                var parts = message.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int start) && int.TryParse(parts[2], out int length))
                {
                    session.m_Speaking = true;
                    session.m_LastWord = Time.unscaledTime;
                    session.WordSpoken?.Invoke(start, length);
                }
            }
        }

        void OnDestroy()
        {
            StopSpeaking();
            if (s_Active == this)
                s_Active = null;
        }

        // ---- JSON helpers (JsonUtility can't keep unknown blocks, so the raw content array is cut out) ----

        static string JsonString(string value)
        {
            var json = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }
            return json.Append('"').ToString();
        }

        // The raw JSON value of a key of the top-level object (an array or object here), or null
        static string TopLevelValue(string json, string key)
        {
            int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    int end = SkipString(json, i);
                    if (end < 0)
                        return null;
                    if (depth == 1 && json.Substring(i + 1, end - i - 1) == key)
                    {
                        int colon = end + 1;
                        while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                        if (colon < json.Length && json[colon] == ':')
                        {
                            int start = colon + 1;
                            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
                            int stop = SkipValue(json, start);
                            return stop > start ? json.Substring(start, stop - start) : null;
                        }
                    }
                    i = end;
                }
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return null;
        }

        // Index of the closing quote of the string opening at start, or -1
        static int SkipString(string json, int start)
        {
            for (int i = start + 1; i < json.Length; i++)
            {
                if (json[i] == '\\') i++;
                else if (json[i] == '"') return i;
            }
            return -1;
        }

        // Index just after the array/object opening at start, or -1
        static int SkipValue(string json, int start)
        {
            if (start >= json.Length || (json[start] != '[' && json[start] != '{'))
                return -1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    i = SkipString(json, i);
                    if (i < 0)
                        return -1;
                }
                else if (c == '{' || c == '[') depth++;
                else if ((c == '}' || c == ']') && --depth == 0)
                    return i + 1;
            }
            return -1;
        }

        static string ErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "brak szczegółów";
            var parsed = JsonUtility.FromJson<ErrorResponse>(body);
            return parsed?.error?.message ?? "brak szczegółów";
        }

        [Serializable] class Block { public string type; public string text; }
        [Serializable] class Response { public Block[] content; public string stop_reason; }
        [Serializable] class ErrorBody { public string type; public string message; }
        [Serializable] class ErrorResponse { public ErrorBody error; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int CardboardXR_KeychainSet([MarshalAs(UnmanagedType.LPUTF8Str)] string account,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("__Internal")]
        static extern string CardboardXR_KeychainGet([MarshalAs(UnmanagedType.LPUTF8Str)] string account);
        [DllImport("__Internal")]
        static extern void CardboardXR_KeychainDelete([MarshalAs(UnmanagedType.LPUTF8Str)] string account);
        [DllImport("__Internal")]
        static extern void CardboardXR_Speak([MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string language);
        [DllImport("__Internal")]
        static extern void CardboardXR_StopSpeaking();

        static void EnsureReceiver()
        {
            if (GameObject.Find("AssistantNative") != null)
                return;
            var go = new GameObject("AssistantNative");
            DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
        }

        [Preserve]
        class Receiver : MonoBehaviour
        {
            [Preserve]
            void OnSpeech(string message) => DispatchSpeech(message);
        }
#endif
    }
}
