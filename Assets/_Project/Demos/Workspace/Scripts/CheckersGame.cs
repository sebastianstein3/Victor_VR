using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // Warcaby on the table: 8 x 8, the user plays white (nearest, moves first), the computer black.
    // Rules as in Polish/international draughts: men move diagonally forward and capture both ways,
    // capturing is compulsory and the longest capture must be taken, captured pieces come off at the end
    // of the move (and can't be jumped twice), a man ending its move on the far row becomes a king, and
    // kings move and capture along the whole diagonal. No moves left = lost.
    // Look at a white piece and pinch to pick it (its moves light up green), then pinch a green square.
    public class CheckersGame : TableGame
    {
        const float k_Square = 0.045f;
        const float k_Top = 0.014f;   // board surface above the table
        const int k_Depth = 4;        // the computer's look-ahead, in moves
        static readonly Color k_Light = new(0.93f, 0.86f, 0.72f);
        static readonly Color k_Dark = new(0.42f, 0.27f, 0.16f);
        static readonly Color k_Target = new(0.3f, 0.8f, 0.4f);
        static readonly Color k_White = new(0.95f, 0.94f, 0.9f);
        static readonly Color k_Black = new(0.12f, 0.12f, 0.14f);
        static readonly Color k_Selected = new(1f, 0.8f, 0.2f);
        static readonly (int dr, int dc)[] k_Directions = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        class Move
        {
            public List<int> Path;     // squares visited, from the start
            public List<int> Captured;
            public int From => Path[0];
            public int To => Path[^1];
        }

        public override string Title => "Warcaby";

        // 1 white man, 2 white king, -1 black man, -2 black king; index = row * 8 + column, row 0 nearest
        int[] m_Board = new int[64];
        readonly GameObject[] m_Squares = new GameObject[64];
        readonly Dictionary<int, GameObject> m_Pieces = new();
        Transform m_PieceRoot;
        List<Move> m_Legal = new();
        int m_Selected = -1;
        bool m_Busy;
        bool m_Over;
        readonly System.Random m_Random = new();

        protected override void Build()
        {
            float half = 4f * k_Square;
            Shape(PrimitiveType.Cube, transform, new Vector3(0f, k_Top / 2f - 0.002f, 0f),
                new Vector3(2f * half + 0.024f, k_Top - 0.004f, 2f * half + 0.024f), new Color(0.25f, 0.15f, 0.08f), -1, false);
            for (int square = 0; square < 64; square++)
            {
                int row = square / 8, column = square % 8;
                bool dark = (row + column) % 2 == 0;
                m_Squares[square] = Shape(PrimitiveType.Cube, transform, SquarePosition(square) - Vector3.up * 0.002f,
                    new Vector3(k_Square, 0.004f, k_Square), dark ? k_Dark : k_Light, dark ? square : -1, dark);
            }
            m_PieceRoot = new GameObject("Pieces").transform;
            m_PieceRoot.SetParent(transform, false);
        }

        Vector3 SquarePosition(int square) =>
            new((square % 8 - 3.5f) * k_Square, k_Top, (square / 8 - 3.5f) * k_Square);

        public override void NewGame()
        {
            StopAllCoroutines();
            m_Board = new int[64];
            for (int square = 0; square < 64; square++)
            {
                int row = square / 8, column = square % 8;
                if ((row + column) % 2 != 0)
                    continue;
                if (row <= 2) m_Board[square] = 1;
                else if (row >= 5) m_Board[square] = -1;
            }
            m_Busy = false;
            m_Over = false;
            m_Selected = -1;
            RefreshPieces();
            m_Legal = LegalMoves(m_Board, 1);
            ShowTargets();
            SetStatus("Twój ruch (białe). Spójrz na pionek i uszczypnij.");
        }

        public override void Pick(GameCell cell)
        {
            if (m_Busy || m_Over || cell == null)
                return;
            int square = cell.Index;
            if (m_Board[square] > 0)
            {
                bool canMove = m_Legal.Exists(m => m.From == square);
                m_Selected = canMove ? square : -1;
                ShowTargets();
                if (!canMove)
                    SetStatus(m_Legal.Count > 0 && m_Legal[0].Captured.Count > 0
                        ? "Bicie jest obowiązkowe – wybierz pionek, który bije (najdłuższe bicie)."
                        : "Ten pionek nie ma ruchu.");
                return;
            }
            if (m_Selected < 0)
                return;
            var move = m_Legal.Find(m => m.From == m_Selected && m.To == square);
            if (move == null)
                return;
            m_Selected = -1;
            ShowTargets();
            StartCoroutine(Play(move, userMove: true));
        }

        IEnumerator Play(Move move, bool userMove)
        {
            m_Busy = true;
            // The piece hops along its path, then the captured pieces come off
            if (m_Pieces.TryGetValue(move.From, out var piece) && piece != null)
            {
                for (int i = 1; i < move.Path.Count; i++)
                {
                    Vector3 from = piece.transform.localPosition, to = PiecePosition(move.Path[i]);
                    for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.22f)
                    {
                        piece.transform.localPosition = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.02f;
                        yield return null;
                    }
                    piece.transform.localPosition = to;
                }
            }
            Apply(m_Board, move);
            RefreshPieces();

            int next = userMove ? -1 : 1;
            m_Legal = LegalMoves(m_Board, next);
            if (m_Legal.Count == 0)
            {
                m_Over = true;
                m_Busy = false;
                SetStatus(userMove ? "Wygrałeś! „Nowa gra”, aby zagrać jeszcze raz." : "Przegrana – komputer wygrał. „Nowa gra”?");
                yield break;
            }
            if (userMove)
            {
                SetStatus("Ruch komputera…");
                yield return new WaitForSecondsRealtime(0.5f);
                var reply = ChooseMove(m_Board, -1);
                yield return Play(reply, userMove: false);
                yield break;
            }
            m_Busy = false;
            ShowTargets();
            SetStatus(m_Legal[0].Captured.Count > 0 ? "Twój ruch – masz bicie." : "Twój ruch.");
        }

        Vector3 PiecePosition(int square) => SquarePosition(square) + Vector3.up * 0.005f;

        void RefreshPieces()
        {
            foreach (var piece in m_Pieces.Values)
            {
                if (piece != null)
                    Destroy(piece);
            }
            m_Pieces.Clear();
            for (int square = 0; square < 64; square++)
            {
                int value = m_Board[square];
                if (value == 0)
                    continue;
                var color = value > 0 ? k_White : k_Black;
                var piece = Shape(PrimitiveType.Cylinder, m_PieceRoot, PiecePosition(square), new Vector3(0.036f, 0.005f, 0.036f), color, square);
                if (Math.Abs(value) == 2) // a king: a crown on top
                    Shape(PrimitiveType.Cylinder, piece.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.6f, 0.6f, 0.6f),
                        new Color(0.9f, 0.7f, 0.2f), square);
                m_Pieces[square] = piece;
            }
        }

        // The selected piece raised and gold, the squares it can go to green
        void ShowTargets()
        {
            for (int square = 0; square < 64; square++)
            {
                if ((square / 8 + square % 8) % 2 == 0)
                    SetColor(m_Squares[square], k_Dark);
            }
            foreach (var pair in m_Pieces)
            {
                if (pair.Value == null)
                    continue;
                bool selected = pair.Key == m_Selected;
                pair.Value.transform.localPosition = PiecePosition(pair.Key) + (selected ? Vector3.up * 0.01f : Vector3.zero);
                SetColor(pair.Value, selected ? k_Selected : m_Board[pair.Key] > 0 ? k_White : k_Black);
            }
            if (m_Selected < 0)
                return;
            foreach (var move in m_Legal)
            {
                if (move.From == m_Selected)
                    SetColor(m_Squares[move.To], k_Target);
            }
        }

        // ---- Rules ----

        static bool Inside(int row, int column) => row >= 0 && row < 8 && column >= 0 && column < 8;

        static List<Move> LegalMoves(int[] board, int side)
        {
            var captures = new List<Move>();
            for (int square = 0; square < 64; square++)
            {
                if (Math.Sign(board[square]) == side)
                    CapturesFrom(board, square, captures);
            }
            if (captures.Count > 0)
            {
                int longest = 0;
                foreach (var move in captures)
                    longest = Math.Max(longest, move.Captured.Count);
                return captures.FindAll(m => m.Captured.Count == longest);
            }
            var moves = new List<Move>();
            for (int square = 0; square < 64; square++)
            {
                int piece = board[square];
                if (Math.Sign(piece) != side)
                    continue;
                int row = square / 8, column = square % 8;
                foreach (var (dr, dc) in k_Directions)
                {
                    if (Math.Abs(piece) == 1 && dr != side)
                        continue; // men only forward
                    int r = row + dr, c = column + dc;
                    while (Inside(r, c) && board[r * 8 + c] == 0)
                    {
                        moves.Add(new Move { Path = new List<int> { square, r * 8 + c }, Captured = new List<int>() });
                        if (Math.Abs(piece) == 1)
                            break;
                        r += dr;
                        c += dc;
                    }
                }
            }
            return moves;
        }

        static void CapturesFrom(int[] board, int from, List<Move> result)
        {
            int piece = board[from];
            board[from] = 0; // the moving piece leaves its square (it may pass it again)
            CaptureStep(board, from, piece, new List<int> { from }, new List<int>(), result);
            board[from] = piece;
        }

        // Captured pieces stay on the board until the move ends: they block, and can't be jumped twice
        static void CaptureStep(int[] board, int at, int piece, List<int> path, List<int> captured, List<Move> result)
        {
            int side = Math.Sign(piece);
            bool king = Math.Abs(piece) == 2, extended = false;
            int row = at / 8, column = at % 8;
            foreach (var (dr, dc) in k_Directions)
            {
                int r = row + dr, c = column + dc;
                if (king)
                {
                    while (Inside(r, c) && board[r * 8 + c] == 0)
                    {
                        r += dr;
                        c += dc;
                    }
                }
                if (!Inside(r, c))
                    continue;
                int jumped = r * 8 + c;
                if (Math.Sign(board[jumped]) != -side || captured.Contains(jumped))
                    continue;
                r += dr;
                c += dc;
                while (Inside(r, c) && board[r * 8 + c] == 0)
                {
                    int land = r * 8 + c;
                    path.Add(land);
                    captured.Add(jumped);
                    CaptureStep(board, land, piece, path, captured, result);
                    path.RemoveAt(path.Count - 1);
                    captured.RemoveAt(captured.Count - 1);
                    extended = true;
                    if (!king)
                        break; // a man lands just behind
                    r += dr;
                    c += dc;
                }
            }
            if (!extended && captured.Count > 0)
                result.Add(new Move { Path = new List<int>(path), Captured = new List<int>(captured) });
        }

        static void Apply(int[] board, Move move)
        {
            int piece = board[move.From];
            board[move.From] = 0;
            foreach (var square in move.Captured)
                board[square] = 0;
            int row = move.To / 8;
            if (piece == 1 && row == 7) piece = 2;
            else if (piece == -1 && row == 0) piece = -2;
            board[move.To] = piece;
        }

        // ---- The computer: negamax with alpha-beta, material and advancement ----

        Move ChooseMove(int[] board, int side)
        {
            var moves = LegalMoves(board, side);
            Move best = moves[0];
            int bestScore = int.MinValue;
            foreach (var move in moves)
            {
                var copy = (int[])board.Clone();
                Apply(copy, move);
                int score = -Search(copy, k_Depth - 1, -int.MaxValue, int.MaxValue, -side) + m_Random.Next(3); // a little variety
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        static int Search(int[] board, int depth, int alpha, int beta, int side)
        {
            var moves = LegalMoves(board, side);
            if (moves.Count == 0)
                return -100000 - depth; // lost; sooner is worse
            if (depth == 0)
                return side * Evaluate(board);
            foreach (var move in moves)
            {
                var copy = (int[])board.Clone();
                Apply(copy, move);
                int score = -Search(copy, depth - 1, -beta, -alpha, -side);
                if (score >= beta)
                    return score;
                if (score > alpha)
                    alpha = score;
            }
            return alpha;
        }

        // From white's side: men 100, kings 320, men further forward a little more
        static int Evaluate(int[] board)
        {
            int score = 0;
            for (int square = 0; square < 64; square++)
            {
                int piece = board[square], row = square / 8;
                switch (piece)
                {
                    case 1: score += 100 + row * 4; break;
                    case 2: score += 320; break;
                    case -1: score -= 100 + (7 - row) * 4; break;
                    case -2: score -= 320; break;
                }
            }
            return score;
        }
    }
}
