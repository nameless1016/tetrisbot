namespace TetrisBot.Game;

public enum PieceType { I = 0, O = 1, T = 2, S = 3, Z = 4, J = 5, L = 6 }

/// <summary>T-spin classification at lock time (guideline 3-corner rule).</summary>
public enum SpinKind { None = 0, Mini = 1, Full = 2 }

/// <summary>Global rule constants and the "infinite" speed curve.</summary>
public static class Rules
{
    public const int SpawnX = 3;          // left edge of the piece's bounding box
    public const int SpawnY = 18;         // rows 0..19 are the hidden buffer, 20..39 are visible
    public const int Gravity20GLevel = 20;
    public const int MaxLockResets = 15;

    /// <summary>Guideline gravity curve. Returns 0 for "instant" (20G) from level 20 on.</summary>
    public static double SecondsPerRow(int level)
    {
        if (level >= Gravity20GLevel) return 0.0;
        double b = 0.8 - (level - 1) * 0.007;
        return Math.Pow(b, level - 1);
    }

    /// <summary>
    /// Lock delay is 0.5s up to level 20, then keeps shrinking by 25ms per level until it
    /// reaches 0 at level 40 (piece locks the instant it lands). This is the "no cap" ramp.
    /// </summary>
    public static double LockDelaySeconds(int level)
        => level <= Gravity20GLevel ? 0.5 : Math.Max(0.0, 0.5 - 0.025 * (level - Gravity20GLevel));
}

/// <summary>Piece shapes (SRS orientation) and SRS wall-kick tables.</summary>
public static class PieceData
{
    // Spawn orientation, (x,y) pairs inside the bounding box, y grows DOWNWARD.
    private static readonly int[][] Spawn =
    {
        new[] { 0,1, 1,1, 2,1, 3,1 }, // I
        new[] { 1,0, 2,0, 1,1, 2,1 }, // O
        new[] { 1,0, 0,1, 1,1, 2,1 }, // T
        new[] { 1,0, 2,0, 0,1, 1,1 }, // S
        new[] { 0,0, 1,0, 1,1, 2,1 }, // Z
        new[] { 0,0, 0,1, 1,1, 2,1 }, // J
        new[] { 2,0, 0,1, 1,1, 2,1 }, // L
    };

    public static int BoxSize(PieceType t) => (t == PieceType.I || t == PieceType.O) ? 4 : 3;

    private static readonly int[][][] Cells = Build();

    private static int[][][] Build()
    {
        var all = new int[7][][];
        for (int t = 0; t < 7; t++)
        {
            all[t] = new int[4][];
            int n = BoxSize((PieceType)t);
            all[t][0] = (int[])Spawn[t].Clone();
            for (int r = 1; r < 4; r++)
            {
                var prev = all[t][r - 1];
                var cur = new int[8];
                for (int i = 0; i < 4; i++)
                {
                    int x = prev[i * 2], y = prev[i * 2 + 1];
                    if (t == (int)PieceType.O) { cur[i * 2] = x; cur[i * 2 + 1] = y; }
                    else { cur[i * 2] = n - 1 - y; cur[i * 2 + 1] = x; } // rotate clockwise
                }
                all[t][r] = cur;
            }
        }
        return all;
    }

    /// <summary>8 ints: x0,y0,x1,y1,... relative to the bounding-box origin.</summary>
    public static int[] GetCells(PieceType t, int rot) => Cells[(int)t][rot];

    /// <summary>True if every cell is inside the hidden buffer (lock-out condition).</summary>
    public static bool IsAllHidden(PieceType t, int rot, int py)
    {
        var c = GetCells(t, rot);
        for (int i = 0; i < 4; i++)
            if (py + c[i * 2 + 1] >= Board.HiddenRows) return false;
        return true;
    }

    // Kick tables as published (y is UP). Index = fromRot*2 + (cw ? 0 : 1). 5 (dx,dy) pairs each.
    private static readonly int[][] JlstzKicks =
    {
        new[] { 0,0, -1,0, -1, 1, 0,-2, -1,-2 }, // 0->R
        new[] { 0,0,  1,0,  1, 1, 0,-2,  1,-2 }, // 0->L
        new[] { 0,0,  1,0,  1,-1, 0, 2,  1, 2 }, // R->2
        new[] { 0,0,  1,0,  1,-1, 0, 2,  1, 2 }, // R->0
        new[] { 0,0,  1,0,  1, 1, 0,-2,  1,-2 }, // 2->L
        new[] { 0,0, -1,0, -1, 1, 0,-2, -1,-2 }, // 2->R
        new[] { 0,0, -1,0, -1,-1, 0, 2, -1, 2 }, // L->0
        new[] { 0,0, -1,0, -1,-1, 0, 2, -1, 2 }, // L->2
    };

    private static readonly int[][] IKicks =
    {
        new[] { 0,0, -2,0,  1,0, -2,-1,  1, 2 }, // 0->R
        new[] { 0,0, -1,0,  2,0, -1, 2,  2,-1 }, // 0->L
        new[] { 0,0, -1,0,  2,0, -1, 2,  2,-1 }, // R->2
        new[] { 0,0,  2,0, -1,0,  2, 1, -1,-2 }, // R->0
        new[] { 0,0,  2,0, -1,0,  2, 1, -1,-2 }, // 2->L
        new[] { 0,0,  1,0, -2,0,  1,-2, -2, 1 }, // 2->R
        new[] { 0,0,  1,0, -2,0,  1,-2, -2, 1 }, // L->0
        new[] { 0,0, -2,0,  1,0, -2,-1,  1, 2 }, // L->2
    };

    /// <summary>i-th kick offset (0..4) in board coordinates (y DOWN).</summary>
    public static (int dx, int dy) Kick(PieceType t, int fromRot, bool cw, int i)
    {
        var table = t == PieceType.I ? IKicks : JlstzKicks;
        var e = table[fromRot * 2 + (cw ? 0 : 1)];
        return (e[i * 2], -e[i * 2 + 1]);
    }
}
