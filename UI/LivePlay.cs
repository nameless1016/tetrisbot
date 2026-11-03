using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using TetrisBot.AI;
using TetrisBot.Game;

namespace TetrisBot.UI;

public enum PlayMode { Sprint40, Ultra, Endless }

/// <summary>Applies gravity, lock delay and move-reset rules to a game in real time (human play).</summary>
public sealed class RealTimeDriver
{
    private readonly TetrisGame _g;
    private int _serial = -1, _resets, _lowestY;
    private double _gravAcc, _lockTimer;

    public RealTimeDriver(TetrisGame g) { _g = g; }

    private void Sync()
    {
        if (_g.PieceSerial == _serial) return;
        _serial = _g.PieceSerial;
        _gravAcc = 0; _lockTimer = 0; _resets = 0; _lowestY = _g.Current.Y;
    }

    private void TrackLowest()
    {
        if (_g.Current.Y > _lowestY) { _lowestY = _g.Current.Y; _resets = 0; }
    }

    public void Update(double dt)
    {
        if (_g.GameOver) return;
        Sync();
        double spr = Rules.SecondsPerRow(_g.Level);
        if (spr <= 0) { while (_g.TryMove(0, 1)) { } }
        else
        {
            _gravAcc += dt;
            while (_gravAcc >= spr)
            {
                _gravAcc -= spr;
                if (!_g.TryMove(0, 1)) { _gravAcc = 0; break; }
            }
        }
        TrackLowest();
        if (_g.IsGrounded())
        {
            _lockTimer += dt;
            if (_lockTimer >= Rules.LockDelaySeconds(_g.Level)) _g.LockCurrent();
        }
        else _lockTimer = 0;
    }

    /// <summary>Call after every player action; resets the lock timer (max 15 times per piece).</summary>
    public void AfterAction(bool succeeded)
    {
        if (!succeeded || _g.GameOver) return;
        Sync();
        TrackLowest();
        if (!_g.IsGrounded()) _lockTimer = 0;
        else if (_resets < Rules.MaxLockResets) { _lockTimer = 0; _resets++; }
    }
}

/// <summary>Real-time console game: you, the bot, or both side by side on the same piece sequence.</summary>
public sealed class LivePlay
{
    private readonly PlayMode _mode;
    private readonly bool _human;
    private readonly IBot _bot;
    private readonly double _botPps;

    private TetrisGame _hg, _bg;
    private RealTimeDriver _drv;
    private readonly List<Placement> _opts = new();
    private string _hMsg = "", _bMsg = "";
    private double _hMsgUntil, _bMsgUntil;
    private int _hSerial, _bSerial;
    private bool _hDone, _bDone;
    private double _hTime, _bTime;

    private const double UltraSeconds = 120.0;
    private const int SprintLines = 40;

    public LivePlay(PlayMode mode, bool human, IBot bot, double botPps)
    {
        _mode = mode; 
        _human = human; 
        _bot = bot; 
        _botPps = Math.Max(0.1, botPps);
    }

    private bool IsDone(TetrisGame g, double now)
        => g.GameOver
           || (_mode == PlayMode.Sprint40 && g.Lines >= SprintLines)
           || (_mode == PlayMode.Ultra && now >= UltraSeconds);

