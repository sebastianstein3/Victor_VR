using CardboardXR.Demos;
using CardboardXR.Hands;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Pick up, turn and put down a 3D thing (a model, the hologram). Needs a collider on the object, so the
    // gaze hits it; WorkspaceDemo.TryBeginPinch hands the pinch here.
    //  * Hand tracking (6DoF): pinch on it and it follows the pinching fingers; turning the wrist turns it
    //    (about the vertical axis through its pivot); open the fingers to put it down.
    //  * No hand position (3DoF pinch sensor, or the "Chwyć" button): it follows the head, a pinch anywhere
    //    puts it down.
    // Put down, it settles on the detected table or floor below it (SnapToSurface), else stays in the air.
    public class Carryable : MonoBehaviour
    {
        const int k_SurfaceLayer = 30; // detected surfaces (StereoBuilder.BoundaryLayer)
        const float k_Follow = 14f;

        public bool SnapToSurface = true;
        public bool IsCarried { get; private set; }
        public bool HandCarried { get; private set; }

        CoreServices m_Core;
        Vector3 m_WorldOffset;   // hand mode: from the pinch point to the pivot
        Vector3 m_HeadOffset;    // head mode: in the head's frame
        float m_HandYaw0, m_ObjectYaw0;
        bool m_Settling;
        Vector3 m_SettleTarget;

        public void Initialize(CoreServices core) => m_Core = core;

        static float HandYaw(HandTracker tracker)
        {
            // Wrist to the middle finger's knuckle, flattened: where the hand points
            var direction = tracker.Joints[9] - tracker.Joints[0];
            direction.y = 0f;
            return direction.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        public bool CanHandCarry => m_Core != null && m_Core.HandTracker != null && m_Core.HandTracker.IsTracking;

        // hand: follow the fingers (needs hand tracking), else the head
        public void BeginCarry(bool hand)
        {
            if (m_Core == null)
                return;
            m_Settling = false;
            HandCarried = hand && CanHandCarry;
            IsCarried = true;
            if (HandCarried)
            {
                var tracker = m_Core.HandTracker;
                m_WorldOffset = transform.position - tracker.PinchPoint;
                m_HandYaw0 = HandYaw(tracker);
                m_ObjectYaw0 = transform.eulerAngles.y;
            }
            else
            {
                m_HeadOffset = Quaternion.Inverse(m_Core.Head.rotation) * (transform.position - m_Core.Head.position);
            }
        }

        public void EndCarry()
        {
            if (!IsCarried)
                return;
            IsCarried = false;
            HandCarried = false;
            if (!SnapToSurface)
                return;
            // The surface below (within 2.5 m, starting a little above so a model sunk in a table is found)
            var from = transform.position + Vector3.up * 0.4f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 2.9f, 1 << k_SurfaceLayer, QueryTriggerInteraction.Ignore) &&
                hit.normal.y > 0.9f)
            {
                m_SettleTarget = new Vector3(transform.position.x, hit.point.y, transform.position.z);
                m_Settling = true;
            }
        }

        void Update()
        {
            if (m_Core == null)
                return;
            float t = 1f - Mathf.Exp(-k_Follow * Time.unscaledDeltaTime);
            if (IsCarried)
            {
                if (HandCarried)
                {
                    var tracker = m_Core.HandTracker;
                    if (!tracker.IsTracking)
                        return; // hand lost: it waits where it is
                    transform.position = Vector3.Lerp(transform.position, tracker.PinchPoint + m_WorldOffset, t);
                    float yaw = m_ObjectYaw0 + Mathf.DeltaAngle(m_HandYaw0, HandYaw(tracker));
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f), t);
                }
                else
                {
                    transform.position = Vector3.Lerp(transform.position,
                        m_Core.Head.position + m_Core.Head.rotation * m_HeadOffset, t);
                }
            }
            else if (m_Settling)
            {
                transform.position = Vector3.Lerp(transform.position, m_SettleTarget, t);
                if ((transform.position - m_SettleTarget).sqrMagnitude < 1e-6f)
                    m_Settling = false;
            }
        }
    }
}
