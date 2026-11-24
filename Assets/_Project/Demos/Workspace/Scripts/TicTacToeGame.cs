using System.Collections;
using System.Collections.Generic;
using CardboardXR.Hands;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Kółko i krzyżyk, drawn with the room-drawing ink (SpaceDrawing): the grid, your marks, the computer's
    // marks and the winning line are all ink strokes. You play the crosses: pinch in the air over a free
    // field and draw (any scribble, a cross is best); when the pinch opens, the stroke becomes a neat cross
    // in the field it was drawn over (its centre decides), and the computer answers by drawing a circle.
    // Without 6DoF hand tracking (the 3DoF pinch sensor has no hand position), look at a field and pinch.
    // Levels (the "Poziom" button): easy (the computer sometimes misses its chance), hard (never loses).
    // Games alternate who starts.
    public class TicTacToeGame : TableGame
    {
        const float k_Cell = 0.07f;
        const float k_Top = 0.011f;
        const float k_InkWidth = 0.006f;
        static readonly Color k_Cross = new(0.3f, 0.65f, 1f);
        static readonly Color k_Circle = new(1f, 0.5f, 0.25f);
        static readonly Color k_Grid = new(0.92f, 0.92f, 0.95f);
        static readonly Color k_Win = new(1f, 0.85f, 0.2f);
        static readonly int[][] k_Lines =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
            new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
            new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
        };

        public override string Title => "Kółko i krzyżyk";
        public override string ActionLabel => "Poziom";

        SpaceDrawing m_Ink;
        readonly int[] m_Board = new int[9]; // 0 free, 1 cross (you), -1 circle (computer)
        readonly List<PaintStroke> m_Marks = new();
        bool m_Busy;
        bool m_Over;
        bool m_Hard;
        bool m_ComputerStarts = true; // flipped by the first NewGame: you start
        bool m_Stroking;
        readonly System.Random m_Random = new();

        protected override void Build()
        {
            Shape(PrimitiveType.Cube, transform, new Vector3(0f, k_Top / 2f - 0.002f, 0f),
                new Vector3(3f * k_Cell + 0.035f, k_Top - 0.004f, 3f * k_Cell + 0.035f), new Color(0.13f, 0.14f, 0.2f), -1, false);
            // The fields: flat blocks the gaze can hit (the fallback for a pinch without a hand position)
            for (int i = 0; i < 9; i++)
            {
                Shape(PrimitiveType.Cube, transform, FieldCenter(i) - Vector3.up * 0.001f, new Vector3(k_Cell * 0.94f, 0.004f, k_Cell * 0.94f),
                    new Color(0.19f, 0.2f, 0.28f), i, true);
            }
            var inkObject = new GameObject("Ink");
            inkObject.transform.SetParent(transform, false);
            m_Ink = inkObject.AddComponent<SpaceDrawing>();
            m_Ink.Initialize(Core);
            m_Ink.StrokeEnded += OnStrokeEnded;
        }

        // Local position of a field's centre (0..8, row 0 nearest the user, x to the right)
        Vector3 FieldCenter(int field) => new((field % 3 - 1) * k_Cell, k_Top, (field / 3 - 1) * k_Cell);

        public override void NewGame()
        {
            StopAllCoroutines();
            foreach (var mark in m_Marks)
            {
                if (mark != null)
                    m_Ink.Remove(mark);
            }
            m_Marks.Clear();
            for (int i = 0; i < 9; i++)
                m_Board[i] = 0;
            m_Busy = false;
            m_Over = false;
            m_Stroking = false;
            DrawGrid();
            m_ComputerStarts = !m_ComputerStarts; // alternate: the first game is yours
            if (m_ComputerStarts)
                StartCoroutine(ComputerMove());
            else
                SetStatus($"Twój ruch (krzyżyk). Uszczypnij nad polem i narysuj krzyżyk. Poziom: {Level}.");
        }

        string Level => m_Hard ? "trudny" : "łatwy";

        public override void Action()
        {
            m_Hard = !m_Hard;
            SetStatus($"Poziom: {Level}" + (m_Hard ? " – komputer nie przegrywa." : " – komputer czasem się myli."));
        }

        void DrawGrid()
        {
            // Four lines, the same ink as the marks (drawn once; NewGame redraws them only the first time)
            if (m_Gridded)
                return;
            m_Gridded = true;
            float edge = 1.45f * k_Cell;
            for (int i = 0; i < 2; i++)
            {
                float offset = (i - 0.5f) * k_Cell;
                Trace(new[] { new Vector3(offset, k_Top, -edge), new Vector3(offset, k_Top, edge) }, k_Grid, k_InkWidth);
                Trace(new[] { new Vector3(-edge, k_Top, offset), new Vector3(edge, k_Top, offset) }, k_Grid, k_InkWidth);
            }
        }

        bool m_Gridded;

        // Draws a polyline (local board positions) as one ink stroke at once; returns it
        PaintStroke Trace(Vector3[] local, Color color, float width)
        {
            var stroke = m_Ink.CreateStroke(color, width);
            if (stroke == null)
                return null;
            for (int i = 0; i < local.Length - 1; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(local[i], local[i + 1]) / 0.01f));
                for (int s = 0; s <= steps; s++)
                    stroke.Add(transform.TransformPoint(Vector3.Lerp(local[i], local[i + 1], s / (float)steps) + Vector3.up * 0.0015f), Vector3.up);
            }
            return stroke;
        }

        // The same, drawn out over a moment, as a hand would
        IEnumerator Animate(Vector3[] local, Color color, float width, float seconds, List<PaintStroke> into)
        {
            var stroke = m_Ink.CreateStroke(color, width);
            if (stroke == null)
                yield break;
            into?.Add(stroke);
            float length = 0f;
            for (int i = 1; i < local.Length; i++)
                length += Vector3.Distance(local[i - 1], local[i]);
            int count = Mathf.Max(4, Mathf.CeilToInt(length / 0.008f));
            for (int n = 0; n <= count; n++)
            {
                float t = n / (float)count * (local.Length - 1);
                int segment = Mathf.Min((int)t, local.Length - 2);
                var point = Vector3.Lerp(local[segment], local[segment + 1], t - segment);
                stroke.Add(transform.TransformPoint(point + Vector3.up * 0.0015f), Vector3.up);
                yield return new WaitForSecondsRealtime(seconds / count);
            }
        }

        Vector3[] CrossStroke(int field, bool first)
        {
            var c = FieldCenter(field);
            float r = 0.3f * k_Cell;
            return first
                ? new[] { c + new Vector3(-r, 0f, -r), c + new Vector3(r, 0f, r) }
                : new[] { c + new Vector3(-r, 0f, r), c + new Vector3(r, 0f, -r) };
        }

        Vector3[] CircleStroke(int field)
        {
            var c = FieldCenter(field);
            float r = 0.3f * k_Cell;
            var points = new Vector3[25];
            for (int i = 0; i <= 24; i++)
            {
                float angle = Mathf.PI * 2f * i / 24f + Mathf.PI / 2f; // from the far side, round
                points[i] = c + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            }
            return points;
        }

        // ---- Your move: the stroke you drew ----

        // Hand pinch with the gaze on the board: draw; otherwise (no hand position) the gaze's field is the move
        public override bool BeginPinch(GameCell cell, PinchSource source)
        {
            if (m_Busy || m_Over)
                return true;
            if (source == PinchSource.Hand && Core != null && Core.HandTracker != null && Core.HandTracker.IsTracking)
            {
                m_Stroking = true;
                m_Ink.BeginStroke();
                return true;
            }
            Pick(cell);
            return true;
        }

        public override void EndPinch()
        {
            if (m_Stroking)
            {
                m_Stroking = false;
                m_Ink.EndStroke(); // raises StrokeEnded, or drops a blip
            }
        }

        void OnStrokeEnded(PaintStroke stroke, List<Vector3> worldPoints)
        {
            m_Ink.Remove(stroke); // the scribble is replaced by a neat mark
            if (m_Busy || m_Over || worldPoints.Count == 0)
                return;
            // Where it was drawn: the middle of the stroke over the board, whatever its height
            var sum = Vector3.zero;
            foreach (var point in worldPoints)
                sum += transform.InverseTransformPoint(point);
            var centre = sum / worldPoints.Count;
            int column = Mathf.FloorToInt(centre.x / k_Cell + 1.5f), row = Mathf.FloorToInt(centre.z / k_Cell + 1.5f);
            if (column < 0 || column > 2 || row < 0 || row > 2)
            {
                SetStatus("Narysuj krzyżyk nad jednym z dziewięciu pól.");
                return;
            }
            PlayerMove(row * 3 + column);
        }

        public override void Pick(GameCell cell)
        {
            if (cell != null && !m_Busy && !m_Over)
                PlayerMove(cell.Index);
        }

        void PlayerMove(int field)
        {
            if (m_Board[field] != 0)
            {
                SetStatus("To pole jest zajęte.");
                return;
            }
            StartCoroutine(PlayerThenComputer(field));
        }

        IEnumerator PlayerThenComputer(int field)
        {
            m_Busy = true;
            m_Board[field] = 1;
            yield return Animate(CrossStroke(field, true), k_Cross, k_InkWidth * 1.4f, 0.16f, m_Marks);
            yield return Animate(CrossStroke(field, false), k_Cross, k_InkWidth * 1.4f, 0.16f, m_Marks);
            yield return CheckEnd();
            if (m_Over)
                yield break;
            yield return ComputerMove();
        }

        // ---- The computer: draws a circle ----

        IEnumerator ComputerMove()
        {
            m_Busy = true;
            SetStatus("Ruch komputera…");
            yield return new WaitForSecondsRealtime(0.5f);
            int field = ChooseField();
            m_Board[field] = -1;
            yield return Animate(CircleStroke(field), k_Circle, k_InkWidth * 1.4f, 0.4f, m_Marks);
            yield return CheckEnd();
            if (m_Over)
                yield break;
            m_Busy = false;
            SetStatus("Twój ruch (krzyżyk).");
        }

        // Ends the game (m_Over) when someone has three in a row or the board is full; the winning line is inked
        IEnumerator CheckEnd()
        {
            int winner = Winner(m_Board, out var line);
            bool full = System.Array.IndexOf(m_Board, 0) < 0;
            if (winner == 0 && !full)
                yield break;
            m_Over = true;
            m_Busy = false;
            if (winner != 0)
            {
                var a = FieldCenter(line[0]);
                var b = FieldCenter(line[2]);
                var direction = (b - a).normalized;
                yield return Animate(new[] { a - direction * 0.4f * k_Cell, b + direction * 0.4f * k_Cell }, k_Win, k_InkWidth * 1.8f,
                    0.35f, m_Marks);
            }
            SetStatus(winner == 1 ? "Wygrałeś! „Nowa gra”: zaczyna komputer."
                : winner == -1 ? "Komputer wygrał. „Nowa gra”?" : "Remis. „Nowa gra”?");
        }

        // Easy: usually the best field, but a random one every so often (it also misses wins)
        int ChooseField()
        {
            var free = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                if (m_Board[i] == 0)
                    free.Add(i);
            }
            if (!m_Hard && m_Random.NextDouble() < 0.3)
                return free[m_Random.Next(free.Count)];
            int best = free[0], bestScore = int.MinValue;
            foreach (int field in free)
            {
                m_Board[field] = -1;
                int score = -Negamax(1, 1) + (m_Random.Next(2)); // a little variety between equal moves
                m_Board[field] = 0;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = field;
                }
            }
            return best;
        }

        // Perfect play. side: who moves now (1 cross, -1 circle); returns the score for that side
        int Negamax(int side, int depth)
        {
            int winner = Winner(m_Board, out _);
            if (winner != 0)
                return winner == side ? 10 - depth : depth - 10;
            bool any = false;
            int best = int.MinValue;
            for (int i = 0; i < 9; i++)
            {
                if (m_Board[i] != 0)
                    continue;
                any = true;
                m_Board[i] = side;
                best = Mathf.Max(best, -Negamax(-side, depth + 1));
                m_Board[i] = 0;
            }
            return any ? best : 0;
        }

        static int Winner(int[] board, out int[] line)
        {
            foreach (var l in k_Lines)
            {
                int v = board[l[0]];
                if (v != 0 && v == board[l[1]] && v == board[l[2]])
                {
                    line = l;
                    return v;
                }
            }
            line = null;
            return 0;
        }

        protected override void OnDestroy()
        {
            if (m_Ink != null)
                m_Ink.StrokeEnded -= OnStrokeEnded;
            base.OnDestroy();
        }
    }
}
