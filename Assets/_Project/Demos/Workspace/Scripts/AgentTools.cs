using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CardboardXR.Demos.Workspace
{
    // What the assistant's tools reach the workspace through (implemented by WorkspaceDemo)
    public interface IAgentHost
    {
        bool RunWorkspaceCommand(string phrase);   // a voice phrase of the allowed list (AgentToolbox.WorkspacePhrases)
        bool OpenUrl(string url);                  // a browser window (https only)
        void ShowNote(string text);                // a note widget with this text
        void Confirm(string question, Action<bool> answer); // asks the user; answer(false) when refused or unanswered
        string ListWindows();                      // one line per open window, numbered
        // window: a number from ListWindows, a part of a title or kind, or "aktywne"; action: close, bigger, smaller,
        // center, front, left, right, up, down, closer, farther. Returns what was done, or why not.
        string WindowAction(string window, string action);
    }

    public class ToolResult
    {
        public string Output = "";
        public bool IsError;

        public void Fail(string message)
        {
            Output = message;
            IsError = true;
        }
    }

    // One tool Claude may call: a name, what it does, the JSON schema of its input, and the code that runs it
    // (a coroutine: files in the cloud, an MCP server and the user's confirmation all take a while)
    public class AgentTool
    {
        public string Name;
        public string Description;
        public string SchemaJson = "{\"type\":\"object\",\"properties\":{}}";
        public string Activity;                               // shown while it runs ("szukam w plikach")
        public Func<Dictionary<string, object>, string> ConfirmText; // the question to ask first, or null to run at once
        public Func<Dictionary<string, object>, ToolResult, IEnumerator> Run;

        public string DefinitionJson()
        {
            var json = new StringBuilder("{\"name\":");
            MiniJson.WriteString(json, Name);
            json.Append(",\"description\":");
            MiniJson.WriteString(json, Description ?? "");
            json.Append(",\"input_schema\":").Append(SchemaJson).Append('}');
            return json.ToString();
        }
    }

    // The tools V.I.C.T.O.R. has beyond speaking, and the rules that keep them safe:
    //  * its own files (Documents/Victor), the PDFs of the app (read only) and the folder linked in
    //    "Pliki" (iCloud Drive, Google Drive and others that show in the Files app): list, read, write;
    //  * the workspace: open any of the app's tools by its voice phrase, a web page, a note;
    //  * notes for the memory;
    //  * the tools of MCP servers listed in Documents/mcp_servery.txt (see McpServer).
    // Research (web search and fetch) runs on Anthropic's servers and is added by AssistantSession.
    // Rules: anything that changes something (writing a file, opening a page the user didn't name, an MCP tool
    // that isn't read-only, remembering something the user didn't ask to remember) asks first, with a panel and
    // by voice ("tak" / "nie"); results of tools, pages and files are data, never instructions (the system
    // prompt says so); a question gets at most 8 tool steps; every call is logged to Documents/victor_log.txt.
    public class AgentToolbox
    {
        // The only phrases "workspace_command" may run (WorkspaceDemo.TryVoiceCommand): nothing that deletes
        // or changes settings, keys or memory
        public static readonly string[] WorkspacePhrases =
        {
            "zegar", "minutnik", "notatka", "pogoda", "kalendarz", "widżety", "napisy", "pdf", "filmy", "zdjęcia 3d", "środowisko",
            "pokój", "modele", "galeria", "pliki", "rysuj", "warcaby", "chińczyk", "kółko i krzyżyk", "gry", "układy", "zrzut ekranu",
            "hologram na blat", "pokaż hud", "ukryj hud", "wyśrodkuj", "kino", "przeglądarka", "aplikacje", "więcej", "pomoc",
            "nowy ekran", "pamięć", "zmień model", "układ praca", "układ kino", "układ czytanie", "układ własny",
            "pokaż dok", "ukryj dok", "powiększ obraz", "pomniejsz obraz", "włącz wzrok", "wyłącz wzrok",
        };
        static readonly HashSet<string> s_Disruptive = new() { "układ praca", "układ kino", "układ czytanie", "układ własny", "wyśrodkuj" };

        public static string LocalFolder => Path.Combine(Application.persistentDataPath, "Victor");
        static string PdfFolder => Path.Combine(Application.persistentDataPath, "PDF");
        static string McpFile => Path.Combine(Application.persistentDataPath, "mcp_servery.txt");
        static string LogFile => Path.Combine(Application.persistentDataPath, "victor_log.txt");
        const int k_ReadChars = 20000;
        const int k_WriteChars = 200000;
        const int k_OutputChars = 8000;

        readonly AssistantSession m_Session;
        readonly List<AgentTool> m_BuiltIn = new();
        readonly List<AgentTool> m_Mcp = new();

        // The user's last message: a page or a note they named themselves needs no confirmation
        public string LastUserText { get; set; } = "";
        public IReadOnlyList<AgentTool> McpTools => m_Mcp;
        public int McpServerCount { get; private set; }

        public AgentToolbox(AssistantSession session)
        {
            m_Session = session;
            BuildBuiltIn();
        }

        public IEnumerable<AgentTool> All()
        {
            foreach (var tool in m_BuiltIn)
                yield return tool;
            foreach (var tool in m_Mcp)
                yield return tool;
        }

        public AgentTool Find(string name)
        {
            foreach (var tool in All())
            {
                if (tool.Name == name)
                    return tool;
            }
            return null;
        }

        // ---- Running a call: the question first when there is one, then the tool, then a line in the log ----

        public IEnumerator Execute(AgentTool tool, Dictionary<string, object> input, ToolResult result)
        {
            string question = tool.ConfirmText?.Invoke(input);
            if (question != null)
            {
                bool? answer = null;
                if (m_Session.Host == null)
                    answer = false;
                else
                    m_Session.Host.Confirm(question, a => answer = a);
                float asked = Time.unscaledTime;
                while (answer == null && Time.unscaledTime - asked < 50f)
                    yield return null;
                if (answer != true)
                {
                    result.Fail("Użytkownik odmówił (albo nie odpowiedział). Nie ponawiaj tego bez nowej prośby.");
                    Log(tool, input, result);
                    yield break;
                }
            }
            yield return tool.Run(input, result);
            if (result.Output.Length > k_OutputChars)
                result.Output = result.Output.Substring(0, k_OutputChars) + "\n[…ucięto]";
            Log(tool, input, result);
        }

        static void Log(AgentTool tool, Dictionary<string, object> input, ToolResult result)
        {
            try
            {
                string arguments = MiniJson.Serialize(input);
                if (arguments.Length > 160)
                    arguments = arguments.Substring(0, 160) + "…"; // file contents and the like stay out of the log
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{tool.Name}\t{(result.IsError ? "BŁĄD" : "ok")}\t{arguments}\n");
            }
            catch (Exception)
            {
                // the log is a courtesy
            }
        }

        // ---- Built-in tools ----

        const string k_AreaSchema =
            "\"area\":{\"type\":\"string\",\"enum\":[\"local\",\"pdf\",\"cloud\"],\"description\":\"local: V.I.C.T.O.R.'s own folder in the app. " +
            "pdf: the PDFs the user keeps in the app (read only). cloud: the folder the user linked in Pliki (iCloud Drive, Google Drive or another " +
            "provider of the Files app), without subfolders.\"}";

        void BuildBuiltIn()
        {
            m_BuiltIn.Add(new AgentTool
            {
                Name = "files_list",
                Description = "List the files in a folder (names and sizes).",
                SchemaJson = "{\"type\":\"object\",\"properties\":{" + k_AreaSchema + "},\"required\":[\"area\"]}",
                Activity = "przeglądam pliki",
                Run = FilesList,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "files_read",
                Description = "Read a text file (txt, md, csv, json, code…) or a PDF (its text) from a folder. Long files are cut.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{" + k_AreaSchema + ",\"name\":{\"type\":\"string\",\"description\":\"The file's name\"}}," +
                             "\"required\":[\"area\",\"name\"]}",
                Activity = "czytam plik",
                Run = FilesRead,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "files_write",
                Description = "Create or replace a text file (not in the pdf area). The user is asked first.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{" + k_AreaSchema + ",\"name\":{\"type\":\"string\",\"description\":\"The file's name, e.g. notatki.md\"}," +
                             "\"text\":{\"type\":\"string\",\"description\":\"The whole content\"}},\"required\":[\"area\",\"name\",\"text\"]}",
                Activity = "zapisuję plik",
                ConfirmText = input => $"Zapisać plik „{MiniJson.GetString(input, "name")}” ({(MiniJson.GetString(input, "text") ?? "").Length} znaków) " +
                                       $"w obszarze {MiniJson.GetString(input, "area")}?" + (Exists(input) ? " Zastąpi istniejący." : ""),
                Run = FilesWrite,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "workspace_command",
                Description = "Open or run one of the app's own tools in the user's workspace, by its voice phrase: a clock, timer, weather, calendar, notes, " +
                              "PDF reader, films, photos, 3D models, environments, files, drawing, games, layouts, screenshot, the browser…",
                SchemaJson = "{\"type\":\"object\",\"properties\":{\"phrase\":{\"type\":\"string\",\"enum\":[" + EnumList(WorkspacePhrases) + "]}},\"required\":[\"phrase\"]}",
                Activity = "uruchamiam narzędzie",
                ConfirmText = input => s_Disruptive.Contains(MiniJson.GetString(input, "phrase") ?? "")
                    ? $"Wykonać „{MiniJson.GetString(input, "phrase")}”? Zmieni to okna wokół Ciebie." : null,
                Run = WorkspaceCommand,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "windows_list",
                Description = "List the windows, widgets and panels open around the user (numbered), with their kind. Use it before window_action when the user names a window.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Activity = "sprawdzam okna",
                Run = WindowsList,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "window_action",
                Description = "Do something to one open window, widget or panel the user named: close it, make it bigger or smaller, bring it in front (make it the one " +
                              "that typing and the trackpad go to), put it in front of the user (center), or move it left, right, up, down, closer or farther.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{\"window\":{\"type\":\"string\",\"description\":\"a number from windows_list, part of a title or kind, or 'aktywne'\"}," +
                             "\"action\":{\"type\":\"string\",\"enum\":[\"close\",\"bigger\",\"smaller\",\"center\",\"front\",\"left\",\"right\",\"up\",\"down\",\"closer\",\"farther\"]}}," +
                             "\"required\":[\"window\",\"action\"]}",
                Activity = "zmieniam okno",
                // Closing loses what is in the window: asked unless the user just said to close something
                ConfirmText = input => MiniJson.GetString(input, "action") == "close" && !SaidClose()
                    ? $"Zamknąć okno „{MiniJson.GetString(input, "window")}”?" : null,
                Run = WindowActionTool,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "open_url",
                Description = "Open a web page (https) in a browser window in the workspace for the user to look at. To read a page yourself use web_fetch.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}},\"required\":[\"url\"]}",
                Activity = "otwieram stronę",
                // A page the user named needs no question; any other (from a search, a file) does
                ConfirmText = input => HostOf(MiniJson.GetString(input, "url")) is string host && LastUserText.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0
                    ? null : $"Otworzyć stronę {HostOf(MiniJson.GetString(input, "url")) ?? "?"}?",
                Run = OpenUrl,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "show_note",
                Description = "Put a note with this text in front of the user (a note widget), e.g. a short list or a summary to keep in view.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},\"required\":[\"text\"]}",
                Activity = "piszę notatkę",
                Run = ShowNote,
            });
            m_BuiltIn.Add(new AgentTool
            {
                Name = "remember",
                Description = "Keep a short fact about the user in the long-term notes (preferences, projects, agreements). Use it when the user asks you to remember something.",
                SchemaJson = "{\"type\":\"object\",\"properties\":{\"fact\":{\"type\":\"string\"}},\"required\":[\"fact\"]}",
                Activity = "zapamiętuję",
                // What the user told to remember needs no question; anything else could come from a page or a file
                ConfirmText = input => LastUserText.IndexOf("zapamiętaj", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       LastUserText.IndexOf("remember", StringComparison.OrdinalIgnoreCase) >= 0
                    ? null : $"Zapamiętać: „{MiniJson.GetString(input, "fact")}”?",
                Run = Remember,
            });
        }

        static string EnumList(IEnumerable<string> values)
        {
            var list = new StringBuilder();
            foreach (var value in values)
            {
                if (list.Length > 0)
                    list.Append(',');
                MiniJson.WriteString(list, value);
            }
            return list.ToString();
        }

        static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return null;
            return uri.Host;
        }

        // ---- Files ----

        static string AreaFolder(string area) => area == "pdf" ? PdfFolder : LocalFolder;

        // A plain file name: letters (also Polish), digits, space, dash, underscore, dot; no folders, nothing hidden
        static bool SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.StartsWith(".") || name != name.Trim())
                return false;
            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.'))
                    return false;
            }
            return !name.Contains("..");
        }

        static bool Exists(Dictionary<string, object> input)
        {
            string area = MiniJson.GetString(input, "area"), name = MiniJson.GetString(input, "name");
            return area == "local" && SafeName(name) && File.Exists(Path.Combine(LocalFolder, name));
        }

        IEnumerator FilesList(Dictionary<string, object> input, ToolResult result)
        {
            string area = MiniJson.GetString(input, "area") ?? "local";
            if (area == "cloud")
            {
                Dictionary<string, object> answer = null;
                yield return CloudBridge.Run("list", "", "", a => answer = a);
                string error = MiniJson.GetString(answer, "error");
                if (error != null)
                {
                    result.Fail(error);
                    yield break;
                }
                var text = new StringBuilder("Folder „" + MiniJson.GetString(answer, "folder") + "”:\n");
                var files = MiniJson.AsList(MiniJson.Get(answer, "files"));
                if (files == null || files.Count == 0)
                    text.Append("(pusto)");
                else
                    foreach (var file in files)
                        text.Append(MiniJson.GetString(file, "name")).Append(" (").Append((long)MiniJson.GetNumber(file, "size")).Append(" B)\n");
                result.Output = text.ToString();
                yield break;
            }
            if (area != "local" && area != "pdf")
            {
                result.Fail("Nieznany obszar. Użyj: local, pdf albo cloud.");
                yield break;
            }
            try
            {
                string folder = AreaFolder(area);
                Directory.CreateDirectory(folder);
                var listing = new StringBuilder();
                foreach (var path in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(path);
                    if (!name.StartsWith("."))
                        listing.Append(name).Append(" (").Append(new FileInfo(path).Length).Append(" B)\n");
                }
                result.Output = listing.Length == 0 ? "(pusto)" : listing.ToString();
            }
            catch (Exception e)
            {
                result.Fail(e.GetType().Name);
            }
        }

        IEnumerator FilesRead(Dictionary<string, object> input, ToolResult result)
        {
            string area = MiniJson.GetString(input, "area") ?? "local", name = MiniJson.GetString(input, "name");
            if (!SafeName(name))
            {
                result.Fail("Nieprawidłowa nazwa pliku (bez folderów).");
                yield break;
            }
            if (area == "cloud")
            {
                Dictionary<string, object> answer = null;
                yield return CloudBridge.Run("read", name, "", a => answer = a);
                string error = MiniJson.GetString(answer, "error");
                if (error != null)
                    result.Fail(error);
                else
                    result.Output = Clip(MiniJson.GetString(answer, "text") ?? "");
                yield break;
            }
            if (area != "local" && area != "pdf")
            {
                result.Fail("Nieznany obszar. Użyj: local, pdf albo cloud.");
                yield break;
            }
            string path = Path.Combine(AreaFolder(area), name);
            if (!File.Exists(path))
            {
                result.Fail("Nie ma takiego pliku.");
                yield break;
            }
            try
            {
                if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    string pdf = CloudBridge.PdfText(path, k_ReadChars + 1);
                    if (pdf == null)
                        result.Fail("Nie można odczytać tekstu z tego PDF (działa na iPhonie).");
                    else
                        result.Output = Clip(pdf);
                }
                else if (new FileInfo(path).Length > 2_000_000)
                    result.Fail("Plik jest za duży.");
                else
                    result.Output = Clip(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                result.Fail("Nie odczytano pliku: " + e.GetType().Name);
            }
        }

        static string Clip(string text) =>
            text.Length > k_ReadChars ? text.Substring(0, k_ReadChars) + $"\n[…ucięto; plik ma co najmniej {text.Length} znaków]" : text;

        IEnumerator FilesWrite(Dictionary<string, object> input, ToolResult result)
        {
            string area = MiniJson.GetString(input, "area") ?? "local", name = MiniJson.GetString(input, "name"), text = MiniJson.GetString(input, "text") ?? "";
            if (!SafeName(name))
            {
                result.Fail("Nieprawidłowa nazwa pliku (bez folderów).");
                yield break;
            }
            if (text.Length > k_WriteChars)
            {
                result.Fail("Tekst jest za długi (limit 200 000 znaków).");
                yield break;
            }
            if (area == "cloud")
            {
                Dictionary<string, object> answer = null;
                yield return CloudBridge.Run("write", name, text, a => answer = a);
                string error = MiniJson.GetString(answer, "error");
                if (error != null)
                    result.Fail(error);
                else
                    result.Output = $"Zapisano „{name}” w folderze w chmurze.";
                yield break;
            }
            if (area != "local")
            {
                result.Fail("Zapis jest możliwy tylko w obszarach local i cloud.");
                yield break;
            }
            try
            {
                Directory.CreateDirectory(LocalFolder);
                File.WriteAllText(Path.Combine(LocalFolder, name), text, new UTF8Encoding(false));
                result.Output = $"Zapisano „{name}”.";
            }
            catch (Exception e)
            {
                result.Fail("Nie zapisano: " + e.GetType().Name);
            }
        }

        // ---- The workspace ----

        IEnumerator WorkspaceCommand(Dictionary<string, object> input, ToolResult result)
        {
            string phrase = MiniJson.GetString(input, "phrase") ?? "";
            if (Array.IndexOf(WorkspacePhrases, phrase) < 0)
                result.Fail("Tej komendy nie wolno uruchomić.");
            else if (m_Session.Host == null || !m_Session.Host.RunWorkspaceCommand(phrase))
                result.Fail("Nie udało się.");
            else
                result.Output = "Zrobione.";
            yield break;
        }

        bool SaidClose()
        {
            string said = LastUserText.ToLowerInvariant();
            return said.Contains("zamknij") || said.Contains("zamknąć") || said.Contains("wyłącz") || said.Contains("usuń") || said.Contains("close") ||
                   said.Contains("schowaj");
        }

        IEnumerator WindowsList(Dictionary<string, object> input, ToolResult result)
        {
            result.Output = m_Session.Host != null ? m_Session.Host.ListWindows() : "Brak okien.";
            yield break;
        }

        IEnumerator WindowActionTool(Dictionary<string, object> input, ToolResult result)
        {
            string window = MiniJson.GetString(input, "window") ?? "", action = MiniJson.GetString(input, "action") ?? "";
            string said = m_Session.Host != null ? m_Session.Host.WindowAction(window, action) : null;
            if (said == null)
                result.Fail("Nie udało się.");
            else
                result.Output = said;
            yield break;
        }

        IEnumerator OpenUrl(Dictionary<string, object> input, ToolResult result)
        {
            string url = MiniJson.GetString(input, "url");
            if (HostOf(url) == null)
                result.Fail("Tylko adresy https.");
            else if (m_Session.Host == null || !m_Session.Host.OpenUrl(url))
                result.Fail("Nie udało się otworzyć.");
            else
                result.Output = "Strona otwarta w oknie.";
            yield break;
        }

        IEnumerator ShowNote(Dictionary<string, object> input, ToolResult result)
        {
            string text = MiniJson.GetString(input, "text") ?? "";
            if (m_Session.Host == null || text.Length == 0)
                result.Fail("Brak tekstu.");
            else
            {
                m_Session.Host.ShowNote(text.Length > 1500 ? text.Substring(0, 1500) : text);
                result.Output = "Notatka leży przed użytkownikiem.";
            }
            yield break;
        }

        IEnumerator Remember(Dictionary<string, object> input, ToolResult result)
        {
            string fact = MiniJson.GetString(input, "fact") ?? "";
            if (fact.Length == 0 || !m_Session.AddNote(fact))
                result.Fail("Nie zapamiętano (pamięć wyłączona albo pusty tekst).");
            else
                result.Output = "Zapamiętane.";
            yield break;
        }

        // ---- MCP servers ----

        // Reads Documents/mcp_servery.txt (a line per server: name, address, optional token), connects to each and
        // collects their tools. A token typed in the file goes into the Keychain and is replaced by * there.
        public IEnumerator ReloadMcp(Action<string> report)
        {
            m_Mcp.Clear();
            McpServerCount = 0;
            var lines = new List<string>();
            try
            {
                if (!File.Exists(McpFile))
                {
                    File.WriteAllText(McpFile,
                        "# Serwery MCP dla V.I.C.T.O.R.-a: po jednym w linii: nazwa adres [token]\n" +
                        "# Adres to punkt końcowy Streamable HTTP, np. serwer na Macu w tej samej sieci Wi-Fi:\n" +
                        "# drive http://192.168.1.20:8000/mcp\n" +
                        "# Token (jeśli serwer go wymaga) wpisz na końcu linii; po wczytaniu trafi do pęku kluczy, a w pliku zostanie *.\n");
                    report?.Invoke("Utworzyłem mcp_servery.txt w folderze aplikacji: dopisz serwery i powiedz „odśwież mcp”.");
                    yield break;
                }
                lines.AddRange(File.ReadAllLines(McpFile));
            }
            catch (Exception e)
            {
                report?.Invoke("mcp_servery.txt: " + e.GetType().Name);
                yield break;
            }
            bool rewrite = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                string name = parts[0], url = parts[1], token = parts.Length > 2 ? parts[2].Trim() : null;
                if (token == "*")
                    token = AssistantSession.ReadSecret("mcp-" + name);
                else if (!string.IsNullOrEmpty(token) && AssistantSession.WriteSecret("mcp-" + name, token))
                {
                    lines[i] = $"{name} {url} *"; // the token is in the Keychain now
                    rewrite = true;
                }
                var server = new McpServer(name, url, token);
                yield return server.Connect();
                if (server.Error != null)
                {
                    report?.Invoke($"MCP {name}: {server.Error}");
                    continue;
                }
                McpServerCount++;
                foreach (var tool in server.BuildTools(this, m_Mcp))
                    m_Mcp.Add(tool);
            }
            if (rewrite)
            {
                try
                {
                    File.WriteAllLines(McpFile, lines);
                }
                catch (Exception)
                {
                    // the token stays in the file this time
                }
            }
            report?.Invoke(McpServerCount == 0 ? "Żaden serwer MCP nie odpowiedział." : $"Połączono z MCP: serwerów {McpServerCount}, narzędzi {m_Mcp.Count}.");
        }
    }

    // A client of one MCP server over Streamable HTTP: initialize, list the tools, call them. JSON-RPC 2.0, one
    // POST per message; the server answers with JSON or, as an event stream, with the message in a "data:" line.
    // No OAuth here: a bearer token, or a server on the user's own network that does its own sign-in to Google
    // and the like (the Google Drive / Workspace servers on GitHub run that way on a Mac).
    public class McpServer
    {
        const string k_Protocol = "2025-06-18";
        public readonly string Name;
        readonly string m_Url, m_Token;
        string m_SessionId;
        int m_Id;
        readonly List<Dictionary<string, object>> m_Descriptions = new();

        public string Error { get; private set; }

        public McpServer(string name, string url, string token)
        {
            Name = name;
            m_Url = url;
            m_Token = token;
        }

        public IEnumerator Connect()
        {
            Error = null;
            Dictionary<string, object> result = null;
            string error = null;
            yield return Rpc("initialize", "{\"protocolVersion\":\"" + k_Protocol + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"CardboardXR\",\"version\":\"1.0\"}}",
                false, (r, e) => { result = r; error = e; });
            if (result == null)
            {
                Error = error ?? "brak odpowiedzi";
                yield break;
            }
            yield return Rpc("notifications/initialized", "{}", true, (r, e) => { });

            string cursor = null;
            for (int page = 0; page < 10; page++)
            {
                result = null;
                string parameters = cursor == null ? "{}" : "{\"cursor\":" + MiniJson.Quote(cursor) + "}";
                yield return Rpc("tools/list", parameters, false, (r, e) => { result = r; error = e; });
                if (result == null)
                {
                    Error = error ?? "nie pobrano listy narzędzi";
                    yield break;
                }
                var tools = MiniJson.AsList(MiniJson.Get(result, "tools"));
                if (tools != null)
                {
                    foreach (var tool in tools)
                    {
                        if (MiniJson.AsObject(tool) != null)
                            m_Descriptions.Add(MiniJson.AsObject(tool));
                    }
                }
                cursor = MiniJson.GetString(result, "nextCursor");
                if (string.IsNullOrEmpty(cursor))
                    break;
            }
        }

        // The server's tools as tools of the toolbox: named mcp_<server>_<tool>; one that doesn't say it only
        // reads (annotations.readOnlyHint) asks the user first
        public IEnumerable<AgentTool> BuildTools(AgentToolbox toolbox, List<AgentTool> existing)
        {
            foreach (var description in m_Descriptions)
            {
                string tool = MiniJson.GetString(description, "name");
                if (string.IsNullOrEmpty(tool))
                    continue;
                string name = Sanitize($"mcp_{Name}_{tool}");
                while (existing.Exists(t => t.Name == name))
                    name = name.Length > 60 ? name.Substring(0, 60) + "_x" : name + "_x";
                var schema = MiniJson.AsObject(MiniJson.Get(description, "inputSchema")) ?? new Dictionary<string, object>();
                if (!schema.ContainsKey("type"))
                    schema["type"] = "object";
                bool readOnly = MiniJson.GetBool(MiniJson.Get(description, "annotations"), "readOnlyHint");
                string text = MiniJson.GetString(description, "description") ?? tool;
                string remoteName = tool;
                yield return new AgentTool
                {
                    Name = name,
                    Description = $"[MCP {Name}] " + (text.Length > 400 ? text.Substring(0, 400) + "…" : text),
                    SchemaJson = MiniJson.Serialize(schema),
                    Activity = $"pytam {Name}",
                    ConfirmText = readOnly ? null : (Func<Dictionary<string, object>, string>)(input => Describe(Name, remoteName, input)),
                    Run = (input, result) => Call(remoteName, input, result),
                };
            }
        }

        static string Describe(string server, string tool, Dictionary<string, object> input)
        {
            string arguments = MiniJson.Serialize(input);
            return $"Użyć narzędzia {tool} serwera {server}? " + (arguments.Length > 140 ? arguments.Substring(0, 140) + "…" : arguments);
        }

        static string Sanitize(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in name)
                clean.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-') ? c : '_');
            return clean.Length > 64 ? clean.ToString(0, 64) : clean.ToString();
        }

        IEnumerator Call(string tool, Dictionary<string, object> input, ToolResult result)
        {
            Dictionary<string, object> answer = null;
            string error = null;
            string parameters = "{\"name\":" + MiniJson.Quote(tool) + ",\"arguments\":" + MiniJson.Serialize(input) + "}";
            yield return Rpc("tools/call", parameters, false, (r, e) => { answer = r; error = e; });
            if (answer == null)
            {
                result.Fail(error ?? "brak odpowiedzi serwera");
                yield break;
            }
            var text = new StringBuilder();
            var content = MiniJson.AsList(MiniJson.Get(answer, "content"));
            if (content != null)
            {
                foreach (var item in content)
                {
                    string type = MiniJson.GetString(item, "type");
                    if (type == "text")
                        text.Append(MiniJson.GetString(item, "text")).Append('\n');
                    else if (type == "resource")
                        text.Append(MiniJson.GetString(MiniJson.Get(item, "resource"), "text") ?? "[zasób]").Append('\n');
                    else if (type != null)
                        text.Append('[').Append(type).Append("]\n");
                }
            }
            result.Output = text.Length > 0 ? text.ToString().TrimEnd() : "(pusta odpowiedź)";
            result.IsError = MiniJson.GetBool(answer, "isError");
        }

        // One JSON-RPC message. A notification has no id and no answer. done(result, null) or done(null, error)
        IEnumerator Rpc(string method, string parameters, bool notification, Action<Dictionary<string, object>, string> done)
        {
            int id = notification ? 0 : ++m_Id;
            string body = notification
                ? "{\"jsonrpc\":\"2.0\",\"method\":" + MiniJson.Quote(method) + ",\"params\":" + parameters + "}"
                : "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":" + MiniJson.Quote(method) + ",\"params\":" + parameters + "}";
            using var request = new UnityWebRequest(m_Url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 40;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json, text/event-stream");
            request.SetRequestHeader("MCP-Protocol-Version", k_Protocol);
            if (!string.IsNullOrEmpty(m_Token))
                request.SetRequestHeader("Authorization", "Bearer " + m_Token);
            if (!string.IsNullOrEmpty(m_SessionId))
                request.SetRequestHeader("Mcp-Session-Id", m_SessionId);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                done(null, request.responseCode == 401 ? "serwer odrzucił token (401)" : request.responseCode == 0 ? "brak połączenia z serwerem" : $"błąd {request.responseCode}");
                yield break;
            }
            string sessionId = request.GetResponseHeader("Mcp-Session-Id");
            if (!string.IsNullOrEmpty(sessionId))
                m_SessionId = sessionId;
            if (notification)
            {
                done(null, null);
                yield break;
            }
            string text = request.downloadHandler.text;
            string contentType = request.GetResponseHeader("Content-Type") ?? "";
            Dictionary<string, object> message = null;
            if (contentType.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Events: lines "data: {json}"; the one with our id is the answer
                foreach (var line in text.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                        continue;
                    var candidate = MiniJson.Parse(trimmed.Substring(5).Trim()) as Dictionary<string, object>;
                    if (candidate != null && (int)MiniJson.GetNumber(candidate, "id", -1) == id)
                    {
                        message = candidate;
                        break;
                    }
                }
            }
            else
            {
                var parsed = MiniJson.Parse(text);
                message = parsed as Dictionary<string, object>;
                if (message == null && parsed is List<object> batch)
                {
                    foreach (var item in batch)
                    {
                        if ((int)MiniJson.GetNumber(item, "id", -1) == id)
                            message = MiniJson.AsObject(item);
                    }
                }
            }
            if (message == null)
            {
                done(null, "nieczytelna odpowiedź serwera");
                yield break;
            }
            var error = MiniJson.Get(message, "error");
            if (error != null)
            {
                done(null, MiniJson.GetString(error, "message") ?? "błąd serwera");
                yield break;
            }
            done(MiniJson.AsObject(MiniJson.Get(message, "result")) ?? new Dictionary<string, object>(), null);
        }
    }

    // The two pieces that live in iOS: the folder linked in "Pliki" (Plugins/iOS/CardboardXRFiles.mm) and PDF text
    // (CardboardXRPdf.mm). The native call answers later with a JSON text; the callback only files it away and the
    // coroutine picks it up, so nothing but the main thread touches Unity
    public static class CloudBridge
    {
        delegate void Callback(int requestId, IntPtr json);

        static readonly Dictionary<int, string> s_Results = new();
        static int s_NextId = 1;
        static readonly Callback s_Callback = OnResult; // held in a static field so it is never collected

        [AOT.MonoPInvokeCallback(typeof(Callback))]
        static void OnResult(int requestId, IntPtr json)
        {
            string text = Marshal.PtrToStringUTF8(json) ?? "";
            lock (s_Results)
                s_Results[requestId] = text;
        }

        // op: "list", "read" or "write"
        public static IEnumerator Run(string op, string name, string text, Action<Dictionary<string, object>> done)
        {
#if UNITY_IOS && !UNITY_EDITOR
            int id = s_NextId++;
            CardboardXR_CloudOp(id, op, name, text, s_Callback);
            float started = Time.unscaledTime;
            while (true)
            {
                string json;
                bool ready;
                lock (s_Results)
                {
                    ready = s_Results.TryGetValue(id, out json);
                    if (ready)
                        s_Results.Remove(id);
                }
                if (ready)
                {
                    done(MiniJson.Parse(json) as Dictionary<string, object> ?? new Dictionary<string, object> { ["error"] = "nieczytelna odpowiedź" });
                    yield break;
                }
                if (Time.unscaledTime - started > 90f)
                {
                    done(new Dictionary<string, object> { ["error"] = "chmura nie odpowiedziała na czas" });
                    yield break;
                }
                yield return null;
            }
#else
            done(new Dictionary<string, object> { ["error"] = "folder w chmurze działa na iPhonie (Więcej > Pliki > Połącz folder)" });
            yield break;
#endif
        }

        // The text of a PDF (at most maxChars), or null
        public static string PdfText(string path, int maxChars)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return CardboardXR_PdfText(path, maxChars);
#else
            return null;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CardboardXR_CloudOp(int requestId, [MarshalAs(UnmanagedType.LPUTF8Str)] string op,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, Callback callback);

        [DllImport("__Internal")]
        static extern string CardboardXR_PdfText([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int maxChars);
#endif
    }
}
