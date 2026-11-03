namespace TetrisBot.Game;

public struct ActivePiece { public PieceType Type; public int Rot, X, Y; }

/// <summary>One legal "move" for the bot: which piece (maybe via hold) and where it lands.</summary>
public struct Placement
{
    public PieceType Piece;
    public bool UseHold;
    public Landing Land;
    public PieceType? HoldAfter;   // what sits in the hold slot after this move
    public PieceType NextPiece;    // the next piece the bot will have to play
}

/// <summary>
/// Deterministic Tetris rules engine (SRS, 7-bag, hold, 5-piece preview, modern scoring).
/// Used headless by the AI (Placement API) and in real time by the console UI (TryMove/TryRotate/...).
/// </summary>
public sealed class TetrisGame
{
    public const int PreviewCount = 5;

    public Board Field { get; } = new Board();
    public ActivePiece Current;
    public PieceType? Hold { get; private set; }
    public bool HoldUsed { get; private set; }
    public bool GameOver { get; private set; }
    public long Score { get; private set; }
    public int Lines { get; private set; }
    public int PiecesPlaced { get; private set; }
    public int PieceSerial { get; private set; }
    public int Seed { get; private set; }
    public int StartLevel { get; }
    public bool FixedLevel { get; }
    public int Level => FixedLevel ? StartLevel : StartLevel + Lines / 10;   // no cap
    public ScoreState Scoring;
    public ClearResult LastClear { get; private set; }
    public int ClearSerial { get; private set; }
    public int LastKickFlag { get; private set; }   // 0 = last action wasn't a rotation

    public int TSpins, Tetrises, AllClears, MaxCombo;

    private BagRandomizer _bag;
    private readonly List<PieceType> _queue = new();
    private readonly PlacementSearch _search = new();
    private readonly List<Landing> _landings = new();
    private readonly Board _scratch = new();

    public TetrisGame(int seed, int startLevel = 1, bool fixedLevel = false)
    {
        StartLevel = startLevel;
        FixedLevel = fixedLevel;
        Reset(seed);
    }

    public void Reset(int seed)
    {
        Seed = seed;
        _bag = new BagRandomizer(seed);
        _queue.Clear();
        Field.Clear();
        Hold = null; HoldUsed = false; GameOver = false;
        Score = 0; Lines = 0; PiecesPlaced = 0; PieceSerial = 0; ClearSerial = 0;
        Scoring = default; LastClear = default; LastKickFlag = 0;
        TSpins = Tetrises = AllClears = MaxCombo = 0;
        SpawnNext();
    }

    public void TopOut() => GameOver = true;

    // ---------------- queue ----------------
    private void FillQueue() { while (_queue.Count < PreviewCount + 2) _queue.Add(_bag.Next()); }

    private PieceType Dequeue()
    {
        FillQueue();
        var t = _queue[0];
        _queue.RemoveAt(0);
        FillQueue();
        return t;
    }

    public PieceType PeekNext(int i) { FillQueue(); return _queue[i]; }

    private void SpawnPiece(PieceType t)
    {
        Current = new ActivePiece { Type = t, Rot = 0, X = Rules.SpawnX, Y = Rules.SpawnY };
        LastKickFlag = 0;
        PieceSerial++;
        if (Field.Collides(t, 0, Rules.SpawnX, Rules.SpawnY)) GameOver = true; // block out
    }

    private void SpawnNext() { HoldUsed = false; SpawnPiece(Dequeue()); }

    // ---------------- real-time actions ----------------
    public bool TryMove(int dx, int dy)
    {
        if (GameOver) return false;
        if (Field.Collides(Current.Type, Current.Rot, Current.X + dx, Current.Y + dy)) return false;
        Current.X += dx; Current.Y += dy;
        LastKickFlag = 0;
        return true;
    }

    public bool TryRotate(bool cw)
    {
        if (GameOver || Current.Type == PieceType.O) return false;
        int nr = cw ? (Current.Rot + 1) & 3 : (Current.Rot + 3) & 3;
        for (int i = 0; i < 5; i++)
        {
            var (dx, dy) = PieceData.Kick(Current.Type, Current.Rot, cw, i);
            if (Field.Collides(Current.Type, nr, Current.X + dx, Current.Y + dy)) continue;
            Current.Rot = nr; Current.X += dx; Current.Y += dy;
            LastKickFlag = i == 4 ? 2 : 1;
            return true;
        }
        return false;
    }

