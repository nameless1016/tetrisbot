namespace TetrisBot.Game;

/// <summary>A reachable final resting position for a piece.</summary>
public struct Landing
{
    public int Rot, X, Y;
    public int Inputs;       // approximate number of key presses (shifts + rotations)
    public SpinKind Spin;
}

public struct SearchOptions
{
    public bool HardDropOnly;   // only straight drops (fast, no tucks/spins)
    public bool Gravity20G;     // piece falls instantly after every input
    public int MaxInputs;       // input budget before the piece locks

    public static SearchOptions Full => new SearchOptions { MaxInputs = int.MaxValue };
    public static SearchOptions HardDrop => new SearchOptions { HardDropOnly = true, MaxInputs = int.MaxValue };

    /// <summary>Search limits that mimic the game's current speed (20G + shrinking lock delay).</summary>
    public static SearchOptions ForLevel(int level, bool hardDropOnly, double inputsPerSecond = 15.0)
    {
        var o = new SearchOptions { HardDropOnly = hardDropOnly, MaxInputs = int.MaxValue };
        if (level >= Rules.Gravity20GLevel)
        {
            o.Gravity20G = true;
            o.MaxInputs = (int)(Rules.LockDelaySeconds(level) * inputsPerSecond);
        }
        return o;
    }
}

/// <summary>
/// Finds every position a piece can reach from spawn using BFS over
/// (x, y, rotation, last-move-was-rotation) with real SRS kicks. This is what makes
/// tucks, slides and T-spins available to the bot.
/// </summary>
public sealed class PlacementSearch
{
    private const int XOff = 3, XRange = 14, KickStates = 3;
    private const int StateCount = Board.Height * XRange * 4 * KickStates;

    private readonly bool[] _visited = new bool[StateCount];
    private readonly int[] _queue = new int[StateCount];
    private readonly int[] _depth = new int[StateCount];
    private readonly HashSet<long> _seen = new();

    private Board _board;
    private PieceType _type;
    private int _tail;
    private bool _g20;

    private static int Encode(int x, int y, int rot, int kick)
        => ((y * XRange + (x + XOff)) * 4 + rot) * KickStates + kick;

    private static void Decode(int s, out int x, out int y, out int rot, out int kick)
    {
        kick = s % KickStates; s /= KickStates;
        rot = s % 4; s /= 4;
        x = s % XRange - XOff;
        y = s / XRange;
    }

    private void Push(int x, int y, int rot, int kick, int d)
    {
        if (x < -XOff || x >= XRange - XOff || y < 0 || y >= Board.Height) return;
        int s = Encode(x, y, rot, kick);
        if (_visited[s]) return;
        _visited[s] = true;
        _depth[s] = d;
        _queue[_tail++] = s;
    }

    private int DropY(int x, int y, int rot)
    {
        while (!_board.Collides(_type, rot, x, y + 1)) y++;
        return y;
    }

    public void Find(Board board, PieceType type, SearchOptions opt, List<Landing> result)
    {
        result.Clear();
        _seen.Clear();
        _board = board;
        _type = type;
        _g20 = opt.Gravity20G;

        if (board.Collides(type, 0, Rules.SpawnX, Rules.SpawnY)) return; // blocked out

        if (opt.HardDropOnly)
        {
            int maxRot = type == PieceType.O ? 1 : 4;
            for (int rot = 0; rot < maxRot; rot++)
            {
                int rotInputs = rot == 3 ? 1 : rot;
                for (int x = -XOff; x < XRange - XOff; x++)
                {
                    if (board.Collides(type, rot, x, Rules.SpawnY)) continue;
                    int inputs = rotInputs + Math.Abs(x - Rules.SpawnX);
                    if (inputs > opt.MaxInputs) continue;
                    AddLanding(result, rot, x, DropY(x, Rules.SpawnY, rot), 0, inputs);
                }
            }
            return;
        }

        Array.Clear(_visited, 0, _visited.Length);
        _tail = 0;
        int head = 0;
        int y0 = Rules.SpawnY;
        if (_g20) y0 = DropY(Rules.SpawnX, y0, 0);
        Push(Rules.SpawnX, y0, 0, 0, 0);
        bool canRotate = type != PieceType.O;

        while (head < _tail)
        {
            int s = _queue[head++];
            Decode(s, out int x, out int y, out int rot, out int kick);
            int d = _depth[s];

            bool grounded = board.Collides(type, rot, x, y + 1);
            if (grounded) AddLanding(result, rot, x, y, kick, d);
            else if (!_g20) Push(x, y + 1, rot, 0, d);   // soft drop step is free

            if (d >= opt.MaxInputs) continue;

            for (int dx = -1; dx <= 1; dx += 2)
            {
                if (board.Collides(type, rot, x + dx, y)) continue;
                int ny = _g20 ? DropY(x + dx, y, rot) : y;
                Push(x + dx, ny, rot, 0, d + 1);
            }

            if (!canRotate) continue;
            for (int dir = 0; dir < 2; dir++)
            {
                bool cw = dir == 0;
                int nr = cw ? (rot + 1) & 3 : (rot + 3) & 3;
                for (int i = 0; i < 5; i++)
                {
                    var (kx, ky) = PieceData.Kick(type, rot, cw, i);
                    if (board.Collides(type, nr, x + kx, y + ky)) continue;
                    int ny = y + ky;
                    int flag = i == 4 ? 2 : 1;           // 2 = used the 5th (last) kick
                    if (_g20)
                    {
                        int dy2 = DropY(x + kx, ny, nr);
                        if (dy2 != ny) { ny = dy2; flag = 0; } // falling cancels the spin
                    }
                    Push(x + kx, ny, nr, flag, d + 1);
                    break;
                }
            }
        }
    }

    private void AddLanding(List<Landing> result, int rot, int x, int y, int kick, int inputs)
    {
        var spin = SpinKind.None;
        if (_type == PieceType.T && kick != 0) spin = DetectTSpin(_board, rot, x, y, kick);
        if (!_seen.Add(CellKey(rot, x, y, spin))) return;
        result.Add(new Landing { Rot = rot, X = x, Y = y, Spin = spin, Inputs = inputs });
    }

    /// <summary>Identity of a landing = which 4 cells it fills + spin kind (dedupes symmetric rotations).</summary>
    private long CellKey(int rot, int x, int y, SpinKind spin)
    {
        int[] c = PieceData.GetCells(_type, rot);
        Span<int> v = stackalloc int[4];
        for (int i = 0; i < 4; i++) v[i] = (y + c[i * 2 + 1]) * Board.Width + x + c[i * 2];
        v.Sort();
        long k = (((long)v[0] * 400 + v[1]) * 400 + v[2]) * 400 + v[3];
        return k * 3 + (int)spin;
    }

    /// <summary>
    /// Guideline 3-corner T-spin test. Call only if the last successful action was a rotation.
    /// Mini unless both "front" corners are filled or the last (5th) kick was used.
    /// kick: 1 = rotated normally, 2 = rotated using the final kick offset.
    /// </summary>
    public static SpinKind DetectTSpin(Board b, int rot, int px, int py, int kick)
    {
        bool tl = b.IsBlocked(px, py), tr = b.IsBlocked(px + 2, py);
        bool bl = b.IsBlocked(px, py + 2), br = b.IsBlocked(px + 2, py + 2);
        int count = (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
        if (count < 3) return SpinKind.None;
        bool front = rot switch { 0 => tl && tr, 1 => tr && br, 2 => bl && br, _ => tl && bl };
        return (front || kick == 2) ? SpinKind.Full : SpinKind.Mini;
    }
}
