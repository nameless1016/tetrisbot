using System.Text;
using TetrisBot.Game;

namespace TetrisBot.AI;

public sealed class RunResult
{
    public int Seed, Pieces, Lines, Level, TSpins, Tetrises, AllClears;
    public long Score;
    public bool Cleared, ToppedOut;
    public double SimSeconds;
}

/// <summary>
/// Benchmarks on FIXED held-out seeds (never used for training):
///  - Sprint: clear 40 lines (pieces, simulated time)
///  - Score attack: best score within a fixed piece budget (= 2 minutes at the assumed PPS)
///  - Survival: endless mode with the speed ramp (20G + shrinking lock delay) until top-out
/// Simulated time = pieces / PPS, so results do not depend on the host machine.
/// </summary>
public static class Evaluator
{
    public const int EvalSeedBase = 50000;    // benchmark seeds   50000..
    public const int QuickSeedBase = 90000;   // in-training eval  90000..   (training uses 100000+)

    private static RunResult Run(IBot bot, TetrisGame g, int maxPieces, Func<TetrisGame, bool> done, bool gravityAware)
    {
        var opts = new List<Placement>();
        while (!g.GameOver && g.PiecesPlaced < maxPieces && !done(g))
        {
            var so = gravityAware
                ? SearchOptions.ForLevel(g.Level, bot.HardDropOnly)
                : (bot.HardDropOnly ? SearchOptions.HardDrop : SearchOptions.Full);
            g.GetPlacements(opts, so);
            if (opts.Count == 0) { g.TopOut(); break; }
            g.Apply(opts[bot.Choose(g, opts)]);
        }
        return new RunResult
        {
            Seed = g.Seed, Cleared = done(g), ToppedOut = g.GameOver, Pieces = g.PiecesPlaced, Lines = g.Lines,
            Level = g.Level, Score = g.Score, TSpins = g.TSpins, Tetrises = g.Tetrises, AllClears = g.AllClears,
        };
    }

    public static RunResult PlaySprint(IBot bot, int seed, int target = 40, int maxPieces = 300)
        => Run(bot, new TetrisGame(seed, 1, true), maxPieces, x => x.Lines >= target, false);

    public static RunResult PlayScoreAttack(IBot bot, int seed, int pieces)
        => Run(bot, new TetrisGame(seed, 1, false), pieces, _ => false, false);

    public static RunResult PlaySurvival(IBot bot, int seed, int maxPieces)
        => Run(bot, new TetrisGame(seed, 1, false), maxPieces, _ => false, true);

    /// <summary>Cheap greedy evaluation used for the learning curve during training.</summary>
    public static void QuickEval(IBot bot, int seeds, int pieces, out double meanLines, out double meanScore)
    {
        double l = 0, s = 0;
        for (int i = 0; i < seeds; i++)
        {
            var r = PlayScoreAttack(bot, QuickSeedBase + i, pieces);
            l += r.Lines; s += r.Score;
        }
        meanLines = l / seeds; meanScore = s / seeds;
    }

    // ------------------------------------------------------------------ reporting
    private static (double m, double s) MeanSd(List<double> v)
    {
        if (v.Count == 0) return (double.NaN, double.NaN);
        double m = v.Average();
        double var2 = v.Count > 1 ? v.Sum(x => (x - m) * (x - m)) / (v.Count - 1) : 0.0;
        return (m, Math.Sqrt(var2));
    }

    private static string Fmt((double m, double s) t, string f = "F1")
        => double.IsNaN(t.m) ? "-" : t.m.ToString(f) + "±" + t.s.ToString(f);

