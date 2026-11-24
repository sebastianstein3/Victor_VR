using System;
using System.Collections.Generic;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // A board game on the real table (Więcej > Gry): the board stands on the detected surface being
    // looked at (as the 3D models do), facing the user, built from simple shapes. Play by looking at a
    // piece or field and pinching (the gaze hits their colliders; WorkspaceDemo.TryBeginPinch hands the
    // pinch to the game). The computer plays the other side(s). A small panel beside the board shows
    // whose turn it is, and has "Nowa gra", the game's own action (the dice) and "Na stół" (move the
    // board to where the user looks). Closing the panel removes the game.
    public abstract class TableGame : MonoBehaviour
    {
        public event Action<string> StatusChanged;
        public event Action<TableGame> Closed;
        public event Action<TableGame> Moving; // about to be moved by code: its room anchor lets go

        public abstract string Title { get; }
        public virtual string ActionLabel => null; // a second button (the dice), or none
        public virtual void Action() { }

        protected Transform m_Head;
        readonly Dictionary<Color, Material> m_Materials = new();
        Shader m_Shader;
        string m_Status = "";

        public string Status => m_Status;

        public void Initialize(Transform head)
        {
            m_Head = head;
            m_Shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            Build();
            NewGame();
        }

        // On the table where the user looks, turned so the user's side is nearest
        public void PlaceAtGaze()
        {
            Moving?.Invoke(this);
            var point = ModelViewer.PlacementPoint(m_Head);
            transform.position = point;
            var away = Vector3.ProjectOnPlane(point - m_Head.position, Vector3.up);
            if (away.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up); // +z away from the user
        }

        protected abstract void Build();
        public abstract void NewGame();

        // A pinch with the gaze on one of this game's colliders
        public abstract void Pick(GameCell cell);

        protected void SetStatus(string status)
        {
            m_Status = status;
            StatusChanged?.Invoke(status);
        }

        public void Close()
        {
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        // ---- Building blocks ----

        protected Material MaterialFor(Color color)
        {
            if (!m_Materials.TryGetValue(color, out var material))
            {
                material = new Material(m_Shader) { name = "Game " + ColorUtility.ToHtmlStringRGB(color) };
                material.SetColor("_BaseColor", color);
                material.color = color;
                m_Materials[color] = material;
            }
            return material;
        }

        // A primitive under parent at a local position and size, coloured; with a GameCell when index >= 0
        protected GameObject Shape(PrimitiveType type, Transform parent, Vector3 position, Vector3 size, Color color,
            int index = -1, bool collider = true)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position;
            shape.transform.localScale = size;
            var renderer = shape.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!collider)
                Destroy(shape.GetComponent<Collider>());
            if (index >= 0)
                shape.AddComponent<GameCell>().Set(this, index);
            return shape;
        }

        protected void SetColor(GameObject shape, Color color)
        {
            if (shape != null)
                shape.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
        }

        protected virtual void OnDestroy()
        {
            foreach (var material in m_Materials.Values)
                Destroy(material);
        }
    }

    // On a field or piece: which game it belongs to and its index there (a square, a pawn)
    public class GameCell : MonoBehaviour
    {
        public TableGame Game { get; private set; }
        public int Index { get; set; }

        public void Set(TableGame game, int index)
        {
            Game = game;
            Index = index;
        }
    }

    // The panel beside a board
    public class GameControls : WidgetPanel
    {
        public override string Kind => "gra";
        TextMeshProUGUI m_Status;
        M3Button m_Action;
        TableGame m_Game;

        public void Attach(TableGame game)
        {
            m_Game = game;
            game.StatusChanged += text => m_Status.text = "<noparse>" + text + "</noparse>";
            m_Status.text = "<noparse>" + game.Status + "</noparse>";
            if (game.ActionLabel != null)
                m_Action.Text = game.ActionLabel;
            m_Action.gameObject.SetActive(game.ActionLabel != null);
            Closed += _ => { if (m_Game != null) m_Game.Close(); };
        }

        protected override void Build(Vector2 size)
        {
            m_Status = M3.Text("GameStatus", m_Canvas, "", M3Theme.Type.BodyLarge, new Vector2(0f, 22f), new Vector2(size.x - 40f, 60f),
                M3Theme.OnSurface);
            M3.Button("NewGame", m_Canvas, "Nowa gra", M3Button.Variant.Tonal, new Vector2(-122f, -58f), () => m_Game?.NewGame(),
                "replay", 116f);
            m_Action = M3.Button("GameAction", m_Canvas, "", M3Button.Variant.Filled, new Vector2(0f, -58f), () => m_Game?.Action(),
                null, 116f);
            M3.Button("GameMove", m_Canvas, "Na stół", M3Button.Variant.Tonal, new Vector2(122f, -58f), () => m_Game?.PlaceAtGaze(),
                "open_with", 116f);
        }

        // The game's own action (the dice) by voice
        public void DoAction() => m_Game?.Action();
    }
}