    public bool TryHold()
    {
        if (GameOver || HoldUsed) return false;
        var cur = Current.Type;
        if (Hold == null) { Hold = cur; SpawnPiece(Dequeue()); }
        else { var h = Hold.Value; Hold = cur; SpawnPiece(h); }
        HoldUsed = true;
        return true;
    }

    public bool SoftDrop()
    {
        if (!TryMove(0, 1)) return false;
        Score += 1;
        return true;
    }

    public bool IsGrounded() => Field.Collides(Current.Type, Current.Rot, Current.X, Current.Y + 1);

    public int GhostY()
    {
        int y = Current.Y;
        while (!Field.Collides(Current.Type, Current.Rot, Current.X, y + 1)) y++;
        return y;
    }

    public void HardDrop()
    {
        if (GameOver) return;
        int y = GhostY();
        if (y != Current.Y) LastKickFlag = 0;
        Score += 2L * (y - Current.Y);
        Current.Y = y;
        LockCurrent();
    }

    public void LockCurrent()
    {
        if (GameOver) return;
        var spin = SpinKind.None;
        if (Current.Type == PieceType.T && LastKickFlag != 0)
            spin = PlacementSearch.DetectTSpin(Field, Current.Rot, Current.X, Current.Y, LastKickFlag);
        LockPiece(spin);
    }

    private void LockPiece(SpinKind spin)
    {
        bool lockOut = PieceData.IsAllHidden(Current.Type, Current.Rot, Current.Y);
        Field.Place(Current.Type, Current.Rot, Current.X, Current.Y);
        int lines = Field.ClearLines();
        bool allClear = lines > 0 && Field.IsEmpty();
        var res = Scoring.Apply(lines, spin, allClear, Level);

        Score += res.Points;
        Lines += lines;
        PiecesPlaced++;
        LastClear = res;
        ClearSerial++;
        if (spin == SpinKind.Full && Current.Type == PieceType.T) TSpins++;
        if (lines == 4) Tetrises++;
        if (allClear) AllClears++;
        MaxCombo = Math.Max(MaxCombo, res.Combo);

        if (lockOut) GameOver = true; else SpawnNext();
    }

    // ---------------- placement API (for bots) ----------------
    public void GetPlacements(List<Placement> list, SearchOptions opt)
    {
        list.Clear();
        if (GameOver) return;
        Expand(Current.Type, false, opt, list);
        if (!HoldUsed)
        {
            if (Hold.HasValue) { if (Hold.Value != Current.Type) Expand(Hold.Value, true, opt, list); }
            else Expand(PeekNext(0), true, opt, list);
        }
    }

    private void Expand(PieceType type, bool useHold, SearchOptions opt, List<Placement> list)
    {
        _search.Find(Field, type, opt, _landings);
        PieceType? holdAfter;
        PieceType next;
        if (!useHold) { holdAfter = Hold; next = PeekNext(0); }
        else if (Hold.HasValue) { holdAfter = Current.Type; next = PeekNext(0); }
        else { holdAfter = Current.Type; next = PeekNext(1); }

        foreach (var l in _landings)
            list.Add(new Placement { Piece = type, UseHold = useHold, Land = l, HoldAfter = holdAfter, NextPiece = next });
    }

    /// <summary>Plays a placement. Returns the clear result of the lock.</summary>
    public ClearResult Apply(Placement p)
    {
        if (GameOver) return default;
        if (p.UseHold && !TryHold()) return default;
        if (GameOver) return default;
        int dropped = Math.Max(0, p.Land.Y - Current.Y);
        Score += 2L * dropped;                    // hard-drop points, same as a human gets
        Current.Rot = p.Land.Rot; Current.X = p.Land.X; Current.Y = p.Land.Y;
        LockPiece(p.Land.Spin);
        return LastClear;
    }

    /// <summary>
    /// Afterstate simulation: the board (after line clears) that would result from this placement,
    /// without touching the real game. The returned board is a shared scratch buffer.
    /// </summary>
    public Board Simulate(Placement p, out ClearResult clear, out ScoreState newScoring)
    {
        _scratch.CopyFrom(Field);
        _scratch.Place(p.Piece, p.Land.Rot, p.Land.X, p.Land.Y);
        int lines = _scratch.ClearLines();
        bool allClear = lines > 0 && _scratch.IsEmpty();
        newScoring = Scoring;
        clear = newScoring.Apply(lines, p.Land.Spin, allClear, Level);
        return _scratch;
    }
}