    public static void RunBenchmarks(List<IBot> bots, int seeds, double pps, string csvPath)
    {
        int budget = (int)(120 * pps);
        const int survivalCap = 1500;
        var csv = new StringBuilder("bot,mode,seed,cleared,topped_out,pieces,lines,level,score,sim_seconds,tspins,tetrises,all_clears\n");

        void Row(string bot, string mode, RunResult r)
            => csv.AppendLine($"{bot},{mode},{r.Seed},{(r.Cleared ? 1 : 0)},{(r.ToppedOut ? 1 : 0)},{r.Pieces},{r.Lines},{r.Level},{r.Score},{r.Pieces / pps:F2},{r.TSpins},{r.Tetrises},{r.AllClears}");

        Console.WriteLine($"Benchmarks: {seeds} fixed seeds ({EvalSeedBase}..{EvalSeedBase + seeds - 1}), assumed speed {pps:F1} PPS (sim time = pieces / PPS)\n");

        Console.WriteLine("== 40-LINE SPRINT ==");
        Console.WriteLine($"{"bot",-12}{"cleared",9}{"pieces",14}{"sim time (s)",16}{"pieces/line",13}");
        foreach (var bot in bots)
        {
            var res = new List<RunResult>();
            for (int i = 0; i < seeds; i++) { var r = PlaySprint(bot, EvalSeedBase + i); res.Add(r); Row(bot.Name, "sprint40", r); }
            var ok = res.Where(r => r.Cleared).ToList();
            Console.WriteLine($"{bot.Name,-12}{100.0 * ok.Count / seeds,8:F0}%{Fmt(MeanSd(ok.Select(r => (double)r.Pieces).ToList())),14}" +
                              $"{Fmt(MeanSd(ok.Select(r => r.Pieces / pps).ToList())),16}{Fmt(MeanSd(ok.Select(r => r.Pieces / 40.0).ToList()), "F2"),13}");
        }

        Console.WriteLine($"\n== SCORE ATTACK ({budget} pieces = 2:00 at {pps:F1} PPS) ==");
        Console.WriteLine($"{"bot",-12}{"score",20}{"lines",14}{"T-spins",12}{"tetrises",12}{"topped out",12}");
        foreach (var bot in bots)
        {
            var res = new List<RunResult>();
            for (int i = 0; i < seeds; i++) { var r = PlayScoreAttack(bot, EvalSeedBase + i, budget); res.Add(r); Row(bot.Name, "score_attack", r); }
            Console.WriteLine($"{bot.Name,-12}{Fmt(MeanSd(res.Select(r => (double)r.Score).ToList()), "F0"),20}{Fmt(MeanSd(res.Select(r => (double)r.Lines).ToList())),14}" +
                              $"{Fmt(MeanSd(res.Select(r => (double)r.TSpins).ToList())),12}{Fmt(MeanSd(res.Select(r => (double)r.Tetrises).ToList())),12}{100.0 * res.Count(r => r.ToppedOut) / seeds,11:F0}%");
        }

        Console.WriteLine($"\n== SURVIVAL (endless, speed ramps up forever, cap {survivalCap} pieces) ==");
        Console.WriteLine($"{"bot",-12}{"pieces",16}{"lines",14}{"level",12}{"score",20}{"topped out",12}");
        foreach (var bot in bots)
        {
            var res = new List<RunResult>();
            for (int i = 0; i < seeds; i++) { var r = PlaySurvival(bot, EvalSeedBase + i, survivalCap); res.Add(r); Row(bot.Name, "survival", r); }
            Console.WriteLine($"{bot.Name,-12}{Fmt(MeanSd(res.Select(r => (double)r.Pieces).ToList()), "F0"),16}{Fmt(MeanSd(res.Select(r => (double)r.Lines).ToList())),14}" +
                              $"{Fmt(MeanSd(res.Select(r => (double)r.Level).ToList())),12}{Fmt(MeanSd(res.Select(r => (double)r.Score).ToList()), "F0"),20}{100.0 * res.Count(r => r.ToppedOut) / seeds,11:F0}%");
        }

        File.WriteAllText(csvPath, csv.ToString());
        Console.WriteLine($"\nPer-run results written to {csvPath}");
    }
}
