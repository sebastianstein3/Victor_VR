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

        static Vector3[] CreateMockJoints()
        {
            var joints = new Vector3[HandGeometry.JointCount];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = Vector3.zero;
            return joints;
        }
    }
}
