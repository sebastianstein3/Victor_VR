using System;
using System.Collections.Generic;
using System.Text;
using CardboardXR.Demos;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // The assistant as a hologram, like Jarvis's: a glowing wire globe holding a web of filaments (a
    // "virtual brain") around a warm core, three tilted rings with dots circling it, a projector base with
    // concentric rings, an equalizer of orange ticks round its rim, a beam of light up to the globe, and
    // drifting dust. It stands on the table you look at (or floats), can be picked up and turned like a
    // model (Carryable), and shows what the assistant is doing:
    //   idle       slow, calm, cyan
    //   listening  the rings draw in, brighter
    //   thinking   everything spins faster and glows warm, the core flickers
    //   speaking   the core and ticks pulse with every word the phone reads, rings of light go out from the
    //              globe, and the words appear as subtitles with the current one lit
    // Everything is built from code: lines, soft dots and a few triangles, drawn additively by the shader
    // "CardboardXR/Hologram". Below it, the name and three buttons: Zapytaj (ask by voice), stop, close.
    public class HologramAssistant : MonoBehaviour
    {
        public const string DisplayName = "JARVIS";

        const float k_BaseRadius = 0.14f;  // m, the projector base
        const float k_Height = 0.27f;      // m, the globe's centre above the base
        const float k_Radius = 0.085f;     // m, the globe
        const int k_Ticks = 72;
        static readonly Color k_Cyan = new(0.25f, 0.85f, 1f);
        static readonly Color k_Orange = new(1f, 0.55f, 0.15f);
        static readonly Color k_White = new(0.92f, 0.98f, 1f);
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Tint = Shader.PropertyToID("_Tint");

        public Carryable Carry { get; private set; }
        public event Action<HologramAssistant> Closed;
        public event Action<HologramAssistant> PlaceRequested; // "Na blat": stand on the surface looked at

        class Ring
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
        Material m_LineMaterial, m_DotMaterial;
        MaterialPropertyBlock m_Block;
        readonly List<Mesh> m_Meshes = new();

        Transform m_Globe, m_CoreGlow, m_DustPivot;
        Renderer m_GlobeLines, m_Network, m_Nodes, m_CoreRenderer, m_Beam, m_Disc, m_BaseRings, m_TickRenderer, m_Dust, m_Foot;
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

            BuildBase();
            BuildGlobe();
            BuildNetwork();
            BuildCore();
            BuildRings();
            BuildWaves();
            BuildTicks();
            BuildDust();
            BuildPanels();

            var box = gameObject.AddComponent<BoxCollider>(); // what the gaze hits, to pick it up
            box.center = new Vector3(0f, (k_Height + k_Radius) / 2f, 0f);
            box.size = new Vector3(0.3f, k_Height + k_Radius + 0.02f, 0.3f);
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

        void Look(Renderer renderer, float intensity, Color tint)
        {
            if (renderer == null)
                return;
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetFloat(k_Intensity, intensity);
            m_Block.SetColor(k_Tint, tint);
            renderer.SetPropertyBlock(m_Block);
        }

        static Color Alpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        // The base: four rings and an orange dial, a faint disc, a beam up to the globe, a glow at its foot
        void BuildBase()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            float[] radii = { 1f, 0.78f, 0.55f, 0.3f };
            float[] alphas = { 0.6f, 0.42f, 0.36f, 0.3f };
            for (int i = 0; i < radii.Length; i++)
            {
                float radius = k_BaseRadius * radii[i];
                HoloMesh.Loop(v, c, 96, a => new Vector3(Mathf.Cos(a) * radius, 0.002f, Mathf.Sin(a) * radius), Alpha(k_Cyan, alphas[i]));
            }
            float dial = k_BaseRadius * 1.08f;
            HoloMesh.Loop(v, c, 96, a => new Vector3(Mathf.Cos(a) * dial, 0.002f, Mathf.Sin(a) * dial), Alpha(k_Orange, 0.5f));
            m_BaseRings = Part("Base Rings", transform, HoloMesh.Lines(v, c, "Base rings"), m_LineMaterial, Vector3.zero);

            // The disc: bright in the middle, gone at the rim
            var dv = new List<Vector3> { new(0f, 0.001f, 0f) };
            var dc = new List<Color> { Alpha(k_Cyan, 0.22f) };
            var dt = new List<int>();
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                dv.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * k_BaseRadius + new Vector3(0f, 0.001f, 0f));
                dc.Add(Alpha(k_Cyan, 0.03f));
            }
            for (int i = 0; i < segments; i++)
            {
                dt.Add(0);
                dt.Add(1 + i);
                dt.Add(1 + (i + 1) % segments);
            }
            m_Disc = Part("Base Disc", transform, HoloMesh.Surface(dv, dc, dt, "Base disc"), m_LineMaterial, Vector3.zero);

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
                bc.Add(Alpha(k_Cyan, 0.26f));
                bv.Add(new Vector3(Mathf.Cos(a) * 0.03f, top, Mathf.Sin(a) * 0.03f));
                bc.Add(Alpha(k_Cyan, 0f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = (i + 1) % sides * 2;
                bt.Add(a); bt.Add(a + 1); bt.Add(b + 1);
                bt.Add(a); bt.Add(b + 1); bt.Add(b);
            }
            m_Beam = Part("Beam", transform, HoloMesh.Surface(bv, bc, bt, "Beam"), m_LineMaterial, Vector3.zero);

            var foot = HoloMesh.Dots(new List<Vector3> { new(0f, 0.01f, 0f) }, new List<float> { 0.075f },
                new List<Color> { Alpha(k_Cyan, 0.4f) }, "Foot glow");
            m_Foot = Part("Foot Glow", transform, foot, m_DotMaterial, Vector3.zero);
        }

        // Meridians and parallels of a globe, in the globe's own space (it turns about its tilted axis)
        void BuildGlobe()
        {
            m_Globe = new GameObject("Globe").transform;
            m_Globe.SetParent(transform, false);
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

        // The "brain": 230 points in two folded hemispheres inside the globe, each joined to its three
        // nearest, as glowing filaments with a node at every point
        void BuildNetwork()
        {
            var rnd = new System.Random(11);
            var points = new List<Vector3>();
            for (int i = 0; i < 230; i++)
            {
                float u = (float)rnd.NextDouble() * 2f - 1f, theta = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float s = Mathf.Sqrt(1f - u * u);
                var d = new Vector3(s * Mathf.Cos(theta), u, s * Mathf.Sin(theta));
                float rho = k_Radius * Mathf.Lerp(0.5f, 0.96f, Mathf.Sqrt((float)rnd.NextDouble()));
                var p = new Vector3(d.x, d.y * 0.85f, d.z * 1.08f) * rho;
                // The fissure between the hemispheres, and folds
                p.x += Mathf.Sign(p.x) * k_Radius * 0.07f * Mathf.Exp(-Mathf.Pow(p.x / (k_Radius * 0.25f), 2f));
                p *= 1f + 0.07f * Mathf.Sin(d.y * 9f + d.x * 7f) * Mathf.Cos(d.z * 8f);
                points.Add(p);
            }
            var v = new List<Vector3>();
            var c = new List<Color>();
            var seen = new HashSet<long>();
            var near = new List<(float distance, int index)>();
            for (int i = 0; i < points.Count; i++)
            {
                near.Clear();
                for (int j = 0; j < points.Count; j++)
                {
                    if (j != i)
                        near.Add(((points[i] - points[j]).sqrMagnitude, j));
                }
                near.Sort((a, b) => a.distance.CompareTo(b.distance));
                for (int n = 0; n < 3; n++)
                {
                    int j = near[n].index;
                    long key = i < j ? (long)i * 1000 + j : (long)j * 1000 + i;
                    if (!seen.Add(key))
                        continue;
                    var color = rnd.NextDouble() < 0.14 ? k_Orange : k_Cyan;
                    float alpha = Mathf.Lerp(0.25f, 0.6f, (float)rnd.NextDouble());
                    HoloMesh.Segment(v, c, points[i], points[j], Alpha(color, alpha), Alpha(color, alpha * 0.3f));
                }
            }
            m_Network = Part("Filaments", m_Globe, HoloMesh.Lines(v, c, "Filaments"), m_LineMaterial, Vector3.zero);

            var sizes = new List<float>();
            var colors = new List<Color>();
            foreach (var _ in points)
            {
                bool warm = rnd.NextDouble() < 0.12;
                sizes.Add(Mathf.Lerp(0.0013f, 0.0032f, (float)rnd.NextDouble()));
                colors.Add(Alpha(warm ? k_Orange : k_White, warm ? 0.9f : 0.7f));
            }
            m_Nodes = Part("Nodes", m_Globe, HoloMesh.Dots(points, sizes, colors, "Nodes"), m_DotMaterial, Vector3.zero);
        }

        // The glowing core: a wide warm halo, a hot middle and a white point
        void BuildCore()
        {
            var mesh = HoloMesh.Dots(
                new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero },
                new List<float> { 0.055f, 0.028f, 0.011f },
                new List<Color> { Alpha(k_Orange, 0.42f), Alpha(new Color(1f, 0.75f, 0.35f), 0.8f), Alpha(Color.white, 1f) }, "Core");
            m_CoreRenderer = Part("Core", transform, mesh, m_DotMaterial, new Vector3(0f, k_Height, 0f));
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
                ring.Pivot.SetParent(transform, false);
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
            HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * k_Radius, Alpha(k_Cyan, 0.8f));
            HoloMesh.Loop(v, c, 64, a => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * k_Radius, Alpha(k_Cyan, 0.5f));
            HoloMesh.Loop(v, c, 64, a => new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * k_Radius, Alpha(k_Cyan, 0.5f));
            for (int i = 0; i < m_Waves.Length; i++)
            {
                var wave = new Wave();
                wave.Renderer = Part("Wave", transform, HoloMesh.Lines(v, c, "Wave"), m_LineMaterial, new Vector3(0f, k_Height, 0f));
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
                colors.Add(Alpha(k_Orange, 0.95f));
                colors.Add(Alpha(new Color(1f, 0.75f, 0.35f), 0.25f));
                indices[2 * i] = 2 * i;
                indices[2 * i + 1] = 2 * i + 1;
            }
            m_TickMesh = new Mesh { name = "Ticks" };
            m_TickMesh.MarkDynamic();
            m_TickMesh.SetVertices(m_TickVertices);
            m_TickMesh.SetColors(colors);
            m_TickMesh.SetIndices(indices, MeshTopology.Lines, 0);
            m_TickMesh.bounds = new Bounds(new Vector3(0f, 0.04f, 0f), new Vector3(0.4f, 0.1f, 0.4f));
            m_TickRenderer = Part("Ticks", transform, m_TickMesh, m_LineMaterial, Vector3.zero);
        }

        // Specks drifting in the air round the globe
        void BuildDust()
        {
            m_DustPivot = new GameObject("Dust").transform;
            m_DustPivot.SetParent(transform, false);
            var rnd = new System.Random(5);
            var centers = new List<Vector3>();
            var sizes = new List<float>();
            var colors = new List<Color>();
            for (int i = 0; i < 120; i++)
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
                colors.Add(Alpha(warm ? k_Orange : k_White, Mathf.Lerp(0.25f, 0.8f, (float)rnd.NextDouble())));
            }
            m_Dust = Part("Dust Specks", m_DustPivot, HoloMesh.Dots(centers, sizes, colors, "Dust"), m_DotMaterial, Vector3.zero);
        }

        // The name and the buttons below the base, the subtitles above the globe (both turn to face you)
        void BuildPanels()
        {
            m_Controls = WorldUi.Panel("Hologram Controls", transform, new Vector2(330f, 130f), 1.0f, new Color(0f, 0f, 0f, 0f), 20f);
            // Top row: stop, the name, close. Bottom row: Zapytaj, Na blat
            var name = M3.Text("HologramName", m_Controls, DisplayName, M3Theme.Type.TitleLarge, new Vector2(0f, 30f), new Vector2(170f, 40f),
                new Color(0.7f, 0.95f, 1f));
            name.characterSpacing = 14f;
            M3.IconButton("HologramStop", m_Controls, "stop", M3Button.Variant.Tonal, new Vector2(-122f, 30f), () => m_Session.StopSpeaking(), 56f);
            M3.IconButton("HologramClose", m_Controls, "close", M3Button.Variant.Tonal, new Vector2(122f, 30f), Close, 56f);
            M3.Button("HologramAsk", m_Controls, "Zapytaj", M3Button.Variant.Filled, new Vector2(-80f, -30f), () => m_Session.Listen(), "pinch", 150f);
            M3.Button("HologramDesk", m_Controls, "Na blat", M3Button.Variant.Tonal, new Vector2(80f, -30f), () => PlaceRequested?.Invoke(this), "open_with", 150f);
            WorkspaceSounds.Attach(m_Controls);

            m_Subtitles = WorldUi.Panel("Hologram Subtitles", transform, new Vector2(460f, 70f), 1.0f, new Color(0f, 0f, 0f, 0f), 16f);
            m_SubtitleText = M3.Text("HologramSubtitle", m_Subtitles, "", M3Theme.Type.TitleLarge, Vector2.zero, new Vector2(440f, 60f), Color.white);
            m_SubtitleText.fontSize = 26f;
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
                    return "<color=#5CE1FF>myślę" + new string('.', 1 + (int)(now * 2.5f) % 3) + "</color>";
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
            int from = Mathf.Max(0, current - 2), to = Mathf.Min(m_Words.Count - 1, current + 3);
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
            Color tint;
            switch (state)
            {
                case AssistantState.Listening:
                    spin = 16f; ringSpin = 60f; ringScale = 0.8f; glow = 0.95f; tint = new Color(0.85f, 1f, 1f);
                    break;
                case AssistantState.Thinking:
                    spin = 70f; ringSpin = 170f; ringScale = 1.08f + 0.06f * Mathf.Sin(now * 9f); glow = 0.9f + 0.15f * Mathf.Sin(now * 23f);
                    tint = new Color(1f, 0.78f, 0.5f);
                    break;
                case AssistantState.Speaking:
                    spin = 20f; ringSpin = 70f; ringScale = 1f + 0.08f * level; glow = 0.75f + 0.5f * level; tint = Color.white;
                    break;
                default:
                    spin = 8f; ringSpin = 22f; ringScale = 1f; glow = 0.6f + 0.05f * Mathf.Sin(now * 1.3f); tint = Color.white;
                    break;
            }
            float k = 1f - Mathf.Exp(-4f * dt);
            m_Spin = Mathf.Lerp(m_Spin, spin, k);
            m_RingSpin = Mathf.Lerp(m_RingSpin, ringSpin, k);
            m_RingScale = Mathf.Lerp(m_RingScale, ringScale, k);
            m_Glow = Mathf.Lerp(m_Glow, glow, 1f - Mathf.Exp(-9f * dt));
            m_Tint = Color.Lerp(m_Tint, tint, k);

            // The globe and its filaments turn, tilted; the brain breathes with the voice
            m_GlobeAngle += m_Spin * dt;
            m_Globe.localRotation = Quaternion.Euler(0f, 0f, 18f) * Quaternion.Euler(0f, m_GlobeAngle, 0f);
            m_Globe.localScale = Vector3.one * (1f + 0.04f * level);
            foreach (var ring in m_Rings)
            {
                ring.Angle += ring.Speed * (m_RingSpin / 22f) * dt;
                ring.Spin.localRotation = Quaternion.Euler(0f, ring.Angle, 0f);
                ring.Pivot.localScale = Vector3.one * m_RingScale;
                Look(ring.Line, m_Glow, m_Tint);
                Look(ring.Dots, m_Glow * 1.2f, m_Tint);
            }
            Look(m_GlobeLines, m_Glow, m_Tint);
            Look(m_Network, m_Glow * (0.8f + 0.6f * level), m_Tint);
            Look(m_Nodes, m_Glow * (0.9f + 0.7f * level), m_Tint);
            Look(m_BaseRings, m_Glow, m_Tint);
            Look(m_Disc, m_Glow * (0.9f + 0.5f * level), m_Tint);
            Look(m_Beam, m_Glow * (0.9f + 0.6f * level), m_Tint);
            Look(m_Foot, m_Glow * (0.8f + 1f * level), m_Tint);
            Look(m_Dust, 0.8f, m_Tint);
            m_DustPivot.localRotation = Quaternion.Euler(0f, now * 3f, 0f);

            // The core: swells with each word, flickers while thinking
            float coreScale = 1f + 0.7f * level + 0.05f * Mathf.Sin(now * 2f) + (state == AssistantState.Thinking ? 0.12f * Mathf.Sin(now * 20f) : 0f);
            m_CoreGlow.localScale = Vector3.one * coreScale;
            Look(m_CoreRenderer, 0.7f + 0.8f * level + (state == AssistantState.Listening ? 0.4f : 0f), Color.white);

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
            Look(m_TickRenderer, 0.9f + 0.6f * level, Color.white);

            // Rings of light leaving the globe
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
            Face(m_Controls, transform.position + toHead * (k_BaseRadius + 0.07f) + Vector3.up * 0.09f);
            Face(m_Subtitles, transform.position + Vector3.up * (k_Height + k_Radius + 0.14f));
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
        }
    }

    // Meshes for the hologram: line lists with a colour (and brightness in alpha) per vertex, triangles
    // with a colour per vertex, and soft dots (four vertices on one centre, see Hologram.shader)
    static class HoloMesh
    {
        public static Mesh Lines(List<Vector3> vertices, List<Color> colors, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
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

        // Soft dots: radius (m) per dot; the shader turns the corners to face the camera
        public static Mesh Dots(List<Vector3> centers, List<float> radii, List<Color> colors, string name)
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
                    sizes.Add(new Vector2(radii[i], 0f));
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
