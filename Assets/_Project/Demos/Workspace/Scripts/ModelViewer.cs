using System;
using System.Collections.Generic;
using System.IO;
using CardboardXR.UI;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // 3D models in the room, like visionOS's Quick Look: a .glb / .gltf file from the app's
    // Documents/Modele folder is placed on the surface you look at (a detected table or the floor),
    // sized to fit on a table, and can be turned and scaled from a small control panel next to it.
    // Loading uses glTFast (com.unity.cloud.gltfast, Apache-2.0): add it in the Package Manager; until
    // then (no CXR_GLTF) this says so. USDZ isn't read by glTFast; convert to .glb.
    public class ModelViewer : MonoBehaviour
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Modele");
        const float k_FitSize = 0.4f; // the model's largest side, in meters, when placed
        const int k_SurfaceLayer = 30; // the base project's detected surfaces (StereoBuilder.BoundaryLayer)

        public static bool Available =>
#if CXR_GLTF
            true;
#else
            false;
#endif

        public string FilePath { get; private set; }
        public event Action<ModelViewer> Closed;

        public static List<string> FindFiles()
        {
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var file in Directory.GetFiles(Folder))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".glb" || ext == ".gltf")
                        files.Add(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Workspace] {e.Message}");
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        // Where to put a model: the surface under the gaze (within 3 m), or in front at hand height
        public static Vector3 PlacementPoint(Transform head)
        {
            if (Physics.Raycast(head.position, head.forward, out var hit, 3f, 1 << k_SurfaceLayer, QueryTriggerInteraction.Ignore) &&
                hit.normal.y > 0.9f)
                return hit.point;
            var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            return head.position + flat * 0.8f - Vector3.up * 0.3f;
        }

        public async void Load(string path, Vector3 point, Transform head)
        {
            FilePath = path;
            transform.position = point;
            var flat = Vector3.ProjectOnPlane(head.position - point, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up); // faces the user
#if CXR_GLTF
            var import = new GLTFast.GltfImport();
            bool loaded = await import.Load(new Uri(path).AbsoluteUri);
            if (this == null)
                return;
            var content = new GameObject("Model");
            content.transform.SetParent(transform, false);
            if (!loaded || !await import.InstantiateMainSceneAsync(content.transform))
            {
                Debug.LogWarning($"[Workspace] Nie wczytano modelu {path}");
                return;
            }
            FitOnSurface(content.transform);
#else
            await System.Threading.Tasks.Task.CompletedTask;
            Debug.LogWarning("[Workspace] Modele 3D wymagają pakietu glTFast (com.unity.cloud.gltfast)");
#endif
        }

        // Scales the model so its largest side is k_FitSize and puts its bottom on the point
        void FitOnSurface(Transform content)
        {
            var renderers = content.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 1e-4f)
                content.localScale *= k_FitSize / largest;
            bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            // Centre over the point, bottom on it
            var offset = transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            content.position += offset;
        }

        public void Turn(float degrees) => transform.Rotate(0f, degrees, 0f, Space.World);
        public void Scale(float factor) => transform.localScale = Vector3.one * Mathf.Clamp(transform.localScale.x * factor, 0.1f, 10f);

        public void Close()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }
    }

    // The small panel next to a model: turn, smaller, bigger (the panel's own X closes both)
    public class ModelControls : WidgetPanel
    {
        public ModelViewer Model;
        public override string Kind => "model";

        protected override void Build(Vector2 size)
        {
            float y = -10f;
            M3.Button("ModelLeft", m_Canvas, "-30°", M3Button.Variant.Tonal, new Vector2(-105f, y), () => Model?.Turn(-30f), null, 90f);
            M3.Button("ModelRight", m_Canvas, "+30°", M3Button.Variant.Tonal, new Vector2(-10f, y), () => Model?.Turn(30f), null, 90f);
            M3.Button("ModelSmaller", m_Canvas, "-", M3Button.Variant.Text, new Vector2(70f, y), () => Model?.Scale(1f / 1.25f), null, 56f);
            M3.Button("ModelBigger", m_Canvas, "+", M3Button.Variant.Text, new Vector2(130f, y), () => Model?.Scale(1.25f), null, 56f);
            Closed += _ => { if (Model != null) Model.Close(); };
        }
    }
}
