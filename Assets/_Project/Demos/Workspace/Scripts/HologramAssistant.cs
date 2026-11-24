using System;
using System.Collections.Generic;
using System.Text;
using CardboardXR.Demos;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // The assistant as a hologram, like Jarvis's. Two looks put together:
    //  * the inside is a glass planet, as on the voice-interface console: a faint glass ball that glows at
    //    its rim, a wire globe of meridians and parallels, a soft orange glow with a white heart, a lens flare
    //    across it, and three tilted rings with dots circling it;
    //  * the outside is like the golden hologram from the film: a halo made of thousands of glowing
    //    circuit-like fragments on two turning shells, bright arcs sweeping round like comets with a spark at
    //    their head, drifting golden sparks, and a thin gauge ring round it all. Below it, a projector base in
    //    the same gold: segmented rings turning against each other, a floor of circuit fragments, a radar
    //    sweep, a rim of equalizer ticks and a beam of light up to the planet.
    // It stands on the table you look at (or floats), can be picked up and turned like a model (Carryable),
    // and shows what the assistant is doing:
    //   idle       slow and calm
    //   listening  the halo and rings draw in, brighter
    //   thinking   everything spins faster and glows deep amber, the core flickers
    //   speaking   the core and ticks pulse with every word the phone reads, rings of light go out from the
    //              planet, and the words appear as subtitles with the current one lit
    // Everything is built from code: lines, soft dots and a few triangles, drawn additively by the shader
    // "CardboardXR/Hologram" (rim light, travelling light along lines, stretched glints). Beside it, a status
    // panel (HologramHud); below it, the name and four buttons: stop, close, Zapytaj, Na blat.
    public class HologramAssistant : MonoBehaviour
    {
        public const string DisplayName = "V.I.C.T.O.R.";

        const float k_BaseRadius = 0.14f;  // m, the projector base
        const float k_Height = 0.27f;      // m, the globe's centre above the base
        const float k_Radius = 0.085f;     // m, the globe
        const int k_Ticks = 72;
        const float k_Scale = 1.2f;        // the whole figure, about 43 cm tall
        static readonly Color k_Cyan = new(0.25f, 0.85f, 1f);
        static readonly Color k_Orange = new(1f, 0.55f, 0.15f);
        static readonly Color k_White = new(0.92f, 0.98f, 1f);
        static readonly Color k_Gold = new(1f, 0.72f, 0.22f);
        static readonly Color k_Hot = new(1f, 0.88f, 0.55f);
        static readonly Color k_Amber = new(1f, 0.5f, 0.12f);
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Tint = Shader.PropertyToID("_Tint");
        static readonly int k_Warm = Shader.PropertyToID("_Warm");
        static readonly int k_Pulse = Shader.PropertyToID("_Pulse");
        static readonly int k_PulseTime = Shader.PropertyToID("_PulseTime");

        public Carryable Carry { get; private set; }
        public event Action<HologramAssistant> Closed;
        public event Action<HologramAssistant> PlaceRequested; // "Na blat": stand on the surface looked at
        public event Action<HologramAssistant> ModelRequested;  // "Model": choose the AI model from the key's list
        public event Action<HologramAssistant> MemoryRequested; // "Pamięć": what V.I.C.T.O.R. remembers

        // The status panel beside the figure ("pokaż hud" / "ukryj hud")
        public bool HudVisible
        {
            get => m_Hud != null && m_Hud.gameObject.activeSelf;
            set
            {
                if (m_Hud != null)
                    m_Hud.gameObject.SetActive(value);
            }
        }

        class Ring
        {
            public Transform Pivot, Spin;
            public Renderer Line, Dots;
            public float Speed, Angle;
        }

        // A bright arc sweeping round (a comet): turns about its own tilted axis, a spark at its head
        class Comet
        {
            public Transform Pivot, Spin;
            public Renderer Line, Dots;
            public float Speed, Angle;
        }

        class Wave
        {
            public Transform Transform;
            public Renderer Renderer;
            public float Born = -10f;
        }

        struct Word
        {
            public int Start, End;
        }

        CoreServices m_Core;
        Transform m_Head;
        AssistantSession m_Session;
        Material m_LineMaterial, m_DotMaterial, m_ShellMaterial;
        MaterialPropertyBlock m_Block;
        readonly List<Mesh> m_Meshes = new();

        Transform m_Body, m_Globe, m_CoreGlow, m_DustPivot; // m_Body holds the figure (scaled); the panels stay unscaled beside it
        Transform m_DialPivot, m_Dial, m_ShellPivotA, m_ShellPivotB;
        Renderer m_GlobeLines, m_Glass, m_CoreRenderer, m_Beam, m_Disc, m_TickRenderer, m_Dust, m_Foot;
        Renderer m_BaseRingA, m_BaseRingB, m_BaseRingC, m_Debris, m_Sweep, m_ShellA, m_ShellB, m_DialRenderer, m_Flare;
        float m_BaseAngle, m_SweepAngle, m_PulseClock, m_DustScale = 1f, m_HaloScale = 1f;
        HologramHud m_Hud;
        readonly List<Comet> m_Comets = new();
        readonly List<Ring> m_Rings = new();
        readonly Wave[] m_Waves = new Wave[3];
        int m_NextWave;
        float m_LastWave = -10f;
        Mesh m_TickMesh;
        Vector3[] m_TickVertices;

        RectTransform m_Controls, m_Subtitles;
        TextMeshProUGUI m_SubtitleText;
        string m_SubtitleKey = "";

        float m_GlobeAngle;
        float m_Spin = 8f, m_RingSpin = 22f, m_RingScale = 1f, m_Glow = 0.6f;
        Color m_Tint = Color.white;
        float m_Warm; // 0..1: amber while thinking
        AssistantState m_PreviousState;

        readonly List<Word> m_Words = new();
        string m_AnswerText = "";
        int m_CurrentWord = -1;
        float m_ClearAt = float.PositiveInfinity;
        string m_Notice;
        float m_NoticeUntil;

        // False when the shader is missing (it is added to Always Included Shaders by a build step)
        public bool Initialize(CoreServices core, AssistantSession session)
        {
            m_Core = core;
            m_Head = core.Head;
            m_Session = session;
            var shader = Shader.Find("CardboardXR/Hologram");
            if (shader == null)
            {
                Debug.LogWarning("[Hologram] Brak shadera CardboardXR/Hologram");
                return false;
            }
            m_Block = new MaterialPropertyBlock();
            m_LineMaterial = new Material(shader) { name = "Hologram Lines" };
            m_LineMaterial.SetFloat("_Billboard", 0f);
            m_DotMaterial = new Material(shader) { name = "Hologram Dots" };
            m_DotMaterial.SetFloat("_Billboard", 1f);
            m_ShellMaterial = new Material(shader) { name = "Hologram Shell" }; // lit at its edges only
            m_ShellMaterial.SetFloat("_Billboard", 0f);
            m_ShellMaterial.SetFloat("_Fresnel", 2.2f);

            m_Body = new GameObject("Body").transform;
            m_Body.SetParent(transform, false);
            m_Body.localScale = Vector3.one * k_Scale;

            BuildBase();
            BuildGlobe();
            BuildGlass();
            BuildCore();
            BuildFlare();
            BuildRings();
            BuildAureola();
            BuildComets();
            BuildDial();
            BuildWaves();
            BuildTicks();
            BuildDust();
            BuildPanels();

            var box = gameObject.AddComponent<BoxCollider>(); // what the gaze hits, to pick it up
            box.center = new Vector3(0f, (k_Height + k_Radius) * k_Scale / 2f, 0f);
            box.size = new Vector3(0.3f, (k_Height + k_Radius + 0.02f) * k_Scale, 0.3f) * 1.0f;
            Carry = gameObject.AddComponent<Carryable>();
            Carry.Initialize(core);

            session.Answered += OnAnswered;
            session.Notice += OnNotice;
            session.WordSpoken += OnWord;
            return true;
        }

        public void Close()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        // ---- Building ----

        Renderer Part(string name, Transform parent, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_Meshes.Add(mesh);
            return renderer;
        }

        void Look(Renderer renderer, float intensity, Color tint, float warm = -1f)
        {
            if (renderer == null)
                return;
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetFloat(k_Intensity, intensity);
            m_Block.SetColor(k_Tint, tint);
            m_Block.SetFloat(k_Warm, warm < 0f ? m_Warm : warm); // the core is warm already: it passes 0
            renderer.SetPropertyBlock(m_Block);
        }

        static Color Alpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        // The base: four rings and an orange dial, a faint disc, a beam up to the globe, a glow at its foot
        void BuildBase()
        {
            BuildBaseRings();
            BuildDebris();
            BuildSweep();

            // The disc: bright in the middle, gone at the rim
            var dv = new List<Vector3> { new(0f, 0.001f, 0f) };
            var dc = new List<Color> { Alpha(k_Gold, 0.16f) };
            var dt = new List<int>();
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                dv.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * k_BaseRadius + new Vector3(0f, 0.001f, 0f));
                dc.Add(Alpha(k_Gold, 0.03f));
            }
            for (int i = 0; i < segments; i++)
            {
                dt.Add(0);
                dt.Add(1 + i);
                dt.Add(1 + (i + 1) % segments);
            }
            m_Disc = Part("Base Disc", m_Body, HoloMesh.Surface(dv, dc, dt, "Base disc"), m_LineMaterial, Vector3.zero);

            // The beam: a cone from the middle of the base up to just under the globe, fading out
            var bv = new List<Vector3>();
            var bc = new List<Color>();
            var bt = new List<int>();
            const int sides = 24;
            float top = k_Height - k_Radius * 0.8f;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                bv.Add(new Vector3(Mathf.Cos(a) * 0.07f, 0.003f, Mathf.Sin(a) * 0.07f));
                bc.Add(Alpha(k_Gold, 0.13f));
                bv.Add(new Vector3(Mathf.Cos(a) * 0.03f, top, Mathf.Sin(a) * 0.03f));
                bc.Add(Alpha(k_Gold, 0f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = (i + 1) % sides * 2;
                bt.Add(a); bt.Add(a + 1); bt.Add(b + 1);
                bt.Add(a); bt.Add(b + 1); bt.Add(b);
            }
            m_Beam = Part("Beam", m_Body, HoloMesh.Surface(bv, bc, bt, "Beam"), m_LineMaterial, Vector3.zero);

            var foot = HoloMesh.Dots(new List<Vector3> { new(0f, 0.01f, 0f) }, new List<float> { 0.075f },
                new List<Color> { Alpha(k_Gold, 0.25f) }, "Foot glow");
            m_Foot = Part("Foot Glow", m_Body, foot, m_DotMaterial, Vector3.zero);
        }

        static Vector3 Polar(float radius, float angle, float y = 0.002f) => new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

        // A circular arc of points at a radius (angles in degrees), as line segments
        static void Arc(List<Vector3> v, List<Color> c, float radius, float fromDegrees, float spanDegrees, Color color, float y = 0.002f)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(spanDegrees / 5f));
            var previous = Polar(radius, fromDegrees * Mathf.Deg2Rad, y);
            for (int i = 1; i <= steps; i++)
            {
                var next = Polar(radius, (fromDegrees + spanDegrees * i / steps) * Mathf.Deg2Rad, y);
                HoloMesh.Segment(v, c, previous, next, color, color);
                previous = next;
            }
        }

        // Three rings on the base, each its own mesh so they turn against each other: A: the outer circle with
        // tick marks and the orange dial; B: arcs with gaps; C: dashes, an inner circle and spokes
        void BuildBaseRings()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            Arc(v, c, k_BaseRadius, 0f, 360f, Alpha(k_Gold, 0.6f));
            for (int i = 0; i < 60; i++)
            {
                float a = i * Mathf.PI * 2f / 60f;
                float inner = k_BaseRadius * (i % 5 == 0 ? 0.88f : 0.93f);
                HoloMesh.Segment(v, c, Polar(k_BaseRadius * 0.97f, a), Polar(inner, a), Alpha(k_Gold, 0.5f), Alpha(k_Gold, 0.5f));
            }
            Arc(v, c, k_BaseRadius * 1.08f, 0f, 360f, Alpha(k_Amber, 0.55f));
            m_BaseRingA = Part("Base Ring A", m_Body, HoloMesh.Lines(v, c, "Base ring A"), m_LineMaterial, Vector3.zero);

            v = new List<Vector3>();
            c = new List<Color>();
            for (int k = 0; k < 3; k++)
            {
                Arc(v, c, k_BaseRadius * 0.78f, k * 120f, 80f, Alpha(k_Gold, 0.75f));
                Arc(v, c, k_BaseRadius * 0.72f, k * 120f + 60f, 40f, Alpha(k_Gold, 0.45f));
            }
            Arc(v, c, k_BaseRadius * 0.86f, 0f, 360f, Alpha(k_Gold, 0.18f));
            m_BaseRingB = Part("Base Ring B", m_Body, HoloMesh.Lines(v, c, "Base ring B"), m_LineMaterial, Vector3.zero);

            v = new List<Vector3>();
            c = new List<Color>();
            for (int k = 0; k < 24; k++)
                Arc(v, c, k_BaseRadius * 0.55f, k * 15f, 9f, Alpha(k_Gold, 0.55f));
            Arc(v, c, k_BaseRadius * 0.3f, 0f, 360f, Alpha(k_Gold, 0.35f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                HoloMesh.Segment(v, c, Polar(k_BaseRadius * 0.3f, a), Polar(k_BaseRadius * 0.55f, a), Alpha(k_Gold, 0.25f), Alpha(k_Gold, 0.25f));
            }
            m_BaseRingC = Part("Base Ring C", m_Body, HoloMesh.Lines(v, c, "Base ring C"), m_LineMaterial, Vector3.zero);
        }

        // A radar sweep over the base: a 70 degree wedge, brightest at its leading edge, going round
        void BuildSweep()
        {
            var v = new List<Vector3> { new(0f, 0.0025f, 0f) };
            var c = new List<Color> { Alpha(k_Gold, 0.05f) };
            var t = new List<int>();
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float degrees = -70f * i / steps;
                v.Add(Polar(k_BaseRadius * 0.95f, degrees * Mathf.Deg2Rad, 0.0025f));
                c.Add(Alpha(k_Gold, Mathf.Lerp(0.34f, 0f, (float)i / steps)));
            }
            for (int i = 0; i < steps; i++)
            {
                t.Add(0);
                t.Add(1 + i);
                t.Add(2 + i);
            }
            m_Sweep = Part("Sweep", m_Body, HoloMesh.Surface(v, c, t, "Sweep"), m_LineMaterial, Vector3.zero);
        }

        // Meridians and parallels of a globe, in the globe's own space (it turns about its tilted axis)
        void BuildGlobe()
        {
            m_Globe = new GameObject("Globe").transform;
            m_Globe.SetParent(m_Body, false);
            m_Globe.localPosition = new Vector3(0f, k_Height, 0f);
            var v = new List<Vector3>();
            var c = new List<Color>();
            for (int k = 0; k < 6; k++) // six great circles through the poles: twelve meridians
            {
                float phi = k * Mathf.PI / 6f;
                HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a) * Mathf.Cos(phi), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(phi)) * k_Radius,
                    Alpha(k_Cyan, 0.5f));
            }
            for (int lat = -60; lat <= 60; lat += 20)
            {
                float rad = lat * Mathf.Deg2Rad, y = Mathf.Sin(rad) * k_Radius, ring = Mathf.Cos(rad) * k_Radius;
                HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a) * ring, y, Mathf.Sin(a) * ring), Alpha(k_Cyan, lat == 0 ? 0.85f : 0.5f));
            }
            m_GlobeLines = Part("Globe Lines", m_Globe, HoloMesh.Lines(v, c, "Globe"), m_LineMaterial, Vector3.zero);
        }

        // The planet's glass: a smooth ball (an icosphere of 162 corners) that is nearly clear face-on and glows
        // at its rim (the shader's rim light), as the globe on the console does
        void BuildGlass()
        {
            HoloMesh.Icosphere(2, out var corners, out var triangles);
            var v = new List<Vector3>();
            var c = new List<Color>();
            foreach (var corner in corners)
            {
                v.Add(corner * k_Radius * 1.02f);
                c.Add(Alpha(k_Cyan, 0.16f));
            }
            var mesh = HoloMesh.Surface(v, c, triangles, "Glass");
            mesh.RecalculateNormals(); // shared corners: smooth, so the rim glows evenly
            m_Glass = Part("Glass", m_Globe, mesh, m_ShellMaterial, Vector3.zero);
        }

        // ---- The golden halo, made of fragments like the film's ----

        static Vector3 OnSphere(float latitude, float longitude, float radius) =>
            new(Mathf.Cos(latitude) * Mathf.Cos(longitude) * radius, Mathf.Sin(latitude) * radius, Mathf.Cos(latitude) * Mathf.Sin(longitude) * radius);

        static Color FragmentColor(System.Random rnd, float alpha)
        {
            double pick = rnd.NextDouble();
            var color = pick < 0.12 ? k_Hot : pick < 0.35 ? k_Amber : k_Gold;
            return Alpha(color, alpha);
        }

        // Circuit-like fragments on a sphere: short runs that walk along the latitude / longitude grid, turning
        // by right angles, each at its own brightness (a few very bright) and, in UV, its own phase for the
        // shader's travelling light. Radii spread a little, so the shell has depth
        static void SphereFragments(List<Vector3> v, List<Color> c, List<Vector2> uvs, System.Random rnd, int count, float radius)
        {
            for (int f = 0; f < count; f++)
            {
                float latitude = Mathf.Asin((float)rnd.NextDouble() * 2f - 1f), longitude = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = radius * (0.94f + 0.12f * (float)rnd.NextDouble());
                float brightness = Mathf.Lerp(0.15f, 1f, Mathf.Pow((float)rnd.NextDouble(), 2f));
                float phase = (float)rnd.NextDouble();
                int steps = 2 + rnd.Next(4), direction = rnd.Next(4);
                var previous = OnSphere(latitude, longitude, r);
                for (int s = 0; s < steps; s++)
                {
                    if (rnd.NextDouble() < 0.4)
                        direction = (direction + (rnd.Next(2) == 0 ? 1 : 3)) % 4; // a right-angle turn
                    float step = Mathf.Lerp(0.02f, 0.08f, (float)rnd.NextDouble());
                    switch (direction)
                    {
                        case 0: longitude += step / Mathf.Max(Mathf.Cos(latitude), 0.25f); break;
                        case 1: latitude = Mathf.Min(latitude + step, 1.35f); break;
                        case 2: longitude -= step / Mathf.Max(Mathf.Cos(latitude), 0.25f); break;
                        default: latitude = Mathf.Max(latitude - step, -1.35f); break;
                    }
                    var next = OnSphere(latitude, longitude, r);
                    float fade = 1f - s * 0.12f;
                    var color = FragmentColor(rnd, brightness * fade);
                    HoloMesh.Segment(v, c, previous, next, color, Alpha(color, color.a * 0.85f));
                    uvs.Add(new Vector2(0f, phase + 0.13f * s));
                    uvs.Add(new Vector2(1f, phase + 0.13f * s));
                    previous = next;
                }
            }
        }

        // Two shells of fragments round the planet, tilted and turning against each other
        void BuildAureola()
        {
            var rnd = new System.Random(21);
            for (int i = 0; i < 2; i++)
            {
                var pivot = new GameObject(i == 0 ? "Halo A" : "Halo B").transform;
                pivot.SetParent(m_Body, false);
                pivot.localPosition = new Vector3(0f, k_Height, 0f);
                var v = new List<Vector3>();
                var c = new List<Color>();
                var uvs = new List<Vector2>();
                SphereFragments(v, c, uvs, rnd, i == 0 ? 430 : 350, k_Radius * (i == 0 ? 1.55f : 1.85f));
                var renderer = Part(pivot.name, pivot, HoloMesh.Lines(v, c, "Halo", uvs), m_LineMaterial, Vector3.zero);
                if (i == 0) { m_ShellPivotA = pivot; m_ShellA = renderer; }
                else { m_ShellPivotB = pivot; m_ShellB = renderer; }
            }
        }

        // Bright arcs sweeping round the planet like comets: a long arc that grows brighter towards its head
        // (three thin arcs side by side, so it has some body), a white-hot spark with a glow at the head, and a
        // few specks along the tail. Each turns about its own tilted axis at its own speed
        void BuildComets()
        {
            // radius (planet radii), span (degrees), tilt, speed (degrees per second)
            var specs = new (float radius, float span, Vector3 tilt, float speed)[]
            {
                (1.30f, 210f, new Vector3(20f, 0f, 35f), 46f),
                (1.45f, 160f, new Vector3(-50f, 0f, 10f), -38f),
                (1.60f, 250f, new Vector3(75f, 0f, -20f), 29f),
                (1.75f, 190f, new Vector3(10f, 0f, -60f), -52f),
                (1.90f, 230f, new Vector3(-30f, 0f, 70f), 24f),
                (2.05f, 140f, new Vector3(60f, 0f, 45f), -33f),
            };
            var rnd = new System.Random(8);
            foreach (var spec in specs)
            {
                var comet = new Comet { Speed = spec.speed };
                comet.Pivot = new GameObject("Comet").transform;
                comet.Pivot.SetParent(m_Body, false);
                comet.Pivot.localPosition = new Vector3(0f, k_Height, 0f);
                comet.Pivot.localRotation = Quaternion.Euler(spec.tilt);
                comet.Spin = new GameObject("Spin").transform;
                comet.Spin.SetParent(comet.Pivot, false);

                float radius = k_Radius * spec.radius;
                var v = new List<Vector3>();
                var c = new List<Color>();
                const int steps = 48;
                foreach (float offset in new[] { -0.0012f, 0f, 0.0012f })
                {
                    var previous = Polar(radius + offset, 0f, 0f);
                    for (int i = 1; i <= steps; i++)
                    {
                        float t = i / (float)steps;
                        var next = Polar(radius + offset, spec.span * t * Mathf.Deg2Rad, 0f);
                        float alpha = Mathf.Pow(t, 2.2f) * (offset == 0f ? 0.95f : 0.55f);
                        var color = Color.Lerp(k_Gold, k_Hot, t);
                        HoloMesh.Segment(v, c, previous, next, Alpha(color, alpha * 0.85f), Alpha(color, alpha));
                        previous = next;
                    }
                }
                comet.Line = Part("Comet Arc", comet.Spin, HoloMesh.Lines(v, c, "Comet arc"), m_LineMaterial, Vector3.zero);

                var head = Polar(radius, spec.span * Mathf.Deg2Rad, 0f);
                var centers = new List<Vector3> { head, head };
                var sizes = new List<float> { 0.0065f, 0.02f };
                var colors = new List<Color> { Alpha(k_White, 1f), Alpha(k_Gold, 0.45f) };
                for (int i = 0; i < 6; i++) // specks along the tail
                {
                    centers.Add(Polar(radius, spec.span * (0.35f + 0.6f * (float)rnd.NextDouble()) * Mathf.Deg2Rad, 0f));
                    sizes.Add(Mathf.Lerp(0.001f, 0.002f, (float)rnd.NextDouble()));
                    colors.Add(Alpha(k_Hot, 0.7f));
                }
                comet.Dots = Part("Comet Head", comet.Spin, HoloMesh.Dots(centers, sizes, colors, "Comet head"), m_DotMaterial, Vector3.zero);
                m_Comets.Add(comet);
            }
        }

        // The floor of the base: circuit-like fragments on a polar grid, runs along a ring or across rings,
        // turning by right angles
        void BuildDebris()
        {
            var rnd = new System.Random(34);
            var v = new List<Vector3>();
            var c = new List<Color>();
            var uvs = new List<Vector2>();
            for (int f = 0; f < 150; f++)
            {
                int ring = rnd.Next(7);
                float angle = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float brightness = Mathf.Lerp(0.15f, 0.9f, Mathf.Pow((float)rnd.NextDouble(), 2f));
                float phase = (float)rnd.NextDouble();
                bool along = rnd.Next(2) == 0;
                int sign = rnd.Next(2) == 0 ? 1 : -1;
                var previous = Polar(k_BaseRadius * (0.36f + ring * 0.085f), angle, 0.003f);
                int steps = 2 + rnd.Next(4);
                for (int s = 0; s < steps; s++)
                {
                    if (rnd.NextDouble() < 0.45)
                        along = !along;
                    if (along)
                        angle += sign * Mathf.Lerp(0.06f, 0.22f, (float)rnd.NextDouble());
                    else
                        ring = Mathf.Clamp(ring + (rnd.Next(2) == 0 ? 1 : -1), 0, 6);
                    var next = Polar(k_BaseRadius * (0.36f + ring * 0.085f), angle, 0.003f);
                    var color = FragmentColor(rnd, brightness * (1f - s * 0.1f));
                    HoloMesh.Segment(v, c, previous, next, color, color);
                    uvs.Add(new Vector2(0f, phase + 0.13f * s));
                    uvs.Add(new Vector2(1f, phase + 0.13f * s));
                    previous = next;
                }
            }
            m_Debris = Part("Base Debris", m_Body, HoloMesh.Lines(v, c, "Base debris", uvs), m_LineMaterial, Vector3.zero);
        }

        // A lens flare across the core: a long horizontal streak, a short upright one and a thin warm one
        void BuildFlare()
        {
            var zero = new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
            var colors = new List<Color> { Alpha(k_Hot, 0.5f), Alpha(k_Gold, 0.3f), Alpha(k_Amber, 0.6f) };
            var stretch = new List<Vector2> { new(0.17f, 0.0035f), new(0.0035f, 0.07f), new(0.05f, 0.0025f) };
            m_Flare = Part("Flare", m_Body, HoloMesh.Dots(zero, null, colors, "Flare", stretch), m_DotMaterial, new Vector3(0f, k_Height, 0f));
        }

        // A thin vertical dial round the figure, like a gauge: a circle with a scale of ticks and an orange arc.
        // It always faces the user (LateUpdate) and turns slowly in its own plane
        void BuildDial()
        {
            m_DialPivot = new GameObject("Dial Pivot").transform;
            m_DialPivot.SetParent(m_Body, false);
            m_DialPivot.localPosition = new Vector3(0f, k_Height, 0f);
            m_Dial = new GameObject("Dial").transform;
            m_Dial.SetParent(m_DialPivot, false);
            float radius = k_Radius * 2.45f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            Vector3 On(float r, float degrees) => new(Mathf.Cos(degrees * Mathf.Deg2Rad) * r, Mathf.Sin(degrees * Mathf.Deg2Rad) * r, 0f);
            for (int i = 0; i < 360; i += 3)
                HoloMesh.Segment(v, c, On(radius, i), On(radius, i + 3), Alpha(k_Gold, 0.4f), Alpha(k_Gold, 0.4f));
            for (int i = 0; i < 120; i++)
            {
                float length = i % 10 == 0 ? 0.034f : i % 5 == 0 ? 0.024f : 0.012f;
                HoloMesh.Segment(v, c, On(radius, i * 3f), On(radius - length, i * 3f), Alpha(k_Gold, 0.5f), Alpha(k_Gold, 0.5f));
            }
            for (int layer = 0; layer < 3; layer++) // a gauge's fill: a thick orange arc
            {
                float r = radius * (0.955f - layer * 0.006f);
                for (int i = -30; i < 95; i += 3)
                    HoloMesh.Segment(v, c, On(r, i), On(r, i + 3), Alpha(k_Amber, 0.9f), Alpha(k_Amber, 0.9f));
            }
            for (int i = 110; i < 310; i += 3)
                HoloMesh.Segment(v, c, On(radius * 0.9f, i), On(radius * 0.9f, i + 3), Alpha(k_Gold, 0.45f), Alpha(k_Gold, 0.45f));
            m_DialRenderer = Part("Dial Lines", m_Dial, HoloMesh.Lines(v, c, "Dial"), m_LineMaterial, Vector3.zero);
        }

        // The planet's glow, as on the console: a wide soft orange light filling the ball, a hotter middle, a
        // white heart, and a small cool highlight off to one side for depth
        void BuildCore()
        {
            var mesh = HoloMesh.Dots(
                new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero, new(-0.026f, 0.03f, 0.02f) },
                new List<float> { 0.09f, 0.052f, 0.022f, 0.035f },
                new List<Color> { Alpha(k_Orange, 0.36f), Alpha(new Color(1f, 0.75f, 0.35f), 0.62f), Alpha(Color.white, 1f), Alpha(k_Cyan, 0.22f) },
                "Core");
            m_CoreRenderer = Part("Core", m_Body, mesh, m_DotMaterial, new Vector3(0f, k_Height, 0f));
            m_CoreGlow = m_CoreRenderer.transform;
        }

        void BuildRings()
        {
            // semi-axes (in globe radii), tilt, speed, colour
            var specs = new (float a, float b, Vector3 tilt, float speed, Color color)[]
            {
                (1.55f, 1.30f, new Vector3(28f, 0f, 18f), 38f, k_Orange),
                (1.40f, 1.40f, new Vector3(72f, 0f, 0f), -26f, k_Cyan),
                (1.85f, 1.15f, new Vector3(-18f, 0f, -28f), 17f, k_Cyan),
            };
            foreach (var spec in specs)
            {
                var ring = new Ring { Speed = spec.speed };
                ring.Pivot = new GameObject("Ring").transform;
                ring.Pivot.SetParent(m_Body, false);
                ring.Pivot.localPosition = new Vector3(0f, k_Height, 0f);
                ring.Pivot.localRotation = Quaternion.Euler(spec.tilt);
                ring.Spin = new GameObject("Spin").transform;
                ring.Spin.SetParent(ring.Pivot, false);

                float a = spec.a * k_Radius, b = spec.b * k_Radius;
                var v = new List<Vector3>();
                var c = new List<Color>();
                HoloMesh.Loop(v, c, 128, t => new Vector3(Mathf.Cos(t) * a, 0f, Mathf.Sin(t) * b), Alpha(spec.color, 0.65f));
                ring.Line = Part("Ring Line", ring.Spin, HoloMesh.Lines(v, c, "Ring"), m_LineMaterial, Vector3.zero);
                bool orange = spec.color == k_Orange;
                var dots = HoloMesh.Dots(new List<Vector3> { new(a, 0f, 0f), new(-a, 0f, 0f) },
                    new List<float> { orange ? 0.0065f : 0.0045f, 0.0035f }, new List<Color> { Alpha(spec.color, 1f), Alpha(spec.color, 0.8f) }, "Ring dots");
                ring.Dots = Part("Ring Dots", ring.Spin, dots, m_DotMaterial, Vector3.zero);
                m_Rings.Add(ring);
            }
        }

        // Rings of light that leave the globe on each spoken word (pooled, three at a time)
        void BuildWaves()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * k_Radius, Alpha(k_Gold, 0.8f));
            HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * k_Radius, Alpha(k_Gold, 0.5f));
            HoloMesh.Loop(v, c, 64, a => new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * k_Radius, Alpha(k_Gold, 0.5f));
            for (int i = 0; i < m_Waves.Length; i++)
            {
                var wave = new Wave();
                wave.Renderer = Part("Wave", m_Body, HoloMesh.Lines(v, c, "Wave"), m_LineMaterial, new Vector3(0f, k_Height, 0f));
                wave.Transform = wave.Renderer.transform;
                wave.Renderer.enabled = false;
                m_Waves[i] = wave;
            }
        }

        // The equalizer: orange ticks standing round the rim, whose heights move with the voice
        void BuildTicks()
        {
            m_TickVertices = new Vector3[k_Ticks * 2];
            var colors = new List<Color>();
            var indices = new int[k_Ticks * 2];
            float radius = k_BaseRadius * 1.08f;
            for (int i = 0; i < k_Ticks; i++)
            {
                float a = i * Mathf.PI * 2f / k_Ticks;
                var p = new Vector3(Mathf.Cos(a) * radius, 0.002f, Mathf.Sin(a) * radius);
                m_TickVertices[2 * i] = p;
                m_TickVertices[2 * i + 1] = p + Vector3.up * 0.004f;
                colors.Add(Alpha(k_Gold, 0.95f));
                colors.Add(Alpha(k_Hot, 0.25f));
                indices[2 * i] = 2 * i;
                indices[2 * i + 1] = 2 * i + 1;
            }
            m_TickMesh = new Mesh { name = "Ticks" };
            m_TickMesh.MarkDynamic();
            m_TickMesh.SetVertices(m_TickVertices);
            m_TickMesh.SetColors(colors);
            m_TickMesh.SetIndices(indices, MeshTopology.Lines, 0);
            m_TickMesh.bounds = new Bounds(new Vector3(0f, 0.04f, 0f), new Vector3(0.4f, 0.1f, 0.4f));
            m_TickRenderer = Part("Ticks", m_Body, m_TickMesh, m_LineMaterial, Vector3.zero);
        }

        // Specks drifting in the air round the globe
        void BuildDust()
        {
            m_DustPivot = new GameObject("Dust").transform;
            m_DustPivot.SetParent(m_Body, false);
            var rnd = new System.Random(5);
            var centers = new List<Vector3>();
            var sizes = new List<float>();
            var colors = new List<Color>();
            for (int i = 0; i < 220; i++)
            {
                Vector3 p;
                do
                {
                    p = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f);
                } while (p.sqrMagnitude > 1f);
                p *= 0.38f;
                p.y = Mathf.Max(0.01f, p.y * 0.8f + k_Height * 0.85f);
                centers.Add(p);
                sizes.Add(Mathf.Lerp(0.0008f, 0.0022f, (float)rnd.NextDouble()));
                bool warm = rnd.NextDouble() < 0.25;
                colors.Add(Alpha(warm ? k_Amber : k_Hot, Mathf.Lerp(0.3f, 0.95f, (float)rnd.NextDouble())));
            }
            m_Dust = Part("Dust Specks", m_DustPivot, HoloMesh.Dots(centers, sizes, colors, "Dust"), m_DotMaterial, Vector3.zero);
        }

        // The name and the buttons below the base, the subtitles above the globe (both turn to face you)
        void BuildPanels()
        {
            m_Controls = WorldUi.Panel("Hologram Controls", transform, new Vector2(330f, 190f), 1.0f, new Color(0f, 0f, 0f, 0f), 20f);
            // Top row: stop, the name, close. Middle: Zapytaj, Na blat. Bottom: Model, Pamięć
            var name = M3.Text("HologramName", m_Controls, DisplayName, M3Theme.Type.TitleLarge, new Vector2(0f, 64f), new Vector2(180f, 40f),
                new Color(0.05f, 0.1f, 0.17f)); // dark: it stands on the base's glow, where light text disappears
            name.characterSpacing = 6f;
            name.fontStyle = FontStyles.Bold;
            M3.IconButton("HologramStop", m_Controls, "stop", M3Button.Variant.Tonal, new Vector2(-122f, 64f), () => m_Session.StopSpeaking(), 56f);
            M3.IconButton("HologramClose", m_Controls, "close", M3Button.Variant.Tonal, new Vector2(122f, 64f), Close, 56f);
            M3.Button("HologramAsk", m_Controls, "Zapytaj", M3Button.Variant.Filled, new Vector2(-80f, 4f), () => m_Session.Listen(), "pinch", 150f);
            M3.Button("HologramDesk", m_Controls, "Na blat", M3Button.Variant.Tonal, new Vector2(80f, 4f), () => PlaceRequested?.Invoke(this), "open_with", 150f);
            M3.Button("HologramModel", m_Controls, "Model", M3Button.Variant.Tonal, new Vector2(-80f, -56f), () => ModelRequested?.Invoke(this), "auto_awesome", 150f);
            M3.Button("HologramMemory", m_Controls, "Pamięć", M3Button.Variant.Tonal, new Vector2(80f, -56f), () => MemoryRequested?.Invoke(this), "edit", 150f);
            WorkspaceSounds.Attach(m_Controls);

            m_Controls.localScale *= 0.7f; // smaller than a window's: it stands beside a 43 cm figure
            m_Hud = HologramHud.Create(transform, m_Session);
            m_Hud.Panel.localScale *= 0.7f;
            m_Subtitles = WorldUi.Panel("Hologram Subtitles", transform, new Vector2(380f, 56f), 1.0f, new Color(0f, 0f, 0f, 0f), 16f);
            m_SubtitleText = M3.Text("HologramSubtitle", m_Subtitles, "", M3Theme.Type.TitleLarge, Vector2.zero, new Vector2(360f, 48f), Color.white);
            m_SubtitleText.fontSize = 20f;
            m_SubtitleText.fontStyle = FontStyles.Bold;
            m_Subtitles.gameObject.SetActive(false);
        }

        // ---- What the assistant says ----

        void OnAnswered(string text)
        {
            m_AnswerText = text ?? "";
            m_Notice = null;
            m_CurrentWord = -1;
            m_ClearAt = float.PositiveInfinity;
            m_Words.Clear();
            int index = 0;
            while (index < m_AnswerText.Length)
            {
                while (index < m_AnswerText.Length && char.IsWhiteSpace(m_AnswerText[index]))
                    index++;
                int start = index;
                while (index < m_AnswerText.Length && !char.IsWhiteSpace(m_AnswerText[index]))
                    index++;
                if (index > start)
                    m_Words.Add(new Word { Start = start, End = index });
            }
        }

        void OnNotice(string text)
        {
            m_Notice = text;
            m_NoticeUntil = Time.unscaledTime + 6f;
        }

        void OnWord(int start, int length)
        {
            int found = -1;
            for (int i = 0; i < m_Words.Count; i++)
            {
                if (m_Words[i].Start <= start)
                    found = i;
                else
                    break;
            }
            if (found >= 0)
                m_CurrentWord = found;
            // A ring of light for the word (not on every one: they would pile up)
            float now = Time.unscaledTime;
            if (now - m_LastWave > 0.16f)
            {
                var wave = m_Waves[m_NextWave++ % m_Waves.Length];
                wave.Born = now;
                wave.Renderer.enabled = true;
                m_LastWave = now;
            }
        }

        string SubtitleText(AssistantState state, float now)
        {
            if (m_Notice != null && now < m_NoticeUntil)
                return "<color=#F2B8B5>" + Clean(m_Notice.Length > 110 ? m_Notice.Substring(0, 110) + "…" : m_Notice) + "</color>";
            switch (state)
            {
                case AssistantState.Thinking:
                    // What it is doing (a tool at work), else just that it thinks
                    return "<color=#5CE1FF>" + (string.IsNullOrEmpty(m_Session.Activity) ? "myślę" : Clean(m_Session.Activity)) +
                           new string('.', 1 + (int)(now * 2.5f) % 3) + "</color>";
                case AssistantState.Listening:
                    return "<color=#9ADFF2>słucham…</color>";
                case AssistantState.Speaking:
                    return WordWindow();
                default:
                    return now < m_ClearAt ? WordWindow() : "";
            }
        }

        static string Clean(string text) => text.Replace('<', '(').Replace('>', ')');

        // A few words round the one being said, that one lit
        string WordWindow()
        {
            if (m_Words.Count == 0)
                return "";
            int current = Mathf.Max(0, m_CurrentWord);
            int from = Mathf.Max(0, current - 1), to = Mathf.Min(m_Words.Count - 1, current + 2);
            var text = new StringBuilder();
            for (int i = from; i <= to; i++)
            {
                string word = Clean(m_AnswerText.Substring(m_Words[i].Start, m_Words[i].End - m_Words[i].Start));
                if (i == current && m_CurrentWord >= 0)
                    text.Append("<color=#33E0FF>").Append(word).Append("</color>");
                else
                    text.Append(word);
                text.Append(' ');
            }
            return text.ToString().TrimEnd();
        }

        // ---- Life ----

        void Update()
        {
            if (m_Session == null || m_Head == null)
                return;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            var state = m_Session.State;
            float level = m_Session.Level;
            if (m_PreviousState == AssistantState.Speaking && state != AssistantState.Speaking)
                m_ClearAt = now + 3f; // the last words stay a moment
            m_PreviousState = state;

            float spin, ringSpin, ringScale, glow;
            Color tint = Color.white;
            switch (state)
            {
                case AssistantState.Listening:
                    spin = 16f; ringSpin = 60f; ringScale = 0.8f; glow = 0.95f;
                    break;
                case AssistantState.Thinking:
                    spin = 70f; ringSpin = 170f; ringScale = 1.08f + 0.06f * Mathf.Sin(now * 9f); glow = 0.9f + 0.15f * Mathf.Sin(now * 23f);
                    break;
                case AssistantState.Speaking:
                    spin = 20f; ringSpin = 70f; ringScale = 1f + 0.08f * level; glow = 0.75f + 0.5f * level;
                    break;
                default:
                    spin = 8f; ringSpin = 22f; ringScale = 1f; glow = 0.6f + 0.05f * Mathf.Sin(now * 1.3f);
                    break;
            }
            float k = 1f - Mathf.Exp(-4f * dt);
            m_Spin = Mathf.Lerp(m_Spin, spin, k);
            m_RingSpin = Mathf.Lerp(m_RingSpin, ringSpin, k);
            m_RingScale = Mathf.Lerp(m_RingScale, ringScale, k);
            m_Glow = Mathf.Lerp(m_Glow, glow, 1f - Mathf.Exp(-9f * dt));
            m_Tint = Color.Lerp(m_Tint, tint, k);
            m_Warm = Mathf.Lerp(m_Warm, state == AssistantState.Thinking ? 0.75f : 0f, k);
            float busy = state == AssistantState.Thinking ? 3f : state == AssistantState.Listening ? 2.2f : 1f;

            // The planet: the globe turns about its tilted axis and breathes with the voice
            m_GlobeAngle += m_Spin * dt;
            m_Globe.localRotation = Quaternion.Euler(0f, 0f, 18f) * Quaternion.Euler(0f, m_GlobeAngle, 0f);
            m_Globe.localScale = Vector3.one * (1f + 0.04f * level);
            Look(m_GlobeLines, m_Glow, m_Tint);
            Look(m_Glass, m_Glow * (1f + 0.8f * level), m_Tint);
            foreach (var ring in m_Rings)
            {
                ring.Angle += ring.Speed * (m_RingSpin / 22f) * dt;
                ring.Spin.localRotation = Quaternion.Euler(0f, ring.Angle, 0f);
                ring.Pivot.localScale = Vector3.one * m_RingScale;
                Look(ring.Line, m_Glow, m_Tint);
                Look(ring.Dots, m_Glow * 1.2f, m_Tint);
            }

            // The core swells with each word and flickers while thinking; the flare crosses it
            float coreScale = 1f + 0.7f * level + 0.05f * Mathf.Sin(now * 2f) + (state == AssistantState.Thinking ? 0.12f * Mathf.Sin(now * 20f) : 0f);
            m_CoreGlow.localScale = Vector3.one * coreScale;
            Look(m_CoreRenderer, 0.7f + 0.8f * level + (state == AssistantState.Listening ? 0.4f : 0f), Color.white, 0f);
            Look(m_Flare, 0.3f + 0.9f * level + (state == AssistantState.Thinking ? 0.25f + 0.2f * Mathf.Sin(now * 17f) : 0f)
                + (state == AssistantState.Listening ? 0.3f : 0f), Color.white, 0f);

            // The halo: two shells of fragments turning against each other, drawing in while it listens and
            // swelling with the voice; light runs along the fragments (slow at rest, quick while thinking)
            m_HaloScale = Mathf.Lerp(m_HaloScale, state == AssistantState.Listening ? 0.88f : 1f + 0.07f * level, k);
            m_ShellPivotA.localRotation = Quaternion.Euler(22f, m_GlobeAngle * 0.5f, 8f);
            m_ShellPivotB.localRotation = Quaternion.Euler(-28f, -m_GlobeAngle * 0.35f, -12f);
            m_ShellPivotA.localScale = Vector3.one * m_HaloScale;
            m_ShellPivotB.localScale = Vector3.one * m_HaloScale;
            float pulse = state == AssistantState.Thinking ? 1f : state == AssistantState.Speaking ? 0.5f + 0.5f * level
                : state == AssistantState.Listening ? 0.6f : 0.35f;
            float pulseSpeed = state == AssistantState.Thinking ? 0.9f : state == AssistantState.Speaking ? 0.3f + 0.5f * level
                : state == AssistantState.Listening ? 0.2f : 0.1f;
            m_PulseClock += dt * pulseSpeed;
            foreach (var renderer in new[] { m_ShellA, m_ShellB, m_Debris })
            {
                Look(renderer, m_Glow * (0.85f + 0.5f * level), m_Tint);
                renderer.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Pulse, pulse);
                m_Block.SetFloat(k_PulseTime, m_PulseClock);
                renderer.SetPropertyBlock(m_Block);
            }

            // The comets sweep round
            foreach (var comet in m_Comets)
            {
                comet.Angle += comet.Speed * busy * dt;
                comet.Spin.localRotation = Quaternion.Euler(0f, comet.Angle, 0f);
                Look(comet.Line, m_Glow * (0.9f + 0.5f * level), m_Tint);
                Look(comet.Dots, m_Glow * (1f + 0.6f * level), m_Tint);
            }

            // The thin gauge ring round it all
            m_Dial.localRotation = Quaternion.Euler(0f, 0f, now * 5f * busy);
            Look(m_DialRenderer, m_Glow * 0.9f, m_Tint);

            // The base: rings and the floor of fragments turn against each other and a radar sweep goes round
            m_BaseAngle += dt * busy;
            m_SweepAngle += dt * 70f * busy;
            m_BaseRingB.transform.localRotation = Quaternion.Euler(0f, m_BaseAngle * 18f, 0f);
            m_BaseRingC.transform.localRotation = Quaternion.Euler(0f, -m_BaseAngle * 30f, 0f);
            m_Debris.transform.localRotation = Quaternion.Euler(0f, -m_BaseAngle * 9f, 0f);
            m_Sweep.transform.localRotation = Quaternion.Euler(0f, m_SweepAngle, 0f);
            Look(m_BaseRingA, m_Glow, m_Tint);
            Look(m_BaseRingB, m_Glow, m_Tint);
            Look(m_BaseRingC, m_Glow, m_Tint);
            Look(m_Sweep, m_Glow * (state == AssistantState.Idle ? 0.7f : 1.4f), m_Tint);
            Look(m_Disc, m_Glow * (0.9f + 0.5f * level), m_Tint);
            Look(m_Beam, m_Glow * (0.9f + 0.6f * level), m_Tint);
            Look(m_Foot, m_Glow * (0.8f + 1f * level), m_Tint);

            // Golden sparks drifting in the halo, drawn in while it thinks
            m_DustScale = Mathf.Lerp(m_DustScale, state == AssistantState.Thinking ? 0.8f + 0.2f * Mathf.Sin(now * 3f) : 1f, k);
            m_DustPivot.localScale = Vector3.one * m_DustScale;
            m_DustPivot.localRotation = Quaternion.Euler(0f, now * 3f, 0f);
            Look(m_Dust, 0.9f, m_Tint);

            // The equalizer
            float lift = 0.006f + 0.05f * level + (state == AssistantState.Listening ? 0.012f : 0f);
            for (int i = 0; i < k_Ticks; i++)
            {
                float n = Mathf.PerlinNoise(i * 0.31f, now * (1.5f + level * 5f));
                float wave = state == AssistantState.Thinking ? 0.01f * (0.5f + 0.5f * Mathf.Sin(now * 6f - i * 0.5f)) : 0f;
                var top = m_TickVertices[2 * i + 1];
                top.y = 0.002f + 0.004f + n * (lift + wave);
                m_TickVertices[2 * i + 1] = top;
            }
            m_TickMesh.SetVertices(m_TickVertices);
            Look(m_TickRenderer, 0.9f + 0.6f * level, Color.white, 0f);

            // Rings of light leaving the planet
            foreach (var wave in m_Waves)
            {
                if (!wave.Renderer.enabled)
                    continue;
                float u = (now - wave.Born) / 0.9f;
                if (u >= 1f)
                {
                    wave.Renderer.enabled = false;
                    continue;
                }
                wave.Transform.localScale = Vector3.one * Mathf.Lerp(1.05f, 2.6f, u);
                Look(wave.Renderer, (1f - u) * (1f - u) * 1.3f, m_Tint);
            }

            // Subtitles, rebuilt only when what they say changes
            string text = SubtitleText(state, now);
            if (text != m_SubtitleKey)
            {
                m_SubtitleKey = text;
                m_SubtitleText.text = text;
                m_Subtitles.gameObject.SetActive(text.Length > 0);
            }
        }

        // The panels stand where the hologram does but always face the user
        void LateUpdate()
        {
            if (m_Head == null)
                return;
            var toHead = m_Head.position - transform.position;
            toHead.y = 0f;
            toHead = toHead.sqrMagnitude > 1e-4f ? toHead.normalized : Vector3.back;
            // The buttons stand in front of the base (not on it), the subtitles above the globe
            Face(m_Controls, transform.position + toHead * (k_BaseRadius * k_Scale + 0.17f) + Vector3.up * 0.1f);
            // (above the dial: 2.45 globe radii round the globe's centre)
            Face(m_Subtitles, transform.position + Vector3.up * ((k_Height + k_Radius * 2.45f) * k_Scale + 0.06f));
            // The status panel to the user's left of the figure, at the globe's height
            var left = Vector3.Cross(Vector3.up, toHead);
            Face(m_Hud.Panel, transform.position + left * 0.4f + toHead * 0.04f + Vector3.up * 0.27f);
            // The dial's plane turns to face the user
            m_DialPivot.rotation = Quaternion.LookRotation(toHead, Vector3.up);
        }

        void Face(RectTransform panel, Vector3 position)
        {
            panel.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_Head.position, Vector3.up));
        }

        void OnDestroy()
        {
            if (m_Session != null)
            {
                m_Session.Answered -= OnAnswered;
                m_Session.Notice -= OnNotice;
                m_Session.WordSpoken -= OnWord;
            }
            foreach (var mesh in m_Meshes)
            {
                if (mesh != null)
                    Destroy(mesh);
            }
            if (m_LineMaterial != null)
                Destroy(m_LineMaterial);
            if (m_DotMaterial != null)
                Destroy(m_DotMaterial);
            if (m_ShellMaterial != null)
                Destroy(m_ShellMaterial);
        }
    }

    // Meshes for the hologram: line lists with a colour (and brightness in alpha) per vertex, triangles
    // with a colour per vertex, and soft dots (four vertices on one centre, see Hologram.shader)
    static class HoloMesh
    {
        // uvs: optional, per vertex (the shader's travelling light reads UV0)
        public static Mesh Lines(List<Vector3> vertices, List<Color> colors, string name, List<Vector2> uvs = null)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            if (uvs != null)
                mesh.SetUVs(0, uvs);
            var indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Surface(List<Vector3> vertices, List<Color> colors, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void Segment(List<Vector3> vertices, List<Color> colors, Vector3 a, Vector3 b, Color colorA, Color colorB)
        {
            vertices.Add(a);
            vertices.Add(b);
            colors.Add(colorA);
            colors.Add(colorB);
        }

        // A closed curve, angle 0 to 2 pi, as line segments
        public static void Loop(List<Vector3> vertices, List<Color> colors, int segments, Func<float, Vector3> point, Color color)
        {
            var previous = point(0f);
            for (int i = 1; i <= segments; i++)
            {
                var next = point(i * Mathf.PI * 2f / segments);
                Segment(vertices, colors, previous, next, color, color);
                previous = next;
            }
        }

        // A unit icosphere: 12 corners, 20 faces, each subdivision splitting a face in four (1: 42 corners, 80 faces)
        public static void Icosphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var corners = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < corners.Count; i++)
                corners[i] = corners[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var split = new List<int>();
                var middles = new Dictionary<long, int>();
                int Middle(int a, int b)
                {
                    long key = a < b ? ((long)a << 20) + b : ((long)b << 20) + a;
                    if (!middles.TryGetValue(key, out int index))
                    {
                        index = corners.Count;
                        corners.Add(((corners[a] + corners[b]) / 2f).normalized);
                        middles[key] = index;
                    }
                    return index;
                }
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                    split.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = split;
            }
            vertices = corners;
            triangles = faces;
        }

        // Soft dots: radius (m) per dot, or, with stretch, its radius across and up (a streak); the shader turns
        // the corners to face the camera
        public static Mesh Dots(List<Vector3> centers, List<float> radii, List<Color> colors, string name, List<Vector2> stretch = null)
        {
            var vertices = new List<Vector3>(centers.Count * 4);
            var corners = new List<Vector2>(centers.Count * 4);
            var sizes = new List<Vector2>(centers.Count * 4);
            var vertexColors = new List<Color>(centers.Count * 4);
            var triangles = new List<int>(centers.Count * 6);
            var corner = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            for (int i = 0; i < centers.Count; i++)
            {
                int first = vertices.Count;
                for (int c = 0; c < 4; c++)
                {
                    vertices.Add(centers[i]);
                    corners.Add(corner[c]);
                    sizes.Add(stretch != null ? stretch[i] : new Vector2(radii[i], radii[i]));
                    vertexColors.Add(colors[i]);
                }
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(vertexColors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // The dots grow past their centres: room for the biggest (the base glow)
            var bounds = mesh.bounds;
            bounds.Expand(0.15f);
            mesh.bounds = bounds;
            return mesh;
        }
    }
}
