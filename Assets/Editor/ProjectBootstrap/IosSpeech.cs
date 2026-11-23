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
            project.WriteToFile(projectPath);
            Debug.Log("[ProjectSetup] Voice dictation: speech usage text added, Speech.framework linked.");
        }
    }
}
