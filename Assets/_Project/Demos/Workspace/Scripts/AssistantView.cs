using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace CardboardXR.Demos.Workspace
{
    // The AI assistant (Więcej > Asystent, "asystent", or "zapytaj …" from anywhere): a window with the
    // conversation. Ask by voice (the "Zapytaj" button or any dictation while this window is active) or
    // type a question with a keyboard and press Enter. Answers come from Claude (the Messages API over
    // HTTPS, model claude-opus-5-5) and are read aloud through the AirPods (Plugins/iOS/CardboardXRAssistant.mm).
    //
    // The API key: put it in a file "klucz_ai.txt" in the app's Documents folder (Finder or the Files
    // app). On the next open the app moves it into the iOS Keychain and deletes the file; it is never
    // stored anywhere else, logged, or shown. Saying "usuń klucz" removes it from the Keychain.
    //
    // The conversation is sent whole each time, assistant turns exactly as the API returned them
    // (thinking blocks included, as the API requires), and never edited; "Nowa rozmowa" starts over.
    public class AssistantView : MonoBehaviour
    {
        const string k_Url = "https://api.anthropic.com/v1/messages";
        const string k_Model = "claude-opus-5-5";
        const string k_ApiVersion = "2026-10-21";
        // On a policy decline the API answers with another model in the same call
        const string k_FallbackBeta = "server-side-fallback-2026-07-01";
        const string k_KeyAccount = "claude-api-key";
        const string k_KeyFile = "klucz_ai.txt";
        const int k_MaxTurns = 40; // then a fresh conversation (the request grows with every turn)

        VirtualScreen m_Screen;
        VoiceDictation m_Voice;
        TextMeshProUGUI m_Text;
        M3Button m_SpeakButton;
        readonly List<string> m_Messages = new(); // each a JSON message object, in order
        readonly StringBuilder m_Shown = new();
        string m_Key;
        bool m_Busy;
        bool m_ReadAloud = true;
        string m_LastDraft = "";

        public bool Busy => m_Busy;

        public void Initialize(VirtualScreen screen, VoiceDictation voice)
        {
            m_Screen = screen;
            m_Voice = voice;
            m_Screen.SetTitle("Asystent AI");
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Screen.SetText("");
            m_Screen.SetLayoutSize(Vector2.Max(m_Screen.LayoutSize, new Vector2(760f, 540f)));
            m_Text = M3.Text("AssistantText", m_Screen.Rect, "", M3Theme.Type.BodyLarge, VirtualScreen.BodyCenter,
                VirtualScreen.BodySize, M3Theme.OnSurface, TextAlignmentOptions.BottomLeft);
            m_Text.overflowMode = TextOverflowModes.Page;
            VirtualScreen.AttachToBody(m_Text.rectTransform);

            var ask = M3.Button("AssistantAsk", m_Screen.Rect, "Zapytaj", M3Button.Variant.Filled, Vector2.zero, Listen, "pinch", 130f);
            m_SpeakButton = M3.Button("AssistantSpeak", m_Screen.Rect, "Głos", M3Button.Variant.Tonal, Vector2.zero, ToggleReadAloud, null, 90f);
            var reset = M3.Button("AssistantNew", m_Screen.Rect, "Nowa", M3Button.Variant.Text, Vector2.zero, NewConversation, "refresh", 100f);
            VirtualScreen.PinToFooter((RectTransform)ask.transform, 0f, 75f);
            VirtualScreen.PinToFooter((RectTransform)m_SpeakButton.transform, 0f, 190f);
            VirtualScreen.PinToFooter((RectTransform)reset.transform, 0f, 290f);
            m_SpeakButton.Selected = m_ReadAloud;
            WorkspaceSounds.Attach(screen);

            m_Key = LoadKey();
            if (string.IsNullOrEmpty(m_Key))
                Show("Brak klucza API Claude.\n\nNa Macu: Finder > iPhone > Pliki > CardboardXR, albo w aplikacji Pliki, " +
                     $"wrzuć plik „{k_KeyFile}” z kluczem (jedna linia, z console.anthropic.com). Potem otwórz asystenta ponownie: " +
                     "klucz trafi do pęku kluczy iPhone'a, a plik zostanie usunięty.");
            else
                Show("Zapytaj o cokolwiek: przycisk „Zapytaj”, mów, gdy to okno jest aktywne, albo pisz i naciśnij Enter.\n" +
                     "Głosem z każdego miejsca: „zapytaj …”.");
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
        }

        // ---- Asking ----

        void Listen()
        {
            if (m_Voice == null || m_Busy)
                return;
            StopSpeaking();
            if (!m_Voice.IsRecording)
                m_Voice.StartRecording(); // WorkspaceDemo hands the result to Ask while this window is active
        }

        void ToggleReadAloud()
        {
            m_ReadAloud = !m_ReadAloud;
            m_SpeakButton.Selected = m_ReadAloud;
            if (!m_ReadAloud)
                StopSpeaking();
        }

        public void NewConversation()
        {
            if (m_Busy)
                return;
            StopSpeaking();
            m_Messages.Clear();
            m_Shown.Clear();
            Show("Nowa rozmowa.");
        }

        public void Ask(string question)
        {
            question = question?.Trim();
            if (string.IsNullOrEmpty(question) || m_Busy)
                return;
            if (string.IsNullOrEmpty(m_Key))
            {
                Show($"Najpierw dodaj klucz: plik „{k_KeyFile}” w folderze aplikacji.");
                return;
            }
            if (m_Messages.Count >= 2 * k_MaxTurns)
            {
                m_Messages.Clear();
                m_Shown.Clear();
                Append("<i>(rozmowa była długa – zaczynam nową)</i>");
            }
            StopSpeaking();
            m_Messages.Add("{\"role\":\"user\",\"content\":" + JsonString(question) + "}");
            Append("<b>Ty:</b> " + Escape(question));
            StartCoroutine(Send());
        }

        string RequestBody()
        {
            bool english = m_Voice != null && m_Voice.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            string system = english
                ? "You are a voice assistant in the user's mixed reality headset. Your answer is read aloud: reply in 1-3 short " +
                  "sentences of plain speech, in the language of the question, with no Markdown, lists, links or emoji."
                : "Jesteś asystentem głosowym w goglach mixed reality użytkownika. Odpowiedź jest czytana na głos: odpowiadaj " +
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
            m_Busy = true;
            m_Screen.SetTitle("Asystent AI – myślę…");
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
            m_Busy = false;
            m_Screen.SetTitle("Asystent AI");

            if (result == null)
            {
                m_Messages.RemoveAt(m_Messages.Count - 1); // the question wasn't answered: not part of the conversation
                Append("<color=#F2B8B5>" + Escape(error ?? "Błąd") + "</color>");
                yield break;
            }

            var response = JsonUtility.FromJson<Response>(result);
            string content = TopLevelValue(result, "content");
            if (response == null || content == null)
            {
                m_Messages.RemoveAt(m_Messages.Count - 1);
                Append("<color=#F2B8B5>Nieczytelna odpowiedź.</color>");
                yield break;
            }
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
                Append("<i>Asystent nie może odpowiedzieć na to pytanie.</i>");
                yield break;
            }
            string text = answer.Length > 0 ? answer.ToString() : "(brak odpowiedzi)";
            if (response.stop_reason == "max_tokens")
                text += " …";
            Append("<b>Claude:</b> " + Escape(text));
            if (m_ReadAloud)
                Speak(text);
        }

        // ---- Display ----

        void Show(string text)
        {
            m_Shown.Clear();
            Append(text);
        }

        void Append(string line)
        {
            if (m_Shown.Length > 0)
                m_Shown.Append("\n\n");
            m_Shown.Append(line);
            // Keep the end in view: the newest at the bottom, older pages scroll away
            m_Text.text = m_Shown.ToString();
            m_Text.ForceMeshUpdate();
            m_Text.pageToDisplay = Mathf.Max(1, m_Text.textInfo.pageCount);
        }

        void Update()
        {
            // Typed questions: what the keyboard types into this window, sent on Enter
            if (m_Screen == null)
                return;
            string draft = m_Screen.Text ?? "";
            if (draft == m_LastDraft)
                return;
            m_LastDraft = draft;
            if (draft.EndsWith("\n"))
            {
                m_Screen.SetText("");
                m_LastDraft = "";
                Ask(draft);
            }
            else if (!m_Busy && draft.Length > 0)
                m_Screen.SetTitle("Pytanie: " + (draft.Length > 40 ? "…" + draft.Substring(draft.Length - 40) : draft));
        }

        void OnDestroy() => StopSpeaking();

        void Speak(string text)
        {
#if UNITY_IOS && !UNITY_EDITOR
            CardboardXR_Speak(text, m_Voice != null ? m_Voice.Locale : "pl-PL");
#endif
        }

        static void StopSpeaking()
        {
#if UNITY_IOS && !UNITY_EDITOR
            CardboardXR_StopSpeaking();
#endif
        }

        // ---- JSON helpers (JsonUtility can't keep unknown blocks, so the raw content array is cut out) ----

        static string Escape(string text) => "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";

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
#endif
    }
}
