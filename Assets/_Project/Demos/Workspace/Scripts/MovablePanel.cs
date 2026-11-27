using System;
using CardboardXR.UI;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Makes any panel carryable and closable like a window: the cinema screen and the dock had neither. It
    // adds two round buttons to the panel (grab, close) and carries it the way VirtualScreen does: while
    // carried it keeps its place in the view and follows the head, and a pinch or the fist opening puts it
    // down (WorkspaceDemo.DropCarried, the fist, or the head resting: WorkspaceControl).
    public class MovablePanel : MonoBehaviour
    {
        Transform m_Head;
        Vector3 m_Offset;
        M3Button m_Grab;
        Action m_OnClose;

        public bool IsDragging { get; private set; }
        public string Title { get; private set; }

        // grabAt and closeAt: the buttons' places on the panel, in dp from its centre
        public static MovablePanel Attach(RectTransform panel, Transform head, string title, Vector2 grabAt, Vector2 closeAt, Action onClose)
        {
            var movable = panel.gameObject.AddComponent<MovablePanel>();
            movable.m_Head = head;
            movable.Title = title;
            movable.m_OnClose = onClose;
            movable.m_Grab = M3.IconButton("MoveBtn", panel, "open_with", M3Button.Variant.Tonal, grabAt, movable.Toggle, M3.k_ButtonHeight);
            M3.IconButton("CloseBtn", panel, "close", M3Button.Variant.Tonal, closeAt, movable.Close, M3.k_ButtonHeight);
            return movable;
        }

        public void Toggle()
        {
            if (IsDragging)
                StopDragging();
            else
                StartDragging();
        }

        public void StartDragging()
        {
            if (m_Head == null)
                return;
            IsDragging = true;
            m_Offset = Quaternion.Inverse(m_Head.rotation) * (transform.position - m_Head.position);
            if (m_Grab != null)
                m_Grab.Selected = true;
        }

        public void StopDragging()
        {
            IsDragging = false;
            if (m_Grab != null)
                m_Grab.Selected = false;
        }

        public void Close()
        {
            StopDragging();
            m_OnClose?.Invoke();
        }

        void Update()
        {
            if (!IsDragging || m_Head == null)
                return;
            Vector3 target = m_Head.position + m_Head.rotation * m_Offset;
            var look = Quaternion.LookRotation(target - m_Head.position, Vector3.up);
            transform.position = Vector3.Lerp(transform.position, target, Time.unscaledDeltaTime * 10f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.unscaledDeltaTime * 10f);
        }
    }
}
