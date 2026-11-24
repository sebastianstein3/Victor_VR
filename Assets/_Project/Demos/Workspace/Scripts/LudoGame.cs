using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Chińczyk on the table: four players, the user red (base nearest, on the left), the computer blue,
    // green and yellow. The classic cross-shaped board of 40 fields; each player has a base of four,
    // a start field and a home row of four. Rules: a 6 brings a pawn out onto the start field, a 6 also
    // gives another roll, with no pawn out there are three tries for a 6, landing on another player's
    // pawn sends it back to its base, a pawn can't land on its own colour, and it goes home only with
    // the exact number. First with all four home wins.
    // Roll: the dice button on the panel, the dice by the board, or "rzuć". Then pinch a pawn that can move
    // (they are raised).
    public class LudoGame : TableGame
    {
        const float k_Cell = 0.032f;
        const float k_Top = 0.012f;
        static readonly Color[] k_Colors =
            { new(0.9f, 0.2f, 0.2f), new(0.2f, 0.45f, 0.95f), new(0.25f, 0.75f, 0.3f), new(0.95f, 0.82f, 0.2f) };
        static readonly string[] k_Names = { "Ty", "Niebieski", "Zielony", "Żółty" };
        const int k_DiceIndex = 100;

        // The 40 fields, clockwise from red's start, on an 11 x 11 grid (x right, y away from the user)
        static readonly Vector2Int[] k_Track = BuildTrack();
        // Home rows (from the track inwards) and bases, per player
        static readonly Vector2Int[][] k_Home =
        {
            new[] { new Vector2Int(1, 5), new Vector2Int(2, 5), new Vector2Int(3, 5), new Vector2Int(4, 5) },
            new[] { new Vector2Int(5, 1), new Vector2Int(5, 2), new Vector2Int(5, 3), new Vector2Int(5, 4) },
            new[] { new Vector2Int(9, 5), new Vector2Int(8, 5), new Vector2Int(7, 5), new Vector2Int(6, 5) },
            new[] { new Vector2Int(5, 9), new Vector2Int(5, 8), new Vector2Int(5, 7), new Vector2Int(5, 6) },
        };
        static readonly Vector2Int[][] k_Base =
        {
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
            new[] { new Vector2Int(9, 0), new Vector2Int(10, 0), new Vector2Int(9, 1), new Vector2Int(10, 1) },
            new[] { new Vector2Int(9, 9), new Vector2Int(10, 9), new Vector2Int(9, 10), new Vector2Int(10, 10) },
            new[] { new Vector2Int(0, 9), new Vector2Int(1, 9), new Vector2Int(0, 10), new Vector2Int(1, 10) },
        };

        static Vector2Int[] BuildTrack()
        {
            var track = new List<Vector2Int>();
            void Line(int x0, int y0, int x1, int y1)
            {
                int dx = System.Math.Sign(x1 - x0), dy = System.Math.Sign(y1 - y0);
                for (int x = x0, y = y0; ; x += dx, y += dy)
                {
                    track.Add(new Vector2Int(x, y));
                    if (x == x1 && y == y1)
                        break;
                }
            }
            Line(0, 4, 4, 4); Line(4, 3, 4, 0); Line(5, 0, 5, 0);
            Line(6, 0, 6, 4); Line(7, 4, 10, 4); Line(10, 5, 10, 5);
            Line(10, 6, 6, 6); Line(6, 7, 6, 10); Line(5, 10, 5, 10);
            Line(4, 10, 4, 6); Line(3, 6, 0, 6); Line(0, 5, 0, 5);
            return track.ToArray(); // 40; starts at 0, 10, 20, 30
        }

        public override string Title => "Chińczyk";
        public override string ActionLabel => "Rzuć kostką";

        // Per pawn (player * 4 + i): -1 in the base, 0..39 fields from its start, 40..43 its home row
        readonly int[] m_Steps = new int[16];
        readonly GameObject[] m_Pawns = new GameObject[16];
        GameObject m_Dice;
        int m_Player;
        int m_Roll;
        int m_Tries;
        bool m_WaitingForRoll;
        bool m_WaitingForPawn;
        bool m_Over;
        readonly System.Random m_Random = new();

        protected override void Build()
        {
            Shape(PrimitiveType.Cube, transform, new Vector3(0f, k_Top / 2f - 0.002f, 0f),
                new Vector3(11.6f * k_Cell, k_Top - 0.004f, 11.6f * k_Cell), new Color(0.95f, 0.9f, 0.78f), -1, false);
            var field = new Vector3(k_Cell * 0.82f, 0.0015f, k_Cell * 0.82f);
            for (int i = 0; i < k_Track.Length; i++)
            {
                int owner = i % 10 == 0 ? i / 10 : -1; // start fields in their player's colour
                Shape(PrimitiveType.Cylinder, transform, Cell(k_Track[i]), field, owner >= 0 ? k_Colors[owner] : Color.white, -1, false);
            }
            for (int player = 0; player < 4; player++)
            {
                var pale = Color.Lerp(k_Colors[player], Color.white, 0.45f);
                foreach (var home in k_Home[player])
                    Shape(PrimitiveType.Cylinder, transform, Cell(home), field, pale, -1, false);
                foreach (var spot in k_Base[player])
                    Shape(PrimitiveType.Cylinder, transform, Cell(spot), field, pale, -1, false);
            }
            for (int pawn = 0; pawn < 16; pawn++)
            {
                var body = Shape(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(0.018f, 0.009f, 0.018f), k_Colors[pawn / 4], pawn);
                Shape(PrimitiveType.Sphere, body.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.75f, 1.5f, 0.75f), k_Colors[pawn / 4], pawn);
                m_Pawns[pawn] = body;
            }
            // The dice, by the user's right-hand corner
            m_Dice = Shape(PrimitiveType.Cube, transform, new Vector3(6.6f * k_Cell, k_Top + 0.012f, -4.5f * k_Cell),
                Vector3.one * 0.024f, Color.white, k_DiceIndex);
        }

        Vector3 Cell(Vector2Int grid) => new((grid.x - 5) * k_Cell, k_Top, (grid.y - 5) * k_Cell);

        Vector3 PawnPosition(int pawn)
        {
            int player = pawn / 4, steps = m_Steps[pawn];
            var grid = steps < 0 ? k_Base[player][pawn % 4] : steps < 40 ? k_Track[(player * 10 + steps) % 40] : k_Home[player][steps - 40];
            return Cell(grid) + Vector3.up * 0.0105f;
        }

        public override void NewGame()
        {
            StopAllCoroutines();
            for (int pawn = 0; pawn < 16; pawn++)
            {
                m_Steps[pawn] = -1;
                m_Pawns[pawn].transform.localPosition = PawnPosition(pawn);
            }
            m_Over = false;
            m_Player = 0;
            StartTurn();
        }

        void StartTurn()
        {
            bool anyOut = false;
            for (int i = 0; i < 4; i++)
            {
                int steps = m_Steps[m_Player * 4 + i];
                anyOut |= steps >= 0 && steps < 40;
            }
            m_Tries = anyOut ? 1 : 3;
            Raise(null);
            if (m_Player == 0)
            {
                m_WaitingForRoll = true;
                SetStatus("Twoja kolej: rzuć kostką.");
            }
            else
                StartCoroutine(ComputerTurn());
        }

        public override void Action() => UserRoll();

        public override void Pick(GameCell cell)
        {
            if (cell == null || m_Over)
                return;
            if (cell.Index == k_DiceIndex)
            {
                UserRoll();
                return;
            }
            int pawn = cell.Index;
            if (!m_WaitingForPawn || pawn / 4 != 0 || Target(pawn, m_Roll) < 0)
                return;
            m_WaitingForPawn = false;
            Raise(null);
            StartCoroutine(MoveThenContinue(pawn));
        }

        void UserRoll()
        {
            if (!m_WaitingForRoll || m_Over)
                return;
            m_WaitingForRoll = false;
            StartCoroutine(UserRollRoutine());
        }

        IEnumerator UserRollRoutine()
        {
            yield return RollDice();
            var movable = Movable(0, m_Roll);
            if (movable.Count == 0)
            {
                m_Tries--;
                if (m_Tries > 0)
                {
                    SetStatus($"Wypadło {m_Roll}. Rzuć jeszcze raz (zostało prób: {m_Tries}).");
                    m_WaitingForRoll = true;
                    yield break;
                }
                SetStatus($"Wypadło {m_Roll} – brak ruchu.");
                yield return new WaitForSecondsRealtime(1f);
                NextPlayer();
                yield break;
            }
            if (movable.Count == 1)
            {
                SetStatus($"Wypadło {m_Roll}.");
                yield return MoveThenContinue(movable[0]);
                yield break;
            }
            SetStatus($"Wypadło {m_Roll}: uszczypnij pionek, który się rusza (uniesione).");
            Raise(movable);
            m_WaitingForPawn = true;
        }

        IEnumerator ComputerTurn()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                yield return RollDice();
                var movable = Movable(m_Player, m_Roll);
                if (movable.Count == 0)
                {
                    m_Tries--;
                    SetStatus($"{k_Names[m_Player]}: {m_Roll}, brak ruchu.");
                    if (m_Tries > 0)
                        continue;
                    yield return new WaitForSecondsRealtime(0.6f);
                    NextPlayer();
                    yield break;
                }
                SetStatus($"{k_Names[m_Player]}: wypadło {m_Roll}.");
                yield return MoveThenContinue(Choose(movable));
                yield break;
            }
        }

        IEnumerator RollDice()
        {
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                m_Dice.transform.localRotation = Random.rotationUniform;
                yield return null;
            }
            m_Dice.transform.localRotation = Quaternion.identity;
            m_Roll = m_Random.Next(1, 7);
        }

        // Moves the pawn field by field, takes what it lands on, then: a win, another roll after a 6, or the next player
        IEnumerator MoveThenContinue(int pawn)
        {
            int target = Target(pawn, m_Roll);
            int from = m_Steps[pawn];
            int first = from < 0 ? 0 : from + 1;
            for (int step = first; step <= target; step++)
            {
                m_Steps[pawn] = step;
                Vector3 a = m_Pawns[pawn].transform.localPosition, b = PawnPosition(pawn);
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.14f)
                {
                    m_Pawns[pawn].transform.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.012f;
                    yield return null;
                }
                m_Pawns[pawn].transform.localPosition = b;
            }
            // Someone else's pawn on that field goes back to its base
            if (target < 40)
            {
                int field = (pawn / 4 * 10 + target) % 40;
                for (int other = 0; other < 16; other++)
                {
                    if (other / 4 != pawn / 4 && m_Steps[other] >= 0 && m_Steps[other] < 40 &&
                        (other / 4 * 10 + m_Steps[other]) % 40 == field)
                    {
                        m_Steps[other] = -1;
                        m_Pawns[other].transform.localPosition = PawnPosition(other);
                        SetStatus(other / 4 == 0 ? $"{k_Names[pawn / 4]} zbił Twój pionek!" : $"{k_Names[pawn / 4]} zbija: {k_Names[other / 4]} wraca do bazy.");
                        yield return new WaitForSecondsRealtime(0.6f);
                    }
                }
            }
            if (AllHome(pawn / 4))
            {
                m_Over = true;
                SetStatus(pawn / 4 == 0 ? "Wygrałeś! Wszystkie pionki w domu." : $"Wygrywa: {k_Names[pawn / 4]}. „Nowa gra”?");
                yield break;
            }
            if (m_Roll == 6)
            {
                StartTurn(); // the same player again
                if (m_Player == 0)
                    SetStatus("Szóstka: rzucasz jeszcze raz.");
                yield break;
            }
            NextPlayer();
        }

        void NextPlayer()
        {
            m_Player = (m_Player + 1) % 4;
            StartTurn();
        }

        // Where the pawn would end with this roll (steps), or -1 if it can't move
        int Target(int pawn, int roll)
        {
            int player = pawn / 4, steps = m_Steps[pawn];
            int target = steps < 0 ? (roll == 6 ? 0 : -1) : steps + roll;
            if (target < 0 || target > 43)
                return -1;
            for (int own = player * 4; own < player * 4 + 4; own++)
            {
                if (own != pawn && m_Steps[own] == target)
                    return -1; // its own colour is there
            }
            return target;
        }

        List<int> Movable(int player, int roll)
        {
            var movable = new List<int>();
            for (int pawn = player * 4; pawn < player * 4 + 4; pawn++)
            {
                if (m_Steps[pawn] < 44 && Target(pawn, roll) >= 0)
                    movable.Add(pawn);
            }
            return movable;
        }

        bool AllHome(int player)
        {
            for (int pawn = player * 4; pawn < player * 4 + 4; pawn++)
            {
                if (m_Steps[pawn] < 40)
                    return false;
            }
            return true;
        }

        // The computer: take a pawn if it can, else bring one out, else go home, else move the one furthest on
        int Choose(List<int> movable)
        {
            int best = movable[0];
            float bestScore = float.MinValue;
            foreach (int pawn in movable)
            {
                int target = Target(pawn, m_Roll);
                float score = m_Steps[pawn] * 0.5f + (float)m_Random.NextDouble();
                if (m_Steps[pawn] < 0)
                    score += 80f;
                if (target >= 40)
                    score += 60f;
                if (target < 40)
                {
                    int field = (pawn / 4 * 10 + target) % 40;
                    for (int other = 0; other < 16; other++)
                    {
                        if (other / 4 != pawn / 4 && m_Steps[other] >= 0 && m_Steps[other] < 40 && (other / 4 * 10 + m_Steps[other]) % 40 == field)
                            score += 100f;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = pawn;
                }
            }
            return best;
        }

        // The pawns the user can move, raised a little
        void Raise(List<int> pawns)
        {
            for (int pawn = 0; pawn < 16; pawn++)
            {
                bool up = pawns != null && pawns.Contains(pawn);
                m_Pawns[pawn].transform.localPosition = PawnPosition(pawn) + (up ? Vector3.up * 0.012f : Vector3.zero);
            }
        }
    }
}
