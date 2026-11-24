using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace CardboardXR.Demos.Workspace
{
    // Download 3D models into the app (Więcej > Modele 3D > Pobierz modele, "pobierz model"):
    //  * a small catalog of free glTF models from Khronos's glTF-Sample-Assets (CC0 and CC-BY 4.0: the
    //    authors are in each model's LICENSE.md in that repository; a ".zrodlo.txt" next to the file keeps
    //    the source and licence page);
    //  * your own links: Documents/modele_url.txt, one "https://… .glb" per line, optionally followed by a
    //    name ("# " lines are comments).
    // Only HTTPS, at most 60 MB, and the file must be a binary glTF (.glb, magic "glTF"); anything else is
    // thrown away. A model picked from the catalog is placed right away when it has been downloaded.
    public class ModelDownloader : MonoBehaviour
    {
        const long k_MaxBytes = 60L * 1024 * 1024;
        const string k_LicenseBase = "https://github.com/KhronosGroup/glTF-Sample-Assets/blob/main/Models/";
        const string k_RawBase = "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/main/Models/";
        static string UrlFile => Path.Combine(Application.persistentDataPath, "modele_url.txt");

        // label, file name, Khronos folder name
        static readonly (string label, string folder)[] k_Catalog =
        {
            ("Awokado", "Avocado"),
            ("Boombox", "BoomBox"),
            ("Latarnia", "Lantern"),
            ("Butelka", "WaterBottle"),
            ("Autko", "ToyCar"),
            ("Stary aparat", "AntiqueCamera"),
            ("Lis", "Fox"),
        };

        VirtualScreen m_Screen;
        ChoiceList m_List;
        Action<string> m_Place;
        bool m_Busy;

        public void Initialize(VirtualScreen screen, Action<string> place)
        {
            m_Screen = screen;
            m_Place = place;
            m_List = gameObject.AddComponent<ChoiceList>();
            m_List.Initialize(screen, "Pobierz modele", Choices(), closeOnPick: false);
            m_List.KeepAfterPick = true;
            m_Screen.SetTitle("Pobierz modele (CC0 / CC-BY 4.0, Khronos)");
        }

        static string PathFor(string fileName) => Path.Combine(ModelViewer.Folder, fileName);

        List<ChoiceList.Choice> Choices()
        {
            var choices = new List<ChoiceList.Choice>();
            foreach (var (label, folder) in k_Catalog)
            {
                string name = folder, text = label;
                bool have = File.Exists(PathFor(name + ".glb"));
                choices.Add(new ChoiceList.Choice(text, () => PickCatalog(name), have ? "check" : "cloud_download"));
            }
            choices.Add(new ChoiceList.Choice("Z modele_url.txt", FromUrlFile, "edit"));
            return choices;
        }

        void PickCatalog(string folder)
        {
            string file = folder + ".glb";
            if (File.Exists(PathFor(file)))
            {
                m_Place?.Invoke(PathFor(file));
                return;
            }
            if (m_Busy)
                return;
            StartCoroutine(DownloadThen(k_RawBase + folder + "/glTF-Binary/" + folder + ".glb", file,
                k_LicenseBase + folder + "/LICENSE.md", place: true));
        }

        void FromUrlFile()
        {
            if (m_Busy)
                return;
            try
            {
                if (!File.Exists(UrlFile))
                {
                    File.WriteAllText(UrlFile,
                        "# Adresy modeli 3D do pobrania, po jednym w linii (tylko https, plik .glb, do 60 MB).\n" +
                        "# Po adresie można dodać nazwę pliku. Przykład:\n" +
                        "# https://example.com/modele/krzeslo.glb krzeslo\n");
                    m_Screen.SetTitle("Utworzono modele_url.txt: dopisz adresy (folder aplikacji) i wybierz ponownie");
                    return;
                }
            }
            catch (Exception e)
            {
                m_Screen.SetTitle("modele_url.txt: " + e.GetType().Name);
                return;
            }
            StartCoroutine(DownloadList());
        }

        IEnumerator DownloadList()
        {
            m_Busy = true;
            var items = new List<(string url, string name)>();
            foreach (var raw in File.ReadAllLines(UrlFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                items.Add((parts[0], parts.Length > 1 ? parts[1].Trim() : null));
            }
            int ok = 0, failed = 0;
            foreach (var (url, name) in items)
            {
                string file = SafeFileName(name, url);
                if (File.Exists(PathFor(file)))
                {
                    ok++; // already here
                    continue;
                }
                m_Busy = false; // DownloadThen sets it again; the list is one job
                yield return DownloadThen(url, file, url, place: false);
                if (File.Exists(PathFor(file)))
                    ok++;
                else
                    failed++;
            }
            m_Busy = false;
            m_Screen.SetTitle(items.Count == 0 ? "modele_url.txt jest pusty: dopisz adresy .glb"
                : $"Z listy: {ok} modeli gotowych, {failed} nie udało się");
            m_List.SetChoices(Choices());
        }

        IEnumerator DownloadThen(string url, string fileName, string sourcePage, bool place)
        {
            m_Busy = true;
            string error = null;
            string destination = null;
            yield return Download(url, fileName, result => destination = result, e => error = e);
            m_Busy = false;
            if (destination == null)
            {
                m_Screen.SetTitle("Nie pobrano: " + (error ?? "błąd"));
                yield break;
            }
            try
            {
                File.WriteAllText(destination + ".zrodlo.txt", $"Źródło: {url}\nLicencja / autorzy: {sourcePage}\nPobrano: {DateTime.Now:yyyy-MM-dd}\n");
            }
            catch (Exception)
            {
                // the note is a courtesy
            }
            m_Screen.SetTitle("Pobrano: " + Path.GetFileNameWithoutExtension(destination));
            m_List.SetChoices(Choices());
            if (place)
                m_Place?.Invoke(destination);
        }

        // Downloads to a ".part" file, checks it is a binary glTF, and moves it into the models folder
        IEnumerator Download(string url, string fileName, Action<string> done, Action<string> fail)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                fail("tylko adresy https");
                yield break;
            }
            string destination, partial;
            try
            {
                Directory.CreateDirectory(ModelViewer.Folder);
                destination = UniquePath(fileName);
                partial = destination + ".part";
            }
            catch (Exception e)
            {
                fail(e.GetType().Name);
                yield break;
            }
            m_Screen.SetTitle("Pobieram: " + Path.GetFileNameWithoutExtension(fileName) + " 0%");
            string problem = null;
            using (var request = new UnityWebRequest(uri.AbsoluteUri, "GET"))
            {
                request.downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true };
                request.timeout = 0;
                var operation = request.SendWebRequest();
                float last = Time.unscaledTime;
                ulong lastBytes = 0;
                while (!operation.isDone)
                {
                    long declared;
                    if (long.TryParse(request.GetResponseHeader("Content-Length"), out declared) && declared > k_MaxBytes)
                    {
                        problem = "plik jest za duży (limit 60 MB)";
                        request.Abort();
                        break;
                    }
                    if (request.downloadedBytes > (ulong)k_MaxBytes)
                    {
                        problem = "plik jest za duży (limit 60 MB)";
                        request.Abort();
                        break;
                    }
                    // A stalled connection: nothing new for 30 s
                    if (request.downloadedBytes != lastBytes)
                    {
                        lastBytes = request.downloadedBytes;
                        last = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - last > 30f)
                    {
                        problem = "połączenie się zawiesiło";
                        request.Abort();
                        break;
                    }
                    m_Screen.SetTitle($"Pobieram: {Path.GetFileNameWithoutExtension(fileName)} {Mathf.RoundToInt(request.downloadProgress * 100f)}%");
                    yield return null;
                }
                if (problem == null && (request.result != UnityWebRequest.Result.Success || request.responseCode != 200))
                    problem = request.responseCode == 404 ? "nie ma takiego pliku (404)"
                        : request.result == UnityWebRequest.Result.ConnectionError ? "brak połączenia" : $"błąd {request.responseCode}";
            }
            if (problem == null && !IsBinaryGltf(partial))
                problem = "to nie jest plik .glb";
            if (problem != null)
            {
                TryDelete(partial);
                fail(problem);
                yield break;
            }
            try
            {
                File.Move(partial, destination);
            }
            catch (Exception e)
            {
                TryDelete(partial);
                fail(e.GetType().Name);
                yield break;
            }
            done(destination);
        }

        // A .glb starts with the four bytes "glTF"
        static bool IsBinaryGltf(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                if (stream.Length < 28)
                    return false;
                var head = new byte[4];
                return stream.Read(head, 0, 4) == 4 && head[0] == 'g' && head[1] == 'l' && head[2] == 'T' && head[3] == 'F';
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // nothing more to do
            }
        }

        static string UniquePath(string fileName)
        {
            string path = PathFor(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            for (int i = 2; File.Exists(path) && i < 1000; i++)
                path = PathFor($"{stem} ({i}).glb");
            return path;
        }

        // A plain file name ending in .glb: letters, digits, dot, dash, underscore; no folders
        static string SafeFileName(string name, string url)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    name = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
                }
                catch (Exception)
                {
                    name = "";
                }
            }
            name = Path.GetFileNameWithoutExtension(name ?? "");
            var clean = new StringBuilder();
            foreach (char c in name)
                clean.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            string stem = clean.ToString().Trim('_');
            if (stem.Length == 0)
                stem = "model";
            if (stem.Length > 60)
                stem = stem.Substring(0, 60);
            return stem + ".glb";
        }
    }
}
