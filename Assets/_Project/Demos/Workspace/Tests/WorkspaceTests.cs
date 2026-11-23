using CardboardXR.Hands;
using NUnit.Framework;
using UnityEngine;

namespace CardboardXR.Demos.Workspace.Tests
{
    public class WorkspaceTests
    {
        [Test]
        public void TextBufferAppendsAndDeletesCorrectly()
        {
            string buffer = "";
            buffer += 'H';
            buffer += 'e';
            buffer += 'l';
            buffer += 'l';
            buffer += 'o';
            Assert.AreEqual("Hello", buffer);

            // Backspace
            buffer = buffer.Substring(0, buffer.Length - 1);
            Assert.AreEqual("Hell", buffer);

            // Clear
            buffer = "";
            Assert.AreEqual(0, buffer.Length);
        }

        [Test]
        public void ShiftTransformsCharactersAppropriately()
        {
            char key = 'a';
            char withShift = char.ToUpper(key);
            char withoutShift = char.ToLower(key);

            Assert.AreEqual('A', withShift);
            Assert.AreEqual('a', withoutShift);
        }

        [Test]
        public void WordCountCalculatesAccurately()
        {
            string sample = "Wirtualne ekrany w przestrzeni MR";
            var words = sample.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(5, words.Length);
        }

        [Test]
        public void RecognizesPinchPoseWhenFingertipsClose()
        {
            var joints = CreateMockJoints();
            // Put thumb tip and index tip 1.5 cm apart
            joints[4] = new Vector3(0.01f, 0f, 0.4f);
            joints[8] = new Vector3(0.02f, 0f, 0.4f);

            var pose = SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, pinchDist: 0.015f);
            Assert.AreEqual(HandPose.Pinch, pose);
        }

