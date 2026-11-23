using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // Cinema Dimmer: smoothly dims the real-world passthrough view around the user,
    // focusing all attention on the virtual cinema display like a private movie theatre.
    public class CinemaDimmer : MonoBehaviour
    {
        const float k_MaxDimOpacity = 0.82f;
        const float k_FadeSpeed = 3.5f;

        Transform m_Head;
        GameObject m_DimmerCanvas;
        Image m_VignetteImage;
        bool m_IsCinemaMode;
        float m_TargetOpacity;
        float m_CurrentOpacity;

        public bool IsCinemaMode => m_IsCinemaMode;

        public void Initialize(Transform head)
        {
            m_Head = head;

            m_DimmerCanvas = new GameObject("CinemaDimmerCanvas", typeof(RectTransform));
            m_DimmerCanvas.transform.SetParent(transform, false);

            var canvas = m_DimmerCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)m_DimmerCanvas.transform;
            rect.sizeDelta = new Vector2(2400f, 2400f);
            m_DimmerCanvas.AddComponent<M3Canvas>().SetDistance(2.2f);

            // Dark vignette image covering field of view behind monitors
            m_VignetteImage = m_DimmerCanvas.AddComponent<Image>();
            m_VignetteImage.color = new Color(0f, 0f, 0f, 0f);
            m_VignetteImage.raycastTarget = false;
        }

        void OnDestroy()
        {
            // The canvas is a child of the demo, not of this component: remove it with the component.
            if (m_DimmerCanvas != null)
                Destroy(m_DimmerCanvas);
        }

        public void ToggleCinemaMode() => SetCinemaMode(!m_IsCinemaMode);

        public void SetCinemaMode(bool enable)
        {
            m_IsCinemaMode = enable;
            m_TargetOpacity = enable ? k_MaxDimOpacity : 0f;
            Debug.Log($"[CinemaDimmer] Tryb kinowy: {(enable ? "Włączony (tło za monitorami przyciemnione)" : "Wyłączony")}");
        }

        void Update()
        {
            if (m_DimmerCanvas == null || m_Head == null)
                return;

            // Keep dimmer behind monitors (at 2.2m), forming a cinematic theater backdrop
            m_DimmerCanvas.transform.position = m_Head.position + m_Head.forward * 2.2f;
            m_DimmerCanvas.transform.rotation = m_Head.rotation;

            // Smooth opacity transition
            if (Mathf.Abs(m_CurrentOpacity - m_TargetOpacity) > 0.005f)
            {
                m_CurrentOpacity = Mathf.Lerp(m_CurrentOpacity, m_TargetOpacity, Time.unscaledDeltaTime * k_FadeSpeed);
                if (m_VignetteImage != null)
                    m_VignetteImage.color = new Color(0.02f, 0.02f, 0.04f, m_CurrentOpacity);
            }
        }
    }
}
