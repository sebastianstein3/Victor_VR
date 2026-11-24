using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Where new things appear. Like visionOS, a new window opens where the user is looking, not in a
    // fixed order: of the free screen slots (WorkspaceDemo's layout, kept so "Wyśrodkuj" can tidy up),
    // the one nearest the gaze, skipping any place a panel already covers, including windows that were
    // dragged out of their slot or stretched into a neighbour's. Widgets go above the windows near the
    // gaze, never in front of a window (the nearer panel would take the gaze and hide its buttons).
    public partial class WorkspaceDemo
    {
        // A panel as seen from the head: the direction of its centre and its half size, in degrees
        struct ViewBox
        {
            public float Yaw, Pitch, HalfWidth, HalfHeight;

            public bool Overlaps(ViewBox other, float gap) =>
                Mathf.Abs(Mathf.DeltaAngle(Yaw, other.Yaw)) < HalfWidth + other.HalfWidth + gap &&
                Mathf.Abs(Pitch - other.Pitch) < HalfHeight + other.HalfHeight + gap;
        }

        static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static float PitchOf(Vector3 direction) =>
            Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;

        ViewBox BoxOf(RectTransform panel)
        {
            Vector3 toPanel = panel.position - m_Core.Head.position;
            float distance = Mathf.Max(0.1f, toPanel.magnitude);
            var size = panel.rect.size;
            return new ViewBox
            {
                Yaw = YawOf(toPanel),
                Pitch = PitchOf(toPanel),
                HalfWidth = Mathf.Atan2(size.x * 0.5f * Mathf.Abs(panel.lossyScale.x), distance) * Mathf.Rad2Deg,
                HalfHeight = Mathf.Atan2(size.y * 0.5f * Mathf.Abs(panel.lossyScale.y), distance) * Mathf.Rad2Deg,
            };
        }

        // Every panel that takes up part of the view (the keyboard and trackpad lie far below: left out)
        List<ViewBox> OccupiedBoxes(Transform except = null)
        {
            var boxes = new List<ViewBox>();
            void AddBox(Component panel)
            {
                if (panel != null && panel.transform != except && panel.gameObject.activeInHierarchy &&
                    panel.transform is RectTransform rect)
                    boxes.Add(BoxOf(rect));
            }
            foreach (var screen in m_Screens)
                AddBox(screen);
            AddBox(m_PhoneScreen);
            AddBox(m_ControlDock);
            AddBox(m_CinemaPanel);
            foreach (var widget in m_Widgets)
                AddBox(widget);
            return boxes;
        }

        static bool Free(ViewBox box, List<ViewBox> occupied, float gap)
        {
            foreach (var other in occupied)
            {
                if (box.Overlaps(other, gap))
                    return false;
            }
            return true;
        }

        // The free slot nearest the gaze; the main row is preferred over the row above
        int ChooseScreenSlot()
        {
            const int slotsToTry = 15; // three rows of five
            Vector3 gaze = m_Core.Head.forward;
            float gazeYaw = YawOf(gaze), gazePitch = PitchOf(gaze), homeYaw = m_HomeYaw.eulerAngles.y;
            var occupied = OccupiedBoxes();
            int best = -1;
            float bestCost = float.MaxValue;
            for (int slot = 0; slot < slotsToTry; slot++)
            {
                if (m_ScreenSlots.ContainsValue(slot))
                    continue;
                int row = slot / k_ScreenSlotYaws.Length;
                var box = new ViewBox
                {
                    Yaw = homeYaw + k_ScreenSlotYaws[slot % k_ScreenSlotYaws.Length],
                    Pitch = k_ScreenPitch + row * k_ScreenRowPitch,
                    HalfWidth = 20.5f, // 520 x 440 dp at 1x
                    HalfHeight = 17.5f,
                };
                if (!Free(box, occupied, -1f)) // neighbouring slots touch: allow a degree
                    continue;
                float cost = Mathf.Abs(Mathf.DeltaAngle(box.Yaw, gazeYaw)) + 1.5f * Mathf.Abs(box.Pitch - gazePitch) + row * 15f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = slot;
                }
            }
            if (best >= 0)
                return best;
            // Everything near is covered: the first slot nobody holds (old behaviour)
            int first = 0;
            while (m_ScreenSlots.ContainsValue(first))
                first++;
            return first;
        }

        // A widget of sizeDp at distance: above the windows near where the user looks, clear of all panels
        void PlaceWidget(RectTransform widget, Vector2 sizeDp, float distance)
        {
            Vector3 gaze = m_Core.Head.forward;
            float gazeYaw = YawOf(gaze), gazePitch = PitchOf(gaze);
            var occupied = OccupiedBoxes(widget);
            // 1 dp is about 0.08 degrees at the distance the panel was made for
            float halfWidth = sizeDp.x * 0.04f, halfHeight = sizeDp.y * 0.04f;
            float bestYaw = gazeYaw, bestPitch = Mathf.Clamp(gazePitch, -10f, 30f) + 14f, bestCost = float.MaxValue;
            float[] pitches = { gazePitch, k_ScreenPitch + 17.5f + 3f + halfHeight, gazePitch + 30f, gazePitch - 25f };
            for (int p = 0; p < pitches.Length; p++)
            {
                for (int step = 0; step <= 8; step++)
                {
                    // 0, +1, -1, +2, -2... steps of 26 degrees around the gaze
                    float yaw = gazeYaw + (step % 2 == 1 ? 1 : -1) * ((step + 1) / 2) * 26f;
                    var box = new ViewBox { Yaw = yaw, Pitch = pitches[p], HalfWidth = halfWidth, HalfHeight = halfHeight };
                    if (box.Pitch > 60f || box.Pitch < -30f || !Free(box, occupied, 2f))
                        continue;
                    float cost = Mathf.Abs(Mathf.DeltaAngle(yaw, gazeYaw)) + Mathf.Abs(box.Pitch - gazePitch) * 0.8f;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestYaw = yaw;
                        bestPitch = box.Pitch;
                    }
                }
            }
            if (m_RoomAnchors != null)
                m_RoomAnchors.Release(widget);
            Vector3 position = m_Core.Head.position + Quaternion.Euler(-bestPitch, bestYaw, 0f) * Vector3.forward * distance;
            widget.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_Core.Head.position, Vector3.up));
        }

        // The window nearest the gaze (for "which one now" after a window closes)
        VirtualScreen ScreenNearestGaze()
        {
            VirtualScreen nearest = null;
            float bestAngle = float.MaxValue;
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                float angle = Vector3.Angle(m_Core.Head.forward, screen.transform.position - m_Core.Head.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    nearest = screen;
                }
            }
            return nearest;
        }
    }
}
