using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Hands
{
    // The static hand poses recognized in spatial computing.
    public enum HandPose
    {
        None,
        Pinch,           // Thumb and Index tips together (Select / Drag)
        OpenPalmForward, // Open palm facing away from user (Stop / Push / Minimize)
        OpenPalmUser,    // Open palm facing user's eyes (Summon Menu)
        Fist,            // All fingers curled into a fist (Power grab / Move all)
        Pointing,        // Index extended, other fingers curled (Laser pointer / Direct touch)
        ThumbsUp         // Thumb extended upward, other fingers curled (Confirm / OK)
    }

    // Dynamic movement gestures recognized over time.
    public enum DynamicGesture
    {
        None,
        SwipeLeft,       // Quick flick left (Previous page / screen / Undo)
        SwipeRight,      // Quick flick right (Next page / screen / Redo)
        SwipeUp,         // Quick flick UP (Next TikTok / Short)
        SwipeDown,       // Quick flick DOWN (Previous TikTok / Short)
        PushForward      // Quick push forward
    }

    // Evaluates 21 MediaPipe hand landmarks in real time to recognize spatial gestures
    // inspired by Apple Vision Pro, Meta Quest Interaction SDK, and HoloLens 2.
    public class SpatialGestureDetector : MonoBehaviour
    {
        const float k_SwipeSpeedThreshold = 1.1f; // meters per second
        const float k_SwipeCooldown = 0.4f;       // seconds between swipe triggers
        const float k_PinchCloseDist = 0.028f;
        const float k_PinchReleaseDist = 0.04f;   // a pinch lets go only above this (hysteresis)
        const float k_SwipeMinTravel = 0.10f;     // meters the wrist must move for a swipe
        const float k_HandednessRate = 0.3f;      // as HandSubsystemFeed: the model's left/right reading flips now and then
        const float k_SideChangeMargin = 0.2f;
        const int k_HistoryCapacity = 10;

        struct PositionSample
        {
            public Vector3 position;
            public float time;
        }

        [SerializeField] HandTracker m_Tracker;
        [SerializeField] Transform m_Head;

        public void Initialize(HandTracker tracker, Transform head)
        {
            m_Tracker = tracker;
            m_Head = head;
        }

        readonly Queue<PositionSample> m_VelocityHistory = new();
        HandPose m_CurrentPose = HandPose.None;
        float m_LastGestureTime;
        float m_RightChance = 0.5f;
        bool m_IsRight = true;
        bool m_HasSide;
        int m_LastResult = -1;

        public HandPose CurrentPose => m_CurrentPose;
        public event Action<HandPose> PoseChanged;
        public event Action<DynamicGesture> GestureDetected;

        void Update()
        {
            if (m_Tracker == null || !m_Tracker.isActiveAndEnabled || !m_Tracker.IsTracking)
            {
                if (m_CurrentPose != HandPose.None)
                {
                    m_CurrentPose = HandPose.None;
                    PoseChanged?.Invoke(m_CurrentPose);
                }
                m_VelocityHistory.Clear();
                m_HasSide = false;
                return;
            }

            var joints = m_Tracker.Joints;
            var headForward = m_Head != null ? m_Head.forward : Vector3.forward;

            // Which way the palm faces, from the camera image and which hand it is (the base project's
            // PalmGesture, tested on a phone). The phone's camera sees what the eyes see, so a palm
            // toward the camera is a palm toward the user.
            bool rightHand = UpdateSide();
            float palmFacing = float.NaN;
            var image = m_Tracker.ImageLandmarks;
            if (image != null && image.Count >= HandGeometry.JointCount)
            {
                palmFacing = PalmGesture.Facing(image[PalmGesture.k_Wrist], image[PalmGesture.k_IndexKnuckle],
                    image[PalmGesture.k_PinkyKnuckle], rightHand);
            }

            // 1. Recognize Static Pose
            // PinchDistance is held from the last good 3D fit while ShapeFits is false: don't let a stale
            // value latch a pinch; the joints' own distance still counts.
            float pinchDistance = m_Tracker.ShapeFits ? m_Tracker.PinchDistance : float.MaxValue;
            var detectedPose = EvaluatePose(joints, headForward, pinchDistance, rightHand, palmFacing);
            // Hysteresis: a pinch that is already on stays on until the fingers are clearly apart.
            if (m_CurrentPose == HandPose.Pinch && detectedPose != HandPose.Pinch &&
                Mathf.Min(pinchDistance, Vector3.Distance(joints[HandGeometry.ThumbTip], joints[HandGeometry.IndexTip])) < k_PinchReleaseDist)
                detectedPose = HandPose.Pinch;
            if (detectedPose != m_CurrentPose)
            {
                m_CurrentPose = detectedPose;
                PoseChanged?.Invoke(m_CurrentPose);
            }

            // 2. Track Velocity for Dynamic Gestures (Swipe / Push). Not while the hand is busy with
            // something else: pinching (dragging a window), pointing (typing on the desk keyboard) or a
            // fist: reaching for the desk or moving a window would otherwise read as a swipe.
            if (m_CurrentPose == HandPose.Pinch || m_CurrentPose == HandPose.Pointing || m_CurrentPose == HandPose.Fist)
            {
                m_VelocityHistory.Clear();
                return;
            }
            // Wrist position in the head's own space: turning or moving the head must not look like a
            // swipe (in world space a still hand sweeps past at ~1 m/s when the head turns).
            TrackDynamicGestures(m_Head != null ? m_Head.InverseTransformPoint(joints[0]) : joints[0]);
        }

        // Which hand it is, smoothed like HandSubsystemFeed does: one flipped reading would otherwise
        // swap "palm to me" and "palm away" for a frame.
        bool UpdateSide()
        {
            if (m_Tracker.ResultCount == m_LastResult)
                return m_IsRight;
            m_LastResult = m_Tracker.ResultCount;
            float right = m_Tracker.HandednessScore;
            if (!m_HasSide)
            {
                m_HasSide = true;
                m_RightChance = right;
                m_IsRight = right >= 0.5f;
                return m_IsRight;
            }
            m_RightChance = Mathf.Lerp(m_RightChance, right, k_HandednessRate);
            if (m_IsRight ? m_RightChance < 0.5f - k_SideChangeMargin : m_RightChance > 0.5f + k_SideChangeMargin)
                m_IsRight = !m_IsRight;
            return m_IsRight;
        }

        void TrackDynamicGestures(Vector3 currentWristPos)
        {
            float now = Time.unscaledTime;
            m_VelocityHistory.Enqueue(new PositionSample { position = currentWristPos, time = now });

            while (m_VelocityHistory.Count > k_HistoryCapacity || (m_VelocityHistory.Count > 0 && now - m_VelocityHistory.Peek().time > 0.25f))
            {
                m_VelocityHistory.Dequeue();
            }

            if (m_VelocityHistory.Count < 3 || now - m_LastGestureTime < k_SwipeCooldown)
                return;

            var oldest = m_VelocityHistory.Peek();
            float dt = now - oldest.time;
            if (dt < 0.05f)
                return;

            // Positions are already in the head's space (see Update)
            Vector3 delta = currentWristPos - oldest.position;
            Vector3 localVel = delta / dt;

            // Fast AND far enough: a quick small twitch or the start of a reach is not a swipe.
            if (Mathf.Abs(localVel.x) > k_SwipeSpeedThreshold && Mathf.Abs(delta.x) > k_SwipeMinTravel &&
                Mathf.Abs(localVel.x) > Mathf.Abs(localVel.y) * 1.5f)
            {
                var gesture = localVel.x > 0 ? DynamicGesture.SwipeRight : DynamicGesture.SwipeLeft;
                m_LastGestureTime = now;
                GestureDetected?.Invoke(gesture);
            }
            else if (Mathf.Abs(localVel.y) > k_SwipeSpeedThreshold && Mathf.Abs(delta.y) > k_SwipeMinTravel &&
                     Mathf.Abs(localVel.y) > Mathf.Abs(localVel.x) * 1.5f)
            {
                var gesture = localVel.y > 0 ? DynamicGesture.SwipeUp : DynamicGesture.SwipeDown;
                m_LastGestureTime = now;
                GestureDetected?.Invoke(gesture);
            }
            // A push: clearly forward, far, and mostly along z (monocular depth is noisy, so be strict)
            else if (localVel.z > k_SwipeSpeedThreshold * 1.2f && delta.z > k_SwipeMinTravel * 1.2f &&
                     localVel.z > 2f * Mathf.Max(Mathf.Abs(localVel.x), Mathf.Abs(localVel.y)))
            {
                m_LastGestureTime = now;
                GestureDetected?.Invoke(DynamicGesture.PushForward);
            }
        }

        // palmFacing is PalmGesture.Facing (1: the palm squarely toward the user, -1: its back); when it
        // is NaN the 3D joints decide, using which hand it is (rightHand).
        public static HandPose EvaluatePose(Vector3[] joints, Vector3 headForward, float pinchDist = 0.05f,
            bool rightHand = true, float palmFacing = float.NaN)
        {
            if (joints == null || joints.Length < HandGeometry.JointCount)
                return HandPose.None;

            // Check Pinch first
            if (pinchDist < k_PinchCloseDist || Vector3.Distance(joints[4], joints[8]) < k_PinchCloseDist)
                return HandPose.Pinch;

            bool indexExt = IsFingerExtended(joints, 8, 6, 5);
            bool middleExt = IsFingerExtended(joints, 12, 10, 9);
            bool ringExt = IsFingerExtended(joints, 16, 14, 13);
            bool pinkyExt = IsFingerExtended(joints, 20, 18, 17);
            bool thumbExt = Vector3.Distance(joints[4], joints[0]) > Vector3.Distance(joints[2], joints[0]) * 1.25f;

            // 1. Pointing: Index extended, other 3 curled
            if (indexExt && !middleExt && !ringExt && !pinkyExt)
                return HandPose.Pointing;

            // 2. Thumbs Up: Thumb extended and pointing UP, other 4 curled
            if (thumbExt && !indexExt && !middleExt && !ringExt && !pinkyExt)
            {
                Vector3 thumbDir = (joints[4] - joints[2]).normalized;
                if (Vector3.Dot(thumbDir, Vector3.up) > 0.55f)
                    return HandPose.ThumbsUp;
            }

            // 3. Fist: All 4 main fingers curled. The thumb doesn't matter: in a real fist it lies across
            // the fingers, which by distance alone looks "extended".
            if (!indexExt && !middleExt && !ringExt && !pinkyExt)
                return HandPose.Fist;

            // 4. Open Palm: All 5 fingers extended
            if (indexExt && middleExt && ringExt && pinkyExt)
            {
                // Positive: the palm faces the user; negative: it faces away (the "stop" gesture).
                float toUser = float.IsNaN(palmFacing) ? PalmFacingFrom3D(joints, headForward, rightHand) : palmFacing;

                if (toUser > 0.35f)
                    return HandPose.OpenPalmUser;
                if (toUser < -0.35f)
                    return HandPose.OpenPalmForward;
            }

            return HandPose.None;
        }

        public static bool IsFingerExtended(Vector3[] joints, int tipIdx, int pipIdx, int mcpIdx)
        {
            float tipToWrist = Vector3.Distance(joints[tipIdx], joints[0]);
            float pipToWrist = Vector3.Distance(joints[pipIdx], joints[0]);
            return tipToWrist > pipToWrist * 1.15f;
        }

        // GetPalmNormal's cross product points out of the BACK of a right hand and out of the palm of a
        // left hand (in Unity's left-handed space: with the palm toward the user, the right hand's index
        // knuckle is to the right of its pinky knuckle, which makes the product point away from the
        // user). So for a right hand the palm faces the user when the product points along the user's
        // view, and for a left hand when it points against it.
        public static float PalmFacingFrom3D(Vector3[] joints, Vector3 headForward, bool rightHand)
        {
            float along = Vector3.Dot(GetPalmNormal(joints), headForward);
            return rightHand ? along : -along;
        }

        public static Vector3 GetPalmNormal(Vector3[] joints)
        {
            Vector3 wrist = joints[0];
            Vector3 indexBase = joints[5];
            Vector3 pinkyBase = joints[17];
            return Vector3.Cross(indexBase - wrist, pinkyBase - wrist).normalized;
        }
    }
}