    public void Run(int seed)
    {
        bool fixedLevel = _mode == PlayMode.Sprint40;
        if (_human) { _hg = new TetrisGame(seed, 1, fixedLevel); _drv = new RealTimeDriver(_hg); }
        if (_bot != null) _bg = new TetrisGame(seed, 1, fixedLevel);

        Console.CursorVisible = false;
        Console.Clear();
        for (int i = 3; i >= 1; i--)
        {
            Console.SetCursorPosition(0, 0);
            Console.WriteLine($"Seed {seed} - get ready... {i}   ");
            Thread.Sleep(800);
        }
        Console.Clear();

        var sw = Stopwatch.StartNew();
        double last = 0, botAcc = 0;
        bool quit = false;

        while (!quit)
        {
            double now = sw.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, now - last);
            last = now;

            if (_human && !_hDone)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape) { quit = true; break; }
                    HandleKey(key);
                }
                _drv.Update(dt);
                if (IsDone(_hg, now)) { _hDone = true; _hTime = now; }
            }

            if (_bot != null && !_bDone)
            {
                botAcc += dt;
                double step = 1.0 / _botPps;
                while (botAcc >= step && !_bDone)
                {
                    botAcc -= step;
                    BotStep();
                    if (IsDone(_bg, now)) { _bDone = true; _bTime = _bg.PiecesPlaced / _botPps; }
                }
                if (!_bDone && _mode == PlayMode.Ultra && now >= UltraSeconds) { _bDone = true; _bTime = now; }
            }

            Render(now);
            if ((!_human || _hDone) && (_bot == null || _bDone)) break;
            Thread.Sleep(4);
        }

        ShowResult(quit);
        Console.CursorVisible = true;
    }

    private void HandleKey(ConsoleKeyInfo k)
    {
        bool moved = false;
        switch (k.Key)
        {
            case ConsoleKey.LeftArrow: case ConsoleKey.A: moved = _hg.TryMove(-1, 0); break;
            case ConsoleKey.RightArrow: case ConsoleKey.D: moved = _hg.TryMove(1, 0); break;
            case ConsoleKey.DownArrow: case ConsoleKey.S: moved = _hg.SoftDrop(); break;
            case ConsoleKey.UpArrow: case ConsoleKey.X: moved = _hg.TryRotate(true); break;
            case ConsoleKey.Z: moved = _hg.TryRotate(false); break;
            case ConsoleKey.Spacebar: _hg.HardDrop(); return;
            case ConsoleKey.C: moved = _hg.TryHold(); break;
        }
        _drv.AfterAction(moved);
    }

    private void BotStep()
    {
        var so = SearchOptions.ForLevel(_bg.Level, _bot.HardDropOnly);
        _bg.GetPlacements(_opts, so);
        if (_opts.Count == 0) { _bg.TopOut(); return; }
        _bg.Apply(_opts[_bot.Choose(_bg, _opts)]);
    }

    private static void UpdateMsg(TetrisGame g, ref int serial, ref string msg, ref double until, double now)
    {
        if (g.ClearSerial == serial) return;
        serial = g.ClearSerial;
        string t = ConsoleRenderer.ClearText(g.LastClear);
        if (t.Length > 0) { msg = t; until = now + 2.0; }
    }

    private void Render(double now)
    {
        List<string> left = null, right = null;
        if (_human)
        {
            UpdateMsg(_hg, ref _hSerial, ref _hMsg, ref _hMsgUntil, now);
            left = ConsoleRenderer.Panel(_hg, "YOU", _hDone ? _hTime : now, now < _hMsgUntil ? _hMsg : "");
        }
        if (_bot != null)
        {
            UpdateMsg(_bg, ref _bSerial, ref _bMsg, ref _bMsgUntil, now);
            right = ConsoleRenderer.Panel(_bg, "BOT (" + _bot.Name + ", " + _botPps.ToString("F1") + " PPS)", _bDone ? _bTime : now, now < _bMsgUntil ? _bMsg : "");
        }

        int rows = Math.Max(left?.Count ?? 0, right?.Count ?? 0);
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            string line = (left != null ? left[r] : "") + (left != null && right != null ? "   " : "") + (right != null ? right[r] : "");
            sb.AppendLine(ConsoleRenderer.Pad(line, 100));
        }
        sb.AppendLine(ConsoleRenderer.Pad(ModeText(), 100));
        if (_human) sb.AppendLine(ConsoleRenderer.Pad("Arrows/WASD move+soft drop | Up/X rotate CW | Z rotate CCW | Space hard drop | C hold | Esc quit", 100));
        Console.SetCursorPosition(0, 0);
        Console.Write(sb.ToString());
    }

    private string ModeText() => _mode switch
    {
        PlayMode.Sprint40 => "MODE: 40-line sprint (fixed speed) - clear 40 lines as fast as possible",
        PlayMode.Ultra => "MODE: Ultra - highest score in 2:00",
        _ => "MODE: Endless - speed ramps up forever (20G, then lock delay shrinks to 0)",
    };

    private string Describe(string who, TetrisGame g, double time)
    {
        string status = g.GameOver ? "TOPPED OUT"
            : _mode == PlayMode.Sprint40 && g.Lines >= SprintLines ? $"CLEARED 40L in {time:F2}s"
            : "time up / quit";
        double pps = time > 0 ? g.PiecesPlaced / time : 0;
        return $"{who,-6} {status} | pieces {g.PiecesPlaced} | lines {g.Lines} | score {g.Score} | PPS {pps:F2} | T-spins {g.TSpins} | tetrises {g.Tetrises}";
    }

    private void ShowResult(bool quit)
    {
        Console.SetCursorPosition(0, 0);
        Console.Clear();
        Console.WriteLine("===== RESULT =====");
        if (_human) Console.WriteLine(Describe("YOU", _hg, _hDone ? _hTime : 0.001));
        if (_bot != null) Console.WriteLine(Describe("BOT", _bg, _bDone ? _bTime : 0.001));

        if (_human && _bot != null && !quit)
        {
            bool hc = _hg.Lines >= SprintLines, bc = _bg.Lines >= SprintLines;
            string winner;
            if (_mode == PlayMode.Sprint40)
                winner = hc && bc ? (_hTime <= _bTime ? "YOU (faster)" : "BOT (faster)")
                       : hc ? "YOU" : bc ? "BOT" : (_hg.Lines >= _bg.Lines ? "YOU (more lines)" : "BOT (more lines)");
            else winner = _hg.Score >= _bg.Score ? "YOU (higher score)" : "BOT (higher score)";
            Console.WriteLine($"\nWinner: {winner}");
        }
        Console.WriteLine("\nPress any key...");
        while (Console.KeyAvailable) Console.ReadKey(true);
        Console.ReadKey(true);
    }
}