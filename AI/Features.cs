using TetrisBot.Game;

namespace TetrisBot.AI;

public struct BoardStats
{
    public int Holes, Bumpiness, MaxHeight, AggHeight, WellDepth, RowTransitions;
}

public static class BoardAnalyzer
{
    /// <summary>Computes column heights (into <paramref name="h"/>, length 10) and stack-quality stats.</summary>
    public static BoardStats Analyze(Board b, int[] h)
    {
        var s = new BoardStats();
        var cells = b.Cells;
        for (int x = 0; x < Board.Width; x++)
        {
            int top = Board.Height;
            for (int y = 0; y < Board.Height; y++)
                if (cells[y * Board.Width + x] != 0) { top = y; break; }
            h[x] = Board.Height - top;
            for (int y = top + 1; y < Board.Height; y++)
                if (cells[y * Board.Width + x] == 0) s.Holes++;
            s.AggHeight += h[x];
            if (h[x] > s.MaxHeight) s.MaxHeight = h[x];
        }
        for (int x = 0; x < Board.Width - 1; x++) s.Bumpiness += Math.Abs(h[x] - h[x + 1]);

        for (int x = 0; x < Board.Width; x++)
        {
            int nb = x == 0 ? h[1] : x == Board.Width - 1 ? h[Board.Width - 2] : Math.Min(h[x - 1], h[x + 1]);
            int depth = nb - h[x];
            if (depth > s.WellDepth) s.WellDepth = depth;
        }

        for (int y = Board.Height - s.MaxHeight; y < Board.Height; y++)
        {
            bool prev = true; // left wall counts as filled
            for (int x = 0; x < Board.Width; x++)
            {
                bool filled = cells[y * Board.Width + x] != 0;
                if (filled != prev) s.RowTransitions++;
                prev = filled;
            }
            if (!prev) s.RowTransitions++; // right wall
        }
        return s;
    }
}

/// <summary>
/// Turns an afterstate (board after the move + piece queue info) into a normalized feature
/// vector in [0,1]. This is the "data preprocessing" step of the RL pipeline.
/// </summary>
public sealed class FeatureExtractor
{
    public const int Size = 35;
    private readonly int[] _h = new int[Board.Width];

    public BoardStats Analyze(Board b) => BoardAnalyzer.Analyze(b, _h);

    /// <summary>
    /// Layout: [0-9] column heights | 10 holes | 11 bumpiness | 12 max height | 13 deepest well |
    /// 14 row transitions | 15 lines cleared | 16 spin | 17 back-to-back | 18 combo | 19 all-clear |
    /// [20-27] hold piece one-hot (7 = empty) | [28-34] next piece one-hot.
    /// </summary>
    public BoardStats Extract(Board after, Placement p, ClearResult clr, ScoreState sc, double[] dst)
    {
        var st = Analyze(after);
        for (int i = 0; i < Board.Width; i++) dst[i] = Math.Min(1.0, _h[i] / 20.0);
        dst[10] = Math.Min(1.0, st.Holes / 20.0);
        dst[11] = Math.Min(1.0, st.Bumpiness / 60.0);
        dst[12] = Math.Min(1.0, st.MaxHeight / 20.0);
        dst[13] = Math.Min(1.0, st.WellDepth / 10.0);
        dst[14] = Math.Min(1.0, st.RowTransitions / 100.0);
        dst[15] = clr.Lines / 4.0;
        dst[16] = clr.Spin == SpinKind.Full ? 1.0 : clr.Spin == SpinKind.Mini ? 0.5 : 0.0;
        dst[17] = sc.BackToBack ? 1.0 : 0.0;
        dst[18] = Math.Min(1.0, Math.Max(0, sc.Chain - 1) / 10.0);
        dst[19] = clr.AllClear ? 1.0 : 0.0;
        for (int i = 20; i < Size; i++) dst[i] = 0.0;
        dst[20 + (p.HoldAfter.HasValue ? (int)p.HoldAfter.Value : 7)] = 1.0;
        dst[28 + (int)p.NextPiece] = 1.0;
        return st;
    }
}
