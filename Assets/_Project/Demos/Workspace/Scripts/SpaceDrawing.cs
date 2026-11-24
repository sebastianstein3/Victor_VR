using System.Collections.Generic;
using CardboardXR.Demos.Paint;
using CardboardXR.Hands;
using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // Drawing in the room (Więcej > Rysowanie, "rysuj"): while the drawing panel is open, a pinch that
    // isn't on a window draws a stroke from the pinching fingertips, with the Paint demo's ink
    // (PaintStroke, StrokeMesh) and the same smoothing and jump handling. The strokes stay in the room
    // (one room anchor for all, WorkspaceAnchors) after the panel is closed, until "Wyczyść". Needs hand
    // tracking in 6DoF: the 3DoF pinch sensor doesn't know where the hand is.
    public class SpaceDrawing : MonoBehaviour
    {
        public static readonly Color[] Colors =
        {
            Color.white, new(1f, 0.25f, 0.25f), new(1f, 0.6f, 0.15f), new(1f, 0.92f, 0.2f),
            new(0.3f, 0.95f, 0.4f), new(0.3f, 0.45f, 1f),
        };
        public static readonly float[] Widths = { 0.004f, 0.01f, 0.022f };
        const float k_JumpDistance = 0.06f;
        const float k_MaxHandSpeed = 3f;
        const float k_BlipLength = 0.03f;

        CoreServices m_Core;
        Material m_Material;
        readonly List<PaintStroke> m_Strokes = new();
        readonly OneEuroFilter m_Smoothing = new(2f, 1.5f);
        PaintStroke m_Current;
        bool m_WasTracking;

        public bool Active { get; set; }
        public int ColorIndex { get; set; } = 5;
        public int WidthIndex { get; set; } = 1;
        public int StrokeCount => m_Strokes.Count;

        public void Initialize(CoreServices core)
        {
            m_Core = core;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                m_Material = new Material(shader) { name = "Workspace Ink" };
        }

        // A pinch began (6DoF): a new stroke from the fingertips
        public void BeginStroke()
        {
            if (m_Material == null || m_Core.HandTracker == null || !m_Core.HandTracker.IsTracking)
                return;
            m_Smoothing.Reset();
            StartStroke(Tip());
        }

        public void EndStroke()
        {
            if (m_Current == null)
                return;
            var stroke = m_Current;
            m_Current = null;
            if (stroke.PointCount < 2 || stroke.Length < k_BlipLength) // a blip, not a line
            {
                m_Strokes.Remove(stroke);
                Destroy(stroke.gameObject);
            }
        }

        void StartStroke(Vector3 start)
        {
            var strokeObject = new GameObject($"Stroke {m_Strokes.Count + 1}", typeof(MeshFilter), typeof(MeshRenderer));
            strokeObject.transform.SetParent(transform, false); // points are kept in its local space (PaintStroke.Add)
            var stroke = strokeObject.AddComponent<PaintStroke>();
            stroke.Begin(BrushStyle.Ink, Widths[WidthIndex], Colors[ColorIndex], m_Material);
            stroke.Add(start, TowardViewer(start));
            m_Strokes.Add(stroke);
            m_Current = stroke;
        }

        public void Undo()
        {
            if (m_Current != null || m_Strokes.Count == 0)
                return;
            Destroy(m_Strokes[^1].gameObject);
            m_Strokes.RemoveAt(m_Strokes.Count - 1);
        }

        public void Clear()
        {
            EndStroke();
            foreach (var stroke in m_Strokes)
            {
                if (stroke != null)
                    Destroy(stroke.gameObject);
            }
            m_Strokes.Clear();
        }

        Vector3 Tip() => m_Smoothing.Filter(m_Core.HandTracker.PinchPoint, Time.unscaledTimeAsDouble);

        Vector3 TowardViewer(Vector3 point)
        {
            var toward = m_Core.Head.position - point;
            return toward.sqrMagnitude > 1e-6f ? toward.normalized : -m_Core.Head.forward;
        }

        void Update()
        {
            if (m_Current == null || m_Core == null)
                return;
            var tracker = m_Core.HandTracker;
            bool tracking = tracker != null && tracker.isActiveAndEnabled && tracker.IsTracking;
            if (!tracking)
            {
                m_WasTracking = false;
                return;
            }
            if (!m_WasTracking)
                m_Smoothing.Reset();
            m_WasTracking = true;
            var tip = Tip();
            // The tip jumped (a lost hand found again elsewhere): a new stroke there, not a streak across the room
            if (Vector3.Distance(tip, m_Current.LastPoint) > Mathf.Max(k_JumpDistance, k_MaxHandSpeed * Time.unscaledDeltaTime))
            {
                EndStroke();
                StartStroke(tip);
            }
            m_Current.Add(tip, TowardViewer(tip));
            if (m_Current.IsFull)
                StartStroke(m_Current.LastPoint);
        }

        void OnDestroy()
        {
            Clear();
            if (m_Material != null)
                Destroy(m_Material);
        }
    }

    // The drawing panel: colours, line width, undo, clear and done. While it is open, pinches draw.
    public class DrawControls : WidgetPanel
    {
        public override string Kind => "rysowanie";
        public SpaceDrawing Drawing { get; set; }

        readonly List<RectTransform> m_Swatches = new();
        readonly List<M3Button> m_Widths = new();
        M3Button m_Clear;
        float m_ClearArmedUntil;

        protected override void Build(Vector2 size)
        {
            for (int i = 0; i < SpaceDrawing.Colors.Length; i++)
            {
                int index = i;
                var swatch = M3.Surface($"Color_{i}", m_Canvas, new Vector2((i - 2.5f) * 58f, 36f), new Vector2(48f, 48f),
                    SpaceDrawing.Colors[i], 24f, raycastTarget: true);
                var button = swatch.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                button.onClick.AddListener(() =>
                {
                    WorkspaceSounds.PlayTap();
                    if (Drawing != null)
                        Drawing.ColorIndex = index;
                    Refresh();
                });
                m_Swatches.Add(swatch.rectTransform);
            }
            string[] widths = { "S", "M", "L" };
            for (int i = 0; i < widths.Length; i++)
            {
                int index = i;
                m_Widths.Add(M3.Button($"Width_{i}", m_Canvas, widths[i], M3Button.Variant.Outlined, new Vector2(-150f + i * 60f, -28f),
                    () =>
                    {
                        if (Drawing != null)
                            Drawing.WidthIndex = index;
                        Refresh();
                    }, null, 56f));
            }
            M3.Button("Undo", m_Canvas, "Cofnij", M3Button.Variant.Tonal, new Vector2(100f, -28f), () => Drawing?.Undo(), "undo", 160f);
            m_Clear = M3.Button("Clear", m_Canvas, "Wyczyść", M3Button.Variant.Tonal, new Vector2(-88f, -92f), ClearAll, "delete", 170f);
            M3.Button("Done", m_Canvas, "Gotowe", M3Button.Variant.Filled, new Vector2(88f, -92f), Close, "check", 170f);
        }

        // The first press asks, a second within 3 s clears everything
        void ClearAll()
        {
            if (Time.unscaledTime > m_ClearArmedUntil)
            {
                m_ClearArmedUntil = Time.unscaledTime + 3f;
                m_Clear.Text = "Na pewno?";
                return;
            }
            m_ClearArmedUntil = 0f;
            m_Clear.Text = "Wyczyść";
            Drawing?.Clear();
        }

        public void Refresh()
        {
            for (int i = 0; i < m_Swatches.Count; i++)
                m_Swatches[i].localScale = Vector3.one * (Drawing != null && Drawing.ColorIndex == i ? 1.2f : 0.85f);
            for (int i = 0; i < m_Widths.Count; i++)
                m_Widths[i].Selected = Drawing != null && Drawing.WidthIndex == i;
        }

        protected override void Update()
        {
            base.Update();
            if (m_Clear != null && m_ClearArmedUntil > 0f && Time.unscaledTime > m_ClearArmedUntil)
            {
                m_ClearArmedUntil = 0f;
                m_Clear.Text = "Wyczyść";
            }
        }
    }
}
