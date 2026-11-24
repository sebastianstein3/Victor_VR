using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting;

namespace CardboardXR.Demos.Workspace
{
    // Saved layouts (Więcej > Układy, or "układ praca" / "zapisz układ praca"): the windows, widgets,
    // phone, place and cinema mode, kept under a name in Documents/Uklady and opened again in front of
    // the user, replacing what is open. Also the screenshot (Więcej > Zrzut ekranu, "zrzut ekranu"):
    // the left eye's view saved to Photos. And a toast: a short message under the gaze.
    public partial class WorkspaceDemo
    {
        public static readonly string[] LayoutNames = { "Praca", "Kino", "Czytanie", "Własny" };
        static string LayoutFolder => Path.Combine(Application.persistentDataPath, "Uklady");
        static string LayoutFile(string name) => Path.Combine(LayoutFolder, name + ".json");

        public void OpenLayouts()
        {
            var screen = CreateNewScreen("Układy");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>();
            foreach (var name in LayoutNames)
            {
                string chosen = name;
                if (File.Exists(LayoutFile(name)))
                    choices.Add(new ChoiceList.Choice(name, () => LoadLayout(chosen), "view_agenda"));
            }
            choices.Add(new ChoiceList.Choice("Zapisz obecny…", OpenSaveLayout, "check"));
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Układy", choices);
        }

        void OpenSaveLayout()
        {
            var screen = CreateNewScreen("Zapisz układ");
            if (screen == null)
                return;
            var choices = new List<ChoiceList.Choice>();
            foreach (var name in LayoutNames)
            {
                string chosen = name;
                choices.Add(new ChoiceList.Choice(File.Exists(LayoutFile(name)) ? name + " (nadpisz)" : name, () => SaveLayout(chosen)));
            }
            screen.gameObject.AddComponent<ChoiceList>().Initialize(screen, "Zapisz jako", choices);
        }

        public void SaveLayout(string name)
        {
            if (m_Core == null)
                return;
            bool ok = WriteWorkspace(LayoutFile(name), CaptureWorkspace());
            ShowToast(ok ? $"Zapisano układ „{name}”" : "Nie udało się zapisać układu");
        }

        public void LoadLayout(string name)
        {
            var saved = ReadWorkspace(LayoutFile(name));
            if (saved == null)
            {
                ShowToast($"Brak układu „{name}”. Zapisz go: Więcej > Układy");
                return;
            }
            ClearWorkspace();
            m_HomeYaw = FlatYaw(); // the layout opens around where the user faces now
            ApplyWorkspace(saved);
            ShowToast($"Układ „{name}”");
        }

        // Closes the windows, widgets and models and goes back to the room (the desk, dock, drawings and
        // games stay)
        void ClearWorkspace()
        {
            DropCarried();
            foreach (var screen in new List<VirtualScreen>(m_Screens))
            {
                if (screen != null)
                    screen.Close();
            }
            foreach (var widget in new List<WidgetPanel>(m_Widgets))
            {
                if (widget != null)
                    widget.Close();
            }
            foreach (var model in new List<ModelViewer>(m_Models))
            {
                if (model != null)
                    model.Close();
            }
            if (m_PhoneScreen != null)
            {
                Destroy(m_PhoneScreen.gameObject);
                m_PhoneScreen = null;
            }
            if (m_Environment != null)
                m_Environment.Show(null);
            if (m_Cinema != null && m_Cinema.IsCinemaMode)
                ToggleCinemaMode();
        }

        // "układ praca" opens, "zapisz układ praca" saves (any of LayoutNames)
        bool TryLayoutCommand(string command)
        {
            bool save = command.StartsWith("zapisz układ ", StringComparison.Ordinal);
            if (!save && !command.StartsWith("układ ", StringComparison.Ordinal))
                return false;
            string spoken = command.Substring(command.LastIndexOf(' ') + 1);
            foreach (var name in LayoutNames)
            {
                if (string.Equals(name, spoken, StringComparison.OrdinalIgnoreCase))
                {
                    if (save) SaveLayout(name); else LoadLayout(name);
                    return true;
                }
            }
            return false;
        }

        // ---- Toast: a message for a moment, below the gaze ----
        RectTransform m_Toast;
        TextMeshProUGUI m_ToastText;
        float m_ToastUntil;

        public void ShowToast(string message)
        {
            if (m_Core == null)
                return;
            if (m_Toast == null)
            {
                m_Toast = WorldUi.Panel("Toast", transform, new Vector2(440f, 64f), 1.0f, M3Theme.InverseSurface, 32f);
                m_ToastText = M3.Text("ToastText", m_Toast, "", M3Theme.Type.BodyLarge, Vector2.zero, new Vector2(410f, 48f),
                    M3Theme.InverseOnSurface);
            }
            m_ToastText.text = "<noparse>" + message.Replace("</noparse>", "") + "</noparse>";
            WorldUi.PlaceInFront(m_Toast, m_Core.Head, 1.0f, -14f);
            m_Toast.gameObject.SetActive(true);
            m_ToastUntil = Time.unscaledTime + 2.5f;
        }

        void UpdateToast()
        {
            if (m_Toast != null && m_Toast.gameObject.activeSelf && Time.unscaledTime > m_ToastUntil)
                m_Toast.gameObject.SetActive(false);
        }

        // ---- Screenshot: what the left eye sees, to Photos ----
        bool m_TakingScreenshot;

        public void TakeScreenshot()
        {
            if (!m_TakingScreenshot)
                StartCoroutine(CaptureScreenshot());
        }

        IEnumerator CaptureScreenshot()
        {
            m_TakingScreenshot = true;
            if (m_Toast != null)
                m_Toast.gameObject.SetActive(false); // not in the picture
            yield return new WaitForEndOfFrame();
            var screen = ScreenCapture.CaptureScreenshotAsTexture();
            m_TakingScreenshot = false;
            if (screen == null)
            {
                ShowToast("Nie udało się zrobić zrzutu");
                yield break;
            }
            // The phone shows both eyes side by side: keep the left one
            int width = screen.width / 2, height = screen.height;
            var eye = new Texture2D(width, height, TextureFormat.RGB24, false);
            eye.SetPixels(screen.GetPixels(0, 0, width, height));
            eye.Apply(false);
            byte[] jpg = eye.EncodeToJPG(92);
            Destroy(screen);
            Destroy(eye);
            string path = Path.Combine(Application.temporaryCachePath, $"zrzut_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            try
            {
                File.WriteAllBytes(path, jpg);
            }
            catch (Exception e)
            {
                ShowToast("Nie zapisano zrzutu: " + e.Message);
                yield break;
            }
#if UNITY_IOS && !UNITY_EDITOR
            EnsureScreenshotReceiver();
            CardboardXR_SaveToPhotos(path);
#else
            ShowToast("Zrzut zapisany: " + path);
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CardboardXR_SaveToPhotos(string path);

        static WorkspaceDemo s_ScreenshotOwner;

        void EnsureScreenshotReceiver()
        {
            s_ScreenshotOwner = this;
            if (GameObject.Find("ScreenshotNative") != null)
                return;
            var go = new GameObject("ScreenshotNative");
            DontDestroyOnLoad(go);
            go.AddComponent<ScreenshotReceiver>();
        }

        [Preserve]
        class ScreenshotReceiver : MonoBehaviour
        {
            [Preserve]
            void OnSaved(string result)
            {
                if (s_ScreenshotOwner != null)
                    s_ScreenshotOwner.ShowToast(result == "ok" ? "Zrzut zapisany w Zdjęciach" : "Zrzut: " + result);
            }
        }
#endif
    }
}
