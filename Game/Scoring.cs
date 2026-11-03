namespace TetrisBot.Game;

public struct ClearResult
{
    public int Lines;
    public SpinKind Spin;
    public bool AllClear;
    public bool BackToBack;   // B2B bonus was applied to this clear
    public int Combo;         // 0 = first clear of a chain
    public int BasePoints;    // before the level multiplier (used for RL reward)
    public int Points;        // BasePoints * level
}

/// <summary>
/// Modern guideline scoring: line clears, T-spins (+mini), back-to-back x1.5, combo, all-clear.
/// A struct so the AI can copy it cheaply when simulating afterstates.
/// </summary>
public struct ScoreState
{
    public int Chain;          // consecutive line-clearing pieces (0 = none)
    public bool BackToBack;

    public ClearResult Apply(int lines, SpinKind spin, bool allClear, int level)
    {
        var r = new ClearResult { Lines = lines, Spin = spin, AllClear = allClear };
        int pts = LinePoints(lines, spin);
        bool difficult = lines > 0 && (lines >= 4 || spin != SpinKind.None);
        bool b2bApplied = false;

        if (lines > 0)
        {
            if (difficult)
            {
                if (BackToBack) { b2bApplied = true; pts = pts * 3 / 2; }
                BackToBack = true;
            }
            else BackToBack = false;
            Chain++;
        }
        else Chain = 0;

        int combo = Math.Max(0, Chain - 1);
        pts += 50 * combo;

        if (allClear && lines > 0)
            pts += lines switch { 1 => 800, 2 => 1200, 3 => 1800, _ => b2bApplied ? 3200 : 2000 };

        r.BackToBack = b2bApplied;
        r.Combo = combo;
        r.BasePoints = pts;
        r.Points = pts * level;
        return r;
    }

    private static int LinePoints(int lines, SpinKind spin) => spin switch
    {
        SpinKind.Full => lines switch { 0 => 400, 1 => 800, 2 => 1200, _ => 1600 },
        SpinKind.Mini => lines switch { 0 => 100, 1 => 200, _ => 400 },
        _ => lines switch { 0 => 0, 1 => 100, 2 => 300, 3 => 500, _ => 800 },
    };
}
