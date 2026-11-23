using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CardboardXR.Demos.Workspace
{
    // Keeps the Workspace's panels fixed in the room with ARKit anchors, the way the base project's
    // AnchorPlacer keeps cubes: a panel that is not being moved is parented to an anchor at its pose, so
    // when ARKit corrects its map (drift, the floor found later) the panel stays put on the real wall or
    // desk instead of in "world" coordinates. A panel being moved is let go of its anchor first.
    //
    // Only for this run of the app: anchors aren't saved between launches (that would need ARKit's
    // ARWorldMap, which the base project doesn't use).
    public class WorkspaceAnchors : MonoBehaviour
    {
        const float k_MovedWhileWaiting = 0.01f; // meters: a panel moved while its anchor was being made

        ARAnchorManager m_Manager;
        Transform m_Home; // where a panel goes when it lets go of its anchor
        readonly Dictionary<Transform, ARAnchor> m_Anchors = new();
        readonly HashSet<Transform> m_Pending = new();
        readonly List<Transform> m_Gone = new();

        bool Ready => m_Manager != null && m_Manager.enabled && m_Manager.subsystem != null;

        public void Initialize(Transform home)
        {
            m_Home = home;
            // Core's anchor manager (AnchorPlacer turns it on in 6DoF, which the Workspace requires)
            m_Manager = FindFirstObjectByType<ARAnchorManager>();
            if (m_Manager == null)
                Debug.LogWarning("[Workspace] Brak ARAnchorManager: panele nie będą zakotwiczone w pokoju.");
        }

        // Call every frame for each panel: a still panel gets an anchor, a moving one lets go of it.
        public void Keep(Transform panel, bool moving)
        {
            if (panel == null)
                return;
            bool anchored = m_Anchors.ContainsKey(panel);
            if (moving)
            {
                if (anchored)
                    Release(panel);
            }
            else if (!anchored && !m_Pending.Contains(panel) && Ready)
            {
                AnchorAsync(panel);
            }
        }

        // Before moving a panel by code (recenter, desk placement): it gets a new anchor where it lands.
        public void Release(Transform panel)
        {
            if (panel == null || !m_Anchors.TryGetValue(panel, out var anchor))
                return;
            m_Anchors.Remove(panel);
            panel.SetParent(m_Home, worldPositionStays: true);
            if (anchor != null && m_Manager != null)
                m_Manager.TryRemoveAnchor(anchor);
        }

        public void ReleaseAll()
        {
            foreach (var panel in new List<Transform>(m_Anchors.Keys))
                Release(panel);
            m_Anchors.Clear();
        }

        async void AnchorAsync(Transform panel)
        {
            m_Pending.Add(panel);
            var pose = new Pose(panel.position, panel.rotation);
            var result = await m_Manager.TryAddAnchorAsync(pose);
            m_Pending.Remove(panel);

            if (!result.status.IsSuccess() || result.value == null)
                return;
            // Gone, demo closed, or moved meanwhile: the anchor isn't wanted (it'll be made again next frame)
            if (this == null || panel == null || m_Anchors.ContainsKey(panel) ||
                Vector3.Distance(panel.position, pose.position) > k_MovedWhileWaiting)
            {
                if (m_Manager != null)
                    m_Manager.TryRemoveAnchor(result.value);
                return;
            }
            panel.SetParent(result.value.transform, worldPositionStays: true);
            m_Anchors[panel] = result.value;
        }

        void LateUpdate()
        {
            // Panels closed while anchored: free their anchors
            m_Gone.Clear();
            foreach (var pair in m_Anchors)
            {
                if (pair.Key == null)
                    m_Gone.Add(pair.Key);
            }
            foreach (var panel in m_Gone)
            {
                if (m_Anchors.TryGetValue(panel, out var anchor) && anchor != null && m_Manager != null)
                    m_Manager.TryRemoveAnchor(anchor);
                m_Anchors.Remove(panel);
            }
        }

        // WorkspaceDemo calls ReleaseAll on exit, before it destroys the panels (anchors live in the Core
        // scene, so an anchored panel would otherwise outlive the demo). Here only anchors still left are
        // freed, without touching panels that may be mid-destruction.
        void OnDestroy()
        {
            if (m_Manager == null)
                return;
            foreach (var anchor in m_Anchors.Values)
            {
                if (anchor != null)
                    m_Manager.TryRemoveAnchor(anchor);
            }
            m_Anchors.Clear();
        }
    }
}
