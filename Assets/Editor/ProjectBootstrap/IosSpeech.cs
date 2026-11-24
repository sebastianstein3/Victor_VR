using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace ProjectBootstrap
{
    // What the Spatial Workspace's voice dictation (Plugins/iOS/CardboardXRSpeech.mm) needs in the Xcode
    // project, which Unity has no options for: the speech recognition usage text in Info.plist (iOS closes
    // the app on first use without it; the microphone text is Player Settings > Microphone Usage
    // Description), and the Speech and AVFoundation frameworks linked into UnityFramework.
    public class IosSpeech : IPostprocessBuildWithReport
    {
        const string SpeechUsage =
            "CardboardXR uses speech recognition to turn your voice into text in the Spatial Workspace.";

        public int callbackOrder => 1;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            var output = report.summary.outputPath;

            var plistPath = Path.Combine(output, "Info.plist");
            if (File.Exists(plistPath))
            {
                var plist = new PlistDocument();
                plist.ReadFromFile(plistPath);
                plist.root.SetString("NSSpeechRecognitionUsageDescription", SpeechUsage);

                // The Workspace's calendar widget (Plugins/iOS/CardboardXRCalendar.mm), read only
                const string calendarUsage = "CardboardXR shows today's and tomorrow's events in a calendar widget.";
                plist.root.SetString("NSCalendarsFullAccessUsageDescription", calendarUsage);
                plist.root.SetString("NSCalendarsUsageDescription", calendarUsage);

                // Photos: screenshots are added (add only), the gallery reads the library
                plist.root.SetString("NSPhotoLibraryAddUsageDescription", "CardboardXR saves screenshots of the Workspace to Photos.");
                plist.root.SetString("NSPhotoLibraryUsageDescription", "CardboardXR shows your photos and spatial videos in the Workspace gallery.");

                // The Workspace's PC screen reads frames over plain HTTP from a computer on the same
                // Wi-Fi (RemoteDesktopReceiver). Player Settings allow HTTP, which makes Unity allow it
                // everywhere (NSAllowsArbitraryLoads); narrow that to the local network only.
                plist.root.SetString("NSLocalNetworkUsageDescription",
                    "CardboardXR shows your computer's screen from the same Wi-Fi network in the Spatial Workspace.");
                var ats = plist.root["NSAppTransportSecurity"] as PlistElementDict ?? plist.root.CreateDict("NSAppTransportSecurity");
                ats.values.Remove("NSAllowsArbitraryLoads");
                ats.values.Remove("NSAllowsArbitraryLoadsInWebContent");
                ats.SetBoolean("NSAllowsLocalNetworking", true);
                plist.WriteToFile(plistPath);
            }
            else
            {
                Debug.LogWarning($"[ProjectSetup] No Info.plist at {plistPath}; voice dictation will be closed by iOS.");
            }

            var projectPath = PBXProject.GetPBXProjectPath(output);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning($"[ProjectSetup] No Xcode project at {projectPath}; Speech.framework not linked.");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var framework = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework, "Speech.framework", false);
            project.AddFrameworkToProject(framework, "AVFoundation.framework", false);
            // The Workspace's browser (Plugins/iOS/CardboardXRBrowser.mm)
            project.AddFrameworkToProject(framework, "WebKit.framework", false);
            project.AddFrameworkToProject(framework, "Metal.framework", false);
            project.AddFrameworkToProject(framework, "EventKit.framework", false);
            project.AddFrameworkToProject(framework, "PDFKit.framework", false); // PDF reader
            // Live captions' translation (Plugins/iOS/CardboardXRTranslate.swift): Translation exists from
            // iOS 17.4, so it is linked weakly (the plugin checks for iOS 26.4 itself); Swift needs a version
            project.AddFrameworkToProject(framework, "Translation.framework", true);
            project.AddFrameworkToProject(framework, "UniformTypeIdentifiers.framework", false); // file picker (Pliki)
            project.AddFrameworkToProject(framework, "Photos.framework", false); // gallery and screenshots
            project.AddFrameworkToProject(framework, "Security.framework", false); // assistant key in the Keychain
            if (string.IsNullOrEmpty(project.GetBuildPropertyForAnyConfig(framework, "SWIFT_VERSION")))
                project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
            project.WriteToFile(projectPath);
            Debug.Log("[ProjectSetup] Voice dictation: speech usage text added, Speech.framework linked.");
        }
    }
}