        [Test]
        public void RecognizesPointingPoseWhenIndexExtendedAndOthersCurled()
        {
            var joints = CreateMockJoints();
            // Wrist at 0,0,0
            joints[0] = Vector3.zero;

            // Index extended forward
            joints[5] = new Vector3(0.02f, 0.05f, 0f);
            joints[6] = new Vector3(0.02f, 0.08f, 0f);
            joints[8] = new Vector3(0.02f, 0.16f, 0f);

            // Middle, Ring, Pinky curled (tips closer than PIP to wrist)
            joints[9] = new Vector3(0f, 0.05f, 0f);
            joints[10] = new Vector3(0f, 0.08f, 0f);
            joints[12] = new Vector3(0f, 0.05f, 0f);

            joints[13] = new Vector3(-0.02f, 0.05f, 0f);
            joints[14] = new Vector3(-0.02f, 0.08f, 0f);
            joints[16] = new Vector3(-0.02f, 0.05f, 0f);

            joints[17] = new Vector3(-0.04f, 0.04f, 0f);
            joints[18] = new Vector3(-0.04f, 0.07f, 0f);
            joints[20] = new Vector3(-0.04f, 0.04f, 0f);

            // Thumb curled
            joints[2] = new Vector3(0.03f, 0.02f, 0f);
            joints[4] = new Vector3(0.03f, 0.03f, 0f);

            var pose = SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, pinchDist: 0.1f);
            Assert.AreEqual(HandPose.Pointing, pose);
        }

        [Test]
        public void RecognizesThumbsUpPoseWhenThumbPointsUpward()
        {
            var joints = CreateMockJoints();
            joints[0] = Vector3.zero;

            // Thumb extended vertically UP
            joints[2] = new Vector3(0.03f, 0.02f, 0f);
            joints[4] = new Vector3(0.03f, 0.12f, 0f);

            // All fingers curled
            for (int i = 5; i < 21; i += 4)
            {
                joints[i] = new Vector3(0f, 0.05f, 0f);
                joints[i + 1] = new Vector3(0f, 0.08f, 0f);
                joints[i + 3] = new Vector3(0f, 0.05f, 0f); // Tip close to wrist
            }

            var pose = SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, pinchDist: 0.1f);
            Assert.AreEqual(HandPose.ThumbsUp, pose);
        }

        [Test]
        public void RecognizesFistWithTheThumbAcrossTheFingers()
        {
            var joints = CreateMockJoints();
            joints[0] = Vector3.zero;
            for (int i = 5; i < 21; i += 4)
            {
                joints[i] = new Vector3(0f, 0.05f, 0f);
                joints[i + 1] = new Vector3(0f, 0.08f, 0f);
                joints[i + 3] = new Vector3(0f, 0.05f, 0f);
            }
            // Thumb lying sideways across the fingers: far from the wrist, but not pointing up
            joints[2] = new Vector3(0.03f, 0.02f, 0f);
            joints[4] = new Vector3(-0.03f, 0.04f, 0f);

            Assert.AreEqual(HandPose.Fist, SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, 0.1f));
        }

        [Test]
        public void VideoIsCroppedNotStretchedToFillThePhoneCard()
        {
            // A 9:16 vertical clip in the 248 x 310 card (0.8): full width, top and bottom cropped
            var vertical = SpatialPhoneScreen.CropToFill(9f / 16f, 0.8f);
            Assert.AreEqual(1f, vertical.width, 1e-5f);
            Assert.AreEqual(0.703125f, vertical.height, 1e-5f);
            Assert.AreEqual((1f - 0.703125f) / 2f, vertical.y, 1e-5f);

            // A 16:9 clip: full height, sides cropped
            var wide = SpatialPhoneScreen.CropToFill(16f / 9f, 0.8f);
            Assert.AreEqual(1f, wide.height, 1e-5f);
            Assert.AreEqual(0.45f, wide.width, 1e-5f);
        }

        [Test]
        public void RemoteScreenIsLetterboxedInTheWindow()
        {
            var area = new Vector2(460f, 220f); // the window's text area
            var wide = RemoteDesktopReceiver.FitInside(16f / 9f, area);   // a 16:9 PC screen: full height
            Assert.AreEqual(220f, wide.y, 1e-3f);
            Assert.AreEqual(220f * 16f / 9f, wide.x, 1e-3f);
            var ultra = RemoteDesktopReceiver.FitInside(32f / 9f, area);  // wider than the area: full width
            Assert.AreEqual(460f, ultra.x, 1e-3f);
            Assert.AreEqual(460f * 9f / 32f, ultra.y, 1e-3f);
        }

        [Test]
        public void FeedClipsCycleForwardAndBackwardCorrectly()
        {
            int totalClips = 4;
            int current = 0;

            // Swipe Up -> Next clip
            current = (current + 1) % totalClips;
            Assert.AreEqual(1, current);

            current = (current + 1) % totalClips; // 2
            current = (current + 1) % totalClips; // 3
            current = (current + 1) % totalClips; // Wraps around to 0
            Assert.AreEqual(0, current);

            // Swipe Down -> Prev clip
            current = (current - 1 + totalClips) % totalClips;
            Assert.AreEqual(3, current);
        }

        [Test]
        public void MessengerMessageAppendsToConversationList()
        {
            var conversation = new System.Collections.Generic.List<string>
            {
                "💬 [Marek]: Hej!",
                "💬 [Anna]: Cześć!"
            };

            string draft = "Piszę z wirtualnej klawiatury!";
            conversation.Add($"💬 [Ty]: {draft}");

            Assert.AreEqual(3, conversation.Count);
            Assert.IsTrue(conversation[2].Contains("Piszę z wirtualnej klawiatury!"));
        }

        // ---- Palm direction: it must not depend on which hand it is ----

        [Test]
        public void PalmFacingFromImageHasOppositeSignsForRightAndLeftHands()
        {
            // Image pixels, x right and y down: a right hand held up with its palm to the camera has the
            // index knuckle to the right of the pinky knuckle.
            var wrist = new Vector2(100f, 200f);
            var index = new Vector2(130f, 120f);
            var pinky = new Vector2(70f, 130f);

            Assert.Greater(PalmGesture.Facing(wrist, index, pinky, true), 0.5f);
            Assert.Less(PalmGesture.Facing(wrist, index, pinky, false), -0.5f);
        }

        [Test]
        public void OpenPalmIsReadFromTheCameraFacingValue()
        {
            var joints = CreateOpenHand(false);
            Assert.AreEqual(HandPose.OpenPalmUser,
                SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, 0.1f, true, 0.9f));
            Assert.AreEqual(HandPose.OpenPalmForward,
                SpatialGestureDetector.EvaluatePose(joints, Vector3.forward, 0.1f, true, -0.9f));
        }

        [Test]
        public void OpenPalmFrom3DJointsIsTheSameForBothHands()
        {
            // The user looks along +Z; the palm is toward the user. A right hand has its index knuckle
            // to the user's right of the pinky knuckle, a left hand to the left.
            var right = CreateOpenHand(false);
            var left = CreateOpenHand(true);

            Assert.AreEqual(HandPose.OpenPalmUser, SpatialGestureDetector.EvaluatePose(right, Vector3.forward, 0.1f, true));
            Assert.AreEqual(HandPose.OpenPalmUser, SpatialGestureDetector.EvaluatePose(left, Vector3.forward, 0.1f, false));

            // Turned around (the user looks along -Z at the same hands): the palm faces away.
            Assert.AreEqual(HandPose.OpenPalmForward, SpatialGestureDetector.EvaluatePose(right, Vector3.back, 0.1f, true));
            Assert.AreEqual(HandPose.OpenPalmForward, SpatialGestureDetector.EvaluatePose(left, Vector3.back, 0.1f, false));
        }

        // A flat open hand, fingers up (+Y), palm toward a user looking along +Z; mirrored for a left hand.
        static Vector3[] CreateOpenHand(bool leftHand)
        {
            float m = leftHand ? -1f : 1f;
            var joints = CreateMockJoints();
            joints[0] = Vector3.zero;
            joints[2] = new Vector3(0.03f * m, 0.02f, 0f);
            joints[4] = new Vector3(0.07f * m, 0.06f, 0f);

            // Index, middle, ring, pinky: knuckle, middle joint and tip, each straight up from its knuckle
            float[] x = { 0.02f, 0.005f, -0.012f, -0.03f };
            float[] y = { 0.09f, 0.09f, 0.085f, 0.07f };
            for (int k = 0; k < 4; k++)
            {
                int mcp = 5 + 4 * k;
                joints[mcp] = new Vector3(x[k] * m, y[k], 0f);
                joints[mcp + 1] = new Vector3(x[k] * m, y[k] + 0.03f, 0f);
                joints[mcp + 2] = new Vector3(x[k] * m, y[k] + 0.055f, 0f);
                joints[mcp + 3] = new Vector3(x[k] * m, y[k] + 0.08f, 0f);
            }
            return joints;
        }

        static Vector3[] CreateMockJoints()
        {
            var joints = new Vector3[HandGeometry.JointCount];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = Vector3.zero;
            return joints;
        }
    }
}
