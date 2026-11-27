using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // Ways to move and press things that don't depend on pinching, which the camera sees only now and then:
    //  * Looking at a button for about a second presses it (a ring fills on the gaze: "klik wzrokiem").
    //  * A window carried with its grab button (it follows the head) is put down by resting the head: turn
    //    where it should go, hold still for a second (a ring fills), it stays there.
    //  * A fist closed with the gaze on a window, widget, the phone, the dock, the cinema screen or a model
    //    grabs it; opening the hand lets it go. (A fist on nothing still moves the whole layout.)
    // All of it is switched off together with "wyłącz wzrok" (the dwell and the resting head); the fist stays.
    public partial class WorkspaceDemo
    {
        const string k_DwellPref = "CardboardXR.GazeDwell";
        const float k_DwellSeconds = 1.1f;
        const float k_RestSeconds = 1.2f;
        const float k_RestMovedDegrees = 12f;  // the carried thing must first be turned away this far
        const float k_RestStillDegrees = 3f;   // then the head counts as resting within this

        bool m_Dwell = true;
        GameObject m_DwellTarget;
        float m_DwellSince;
        bool m_DwellFired;

        // The ring
        GameObject m_Ring;
        Image m_RingFill;
        Texture2D m_RingTexture;
        Sprite m_RingSprite;

        // Putting down by resting the head
        bool m_RestTracking;
        bool m_RestMoved;
        Vector3 m_RestStart, m_RestRef;
        float m_RestSince;

        // The fist's grab: how to let go
        Action m_FistRelease;

        public bool GazeDwell
        {
            get => m_Dwell;
            set
            {
                m_Dwell = value;
                PlayerPrefs.SetInt(k_DwellPref, value ? 1 : 0);
                if (!value)
                    HideRing();
                ShowToast(value ? "Klik wzrokiem włączony: patrz na przycisk ok. sekundę" : "Klik wzrokiem wyłączony");
            }
        }

        // The picture's size on the phone's screen, which sets how much of the lenses' black margin shows
        // (StereoRig.Zoom): by 0.1 a step, NaN for the default
        void SetPictureZoom(float delta)
        {
            var rig = m_Core != null ? m_Core.Rig : null;
            if (rig == null)
                return;
            rig.Zoom = float.IsNaN(delta) ? 1.2f : rig.Zoom + delta;
            ShowToast($"Obraz: {Mathf.RoundToInt(rig.Zoom * 100f)}% (powiększ obraz / pomniejsz obraz)");
        }

        void InitControl()
        {
            m_Dwell = PlayerPrefs.GetInt(k_DwellPref, 1) != 0;
        }

        void ShutdownControl()
        {
            ReleaseFist();
            if (m_Ring != null)
                Destroy(m_Ring);
            if (m_RingSprite != null)
                Destroy(m_RingSprite);
            if (m_RingTexture != null)
                Destroy(m_RingTexture);
            m_Ring = null;
        }

        void UpdateControl()
        {
            if (m_Core == null || m_Core.Gaze == null)
                return;
            bool ringShown = false;
            if (m_Dwell)
            {
                ringShown = UpdateRest();
                if (!ringShown)
                    ringShown = UpdateDwell();
            }
            if (!ringShown)
                HideRing();
        }

        // ---- Looking at a button ----

        bool UpdateDwell()
        {
            var gaze = m_Core.Gaze;
            var target = gaze.Target;
            Selectable button = target != null ? target.GetComponentInParent<Selectable>() : null;
            // The desk's keys are touched, not looked at
            if (button == null || !button.interactable || !button.isActiveAndEnabled ||
                button.GetComponentInParent<DeskKeyboard>() != null || button.GetComponentInParent<DeskTrackpad>() != null)
            {
                m_DwellTarget = null;
                return false;
            }
            if (button.gameObject != m_DwellTarget)
            {
                m_DwellTarget = button.gameObject;
                m_DwellSince = Time.unscaledTime;
                m_DwellFired = false;
            }
            if (m_DwellFired)
                return false;
            float progress = (Time.unscaledTime - m_DwellSince) / k_DwellSeconds;
            ShowRing(gaze.TargetDistance, progress);
            if (progress >= 1f)
            {
                m_DwellFired = true; // pressing again needs the gaze to leave the button and come back
                gaze.TriggerDown();
                gaze.TriggerUp();
                HideRing();
                return false;
            }
            return true;
        }

        // ---- Resting the head puts a carried window down ----

        bool CarriedByHead()
        {
            if (m_GroupDrag || m_FistRelease != null)
                return false;
            foreach (var screen in m_Screens)
            {
                if (screen != null && screen.IsDragging)
                    return true;
            }
            if (m_PhoneScreen != null && m_PhoneScreen.IsDragging)
                return true;
            foreach (var widget in m_Widgets)
            {
                if (widget != null && widget.IsDragging)
                    return true;
            }
            if (m_DockMovable != null && m_DockMovable.IsDragging)
                return true;
            if (m_CinemaMovable != null && m_CinemaMovable.IsDragging)
                return true;
            foreach (var model in m_Models)
            {
                if (model != null && model.Carry != null && model.Carry.IsCarried && !model.Carry.HandCarried)
                    return true;
            }
            return m_Hologram != null && m_Hologram.Carry != null && m_Hologram.Carry.IsCarried && !m_Hologram.Carry.HandCarried;
        }

        bool UpdateRest()
        {
            if (!CarriedByHead())
            {
                m_RestTracking = false;
                return false;
            }
            Vector3 forward = m_Core.Head.forward;
            float now = Time.unscaledTime;
            if (!m_RestTracking)
            {
                m_RestTracking = true;
                m_RestMoved = false;
                m_RestStart = m_RestRef = forward;
                m_RestSince = now;
                return false;
            }
            if (!m_RestMoved)
            {
                if (Vector3.Angle(forward, m_RestStart) > k_RestMovedDegrees)
                {
                    m_RestMoved = true;
                    m_RestRef = forward;
                    m_RestSince = now;
                }
                return false;
            }
            if (Vector3.Angle(forward, m_RestRef) > k_RestStillDegrees)
            {
                m_RestRef = forward;
                m_RestSince = now;
                return false;
            }
            float progress = (now - m_RestSince) / k_RestSeconds;
            ShowRing(1.2f, progress);
            if (progress >= 1f)
            {
                DropCarried();
                m_RestTracking = false;
                ShowToast("Odłożone");
                return false;
            }
            return true;
        }

        // ---- The fist ----

        void ReleaseFist()
        {
            var release = m_FistRelease;
            m_FistRelease = null;
            release?.Invoke();
        }

        bool TryFistGrab()
        {
            if (m_Core == null || m_Core.Gaze == null || m_Core.Gaze.Target == null)
                return false;
            var target = m_Core.Gaze.Target;
            var screen = target.GetComponentInParent<VirtualScreen>();
            if (screen != null)
            {
                screen.StartDragging();
                if (!screen.IsDragging)
                    return false; // pinned
                SetActiveScreen(screen);
                m_FistRelease = screen.StopDragging;
                return true;
            }
            var widget = target.GetComponentInParent<WidgetPanel>();
            if (widget != null)
            {
                widget.StartDragging();
                m_FistRelease = widget.StopDragging;
                return true;
            }
            var phone = target.GetComponentInParent<SpatialPhoneScreen>();
            if (phone != null)
            {
                phone.StartDragging();
                m_FistRelease = phone.StopDragging;
                return true;
            }
            var movable = target.GetComponentInParent<MovablePanel>();
            if (movable != null)
            {
                movable.StartDragging();
                m_FistRelease = movable.StopDragging;
                return true;
            }
            var carry = target.GetComponentInParent<Carryable>();
            if (carry != null)
            {
                carry.BeginCarry(false); // follows the head
                m_FistRelease = carry.EndCarry;
                return true;
            }
            return false;
        }

        // ---- The ring on the gaze ----

        void ShowRing(float distance, float progress)
        {
            if (m_Ring == null)
                BuildRing();
            var head = m_Core.Head;
            float d = Mathf.Clamp(distance, 0.4f, 6f);
            m_Ring.transform.position = head.position + head.forward * (d - 0.03f);
            m_Ring.transform.rotation = head.rotation;
            m_Ring.transform.localScale = Vector3.one * (d * 0.00045f); // 100 units: ~2.6 degrees
            m_RingFill.fillAmount = Mathf.Clamp01(progress);
            if (!m_Ring.activeSelf)
                m_Ring.SetActive(true);
        }

        void HideRing()
        {
            if (m_Ring != null && m_Ring.activeSelf)
                m_Ring.SetActive(false);
        }

        void BuildRing()
        {
            const int size = 128;
            m_RingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Dwell Ring" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // a ring between 0.68 and 0.96 of the radius, soft at both edges
                    float a = Mathf.Clamp01((r - 0.66f) / 0.04f) * Mathf.Clamp01((0.97f - r) / 0.04f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            m_RingTexture.SetPixels32(pixels);
            m_RingTexture.Apply(false);
            m_RingSprite = Sprite.Create(m_RingTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);

            m_Ring = new GameObject("Dwell Ring", typeof(RectTransform));
            m_Ring.transform.SetParent(transform, false);
            var canvas = m_Ring.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)m_Ring.transform).sizeDelta = new Vector2(100f, 100f);

            var back = NewRingImage("Back", new Color(1f, 1f, 1f, 0.28f));
            back.type = Image.Type.Simple;
            m_RingFill = NewRingImage("Fill", M3Theme.Primary);
            m_RingFill.type = Image.Type.Filled;
            m_RingFill.fillMethod = Image.FillMethod.Radial360;
            m_RingFill.fillOrigin = (int)Image.Origin360.Top;
            m_RingFill.fillClockwise = true;
            m_Ring.SetActive(false);
        }

        Image NewRingImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(m_Ring.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.sprite = m_RingSprite;
            image.color = color;
            image.raycastTarget = false; // never in the way of the gaze
            return image;
        }
    }
}
