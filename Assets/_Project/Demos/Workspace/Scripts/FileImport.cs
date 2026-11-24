using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.Scripting;

namespace CardboardXR.Demos.Workspace
{
    // "Więcej > Pliki": files from iCloud Drive and other places into the app's folders, without Finder
    // (Plugins/iOS/CardboardXRFiles.mm). Two ways:
    //  * "Z telefonu": the system file picker; it appears on the phone's screen, so take the phone out of
    //    the goggles, choose the files, put it back;
    //  * "Połącz folder": choose a folder once the same way (e.g. iCloud Drive/VisionPro); from then on its
    //    files are listed here and one pinch copies a file in, all inside the goggles.
    // Each file lands in the folder it is opened from (PDF, Filmy, Modele, Przestrzenne, Srodowiska).
    public class FileImport : MonoBehaviour
    {
        static readonly List<FileImport> s_Open = new();
        static readonly string[] k_Folders = { "PDF", "Filmy", "Modele", "Przestrzenne", "Srodowiska", "Inne" };

        VirtualScreen m_Screen;
        ChoiceList m_List;
        string m_LinkedName;
        Listing m_Listing;

        public void Initialize(VirtualScreen screen)
        {
            m_Screen = screen;
            s_Open.Add(this);
            m_List = gameObject.AddComponent<ChoiceList>();
            m_List.Initialize(screen, "Pliki", Actions(), closeOnPick: false);
            m_List.KeepAfterPick = true;
#if UNITY_IOS && !UNITY_EDITOR
            EnsureReceiver();
            SetStatus("Pliki — sprawdzam połączony folder…");
            CardboardXR_FilesList();
#else
            SetStatus("Pliki — import działa na iPhonie");
#endif
        }

        List<ChoiceList.Choice> Actions()
        {
            return new List<ChoiceList.Choice>
            {
                new("Z telefonu (zdejmij gogle)", PickOnPhone, "smartphone"),
                new(m_LinkedName == null ? "Połącz folder iCloud" : "Zmień folder", LinkFolder, "settings"),
                new("Odśwież", Refresh, "refresh"),
            };
        }

        void PickOnPhone()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SetStatus("Wybierz pliki na ekranie telefonu");
            CardboardXR_FilesPick();
#endif
        }

        void LinkFolder()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SetStatus("Wybierz folder na ekranie telefonu (np. iCloud Drive)");
            CardboardXR_FilesLinkFolder();
#endif
        }

        void Refresh()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SetStatus("Pliki — odświeżam…");
            CardboardXR_FilesList();
#endif
        }

        void ImportLinked(string name)
        {
#if UNITY_IOS && !UNITY_EDITOR
            SetStatus("Pobieram: " + Short(name, 40) + "…");
            CardboardXR_FilesImportLinked(name);
#endif
        }

        void SetStatus(string text) => m_Screen.SetTitle(text);

        void ShowListing(Listing listing)
        {
            m_Listing = listing;
            m_LinkedName = listing != null && !listing.none ? listing.folder : null;
            var choices = Actions();
            if (listing?.files != null)
            {
                foreach (var file in listing.files)
                {
                    string name = file.name;
                    string label = Short(Path.GetFileNameWithoutExtension(name), 18) + Path.GetExtension(name);
                    choices.Add(new ChoiceList.Choice(label, () => ImportLinked(name),
                        AlreadyHere(name) ? "check" : file.local ? null : "cloud_download"));
                }
            }
            m_List.SetChoices(choices);
            if (listing == null || listing.none)
                SetStatus("Pliki — połącz folder, by wybierać w goglach");
            else if (!string.IsNullOrEmpty(listing.error))
                SetStatus($"{listing.folder}: {listing.error}");
            else
                SetStatus($"Folder {listing.folder}: {listing.files?.Length ?? 0} plików");
        }

        void ShowImported(ImportResult result)
        {
            if (result.cancelled)
            {
                SetStatus("Pliki — anulowano");
                return;
            }
            if (!string.IsNullOrEmpty(result.error))
            {
                SetStatus("Pliki — " + result.error);
                return;
            }
            int ok = 0;
            string last = null, failed = null;
            foreach (var file in result.files ?? Array.Empty<ImportedFile>())
            {
                if (string.IsNullOrEmpty(file.error))
                {
                    ok++;
                    last = $"{Short(file.name, 30)} → {file.folder}";
                }
                else
                {
                    failed = $"{Short(file.name, 24)}: {file.error}";
                }
            }
            SetStatus(failed != null ? "Nie skopiowano " + failed
                : ok == 1 ? "Zapisano " + last
                : $"Zapisano {ok} plików (Więcej > PDF, Filmy, Modele…)");
            if (m_Listing != null)
                ShowListing(m_Listing); // tick the copied ones
        }

        // Copied already, into any of our folders
        static bool AlreadyHere(string name)
        {
            foreach (var folder in k_Folders)
            {
                if (File.Exists(Path.Combine(Application.persistentDataPath, folder, name)))
                    return true;
            }
            return false;
        }

        static string Short(string text, int max) => text.Length > max ? text.Substring(0, max - 1) + "…" : text;

        void OnDestroy() => s_Open.Remove(this);

        // ---- Native results (JSON from CardboardXRFiles.mm) ----

        [Serializable]
        class LinkedFile { public string name; public long size; public bool local; }

        [Serializable]
        class Listing { public string folder; public LinkedFile[] files; public string error; public bool none; }

        [Serializable]
        class ImportedFile { public string name; public string folder; public string error; }

        [Serializable]
        class ImportResult { public ImportedFile[] files; public string error; public bool cancelled; }

        [Serializable]
        class LinkResult { public string folder; public string error; public bool cancelled; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CardboardXR_FilesPick();
        [DllImport("__Internal")] static extern void CardboardXR_FilesLinkFolder();
        [DllImport("__Internal")] static extern void CardboardXR_FilesList();
        [DllImport("__Internal")] static extern void CardboardXR_FilesImportLinked(string name);

        static void EnsureReceiver()
        {
            if (GameObject.Find("FileImportNative") != null)
                return;
            var go = new GameObject("FileImportNative");
            DontDestroyOnLoad(go);
            go.AddComponent<Receiver>();
        }

        [Preserve]
        class Receiver : MonoBehaviour
        {
            [Preserve]
            void OnImported(string json)
            {
                var result = JsonUtility.FromJson<ImportResult>(json) ?? new ImportResult { error = "błąd" };
                foreach (var panel in s_Open.ToArray())
                {
                    if (panel != null)
                        panel.ShowImported(result);
                }
            }

            [Preserve]
            void OnListed(string json)
            {
                var listing = JsonUtility.FromJson<Listing>(json);
                foreach (var panel in s_Open.ToArray())
                {
                    if (panel != null)
                        panel.ShowListing(listing);
                }
            }

            [Preserve]
            void OnLinked(string json)
            {
                var result = JsonUtility.FromJson<LinkResult>(json) ?? new LinkResult { error = "błąd" };
                foreach (var panel in s_Open.ToArray())
                {
                    if (panel == null)
                        continue;
                    if (result.cancelled)
                        panel.SetStatus("Pliki — anulowano");
                    else if (!string.IsNullOrEmpty(result.error))
                        panel.SetStatus("Pliki — " + result.error);
                    else
                        panel.Refresh();
                }
            }
        }
#endif
    }
}
