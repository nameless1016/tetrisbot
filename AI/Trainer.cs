using System.Diagnostics;
using System.Text;
using TetrisBot.Game;

namespace TetrisBot.AI;

public sealed class TrainConfig
{
    // --- network / learning ---
    public int Hidden = 64;
    public double LearningRate = 0.003;
    public double Gamma = 0.97;
    public int BatchSize = 16;            // replay samples trained per environment step
    public int ReplayCapacity = 30000;
    // --- exploration (epsilon-greedy, linear decay) ---
    public double EpsStart = 1.0, EpsEnd = 0.03, EpsDecayFraction = 0.7;
    // --- environment ---
    public int Episodes = 3000;
    public int MaxPiecesPerEpisode = 400;
    public bool HardDropOnly = false;     // false = full BFS search incl. tucks and T-spins
    public RewardConfig Reward = new();
    public int Seed = 1;
    // --- logging / evaluation ---
    public int LogEvery = 100;
    public int EvalEvery = 250;
    public int EvalSeeds = 10;
    public int EvalPieces = 150;
    public string LogPath = "training_log.csv";
    public string WeightsPath = "weights.txt";
    public bool Quiet = false;
}

public sealed class TrainSummary
{
    public double FinalTrainLines, BestEvalLines, FinalEvalLines, FinalEvalScore, Seconds;
    public string WeightsPath;
}

public sealed class Transition
{
    public double[] F, NextF;
    public double NextReward;
    public bool Terminal;
}

/// <summary>Fixed-size ring buffer of (afterstate, best-next-afterstate) pairs.</summary>
public sealed class ReplayBuffer
{
    private readonly Transition[] _buf;
    private readonly Random _rng;
    private int _next, _count;
    public int Count => _count;

    public ReplayBuffer(int capacity, int dim, Random rng)
    {
        _rng = rng;
        _buf = new Transition[capacity];
        for (int i = 0; i < capacity; i++) _buf[i] = new Transition { F = new double[dim], NextF = new double[dim] };
    }

    public void Add(double[] f, double[] nextF, double nextReward, bool terminal)
    {
        var t = _buf[_next];
        Array.Copy(f, t.F, f.Length);
        if (nextF != null) Array.Copy(nextF, t.NextF, nextF.Length);
        t.NextReward = nextReward;
        t.Terminal = terminal;
        _next = (_next + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
    }

    public Transition Sample() => _buf[_rng.Next(_count)];
}

/// <summary>
/// Afterstate TD learning. For every decision the agent scores all legal placements,
/// Q = reward + gamma * V(afterstate), and (epsilon-)greedily plays the best one.
/// The network V is regressed toward:  y = r_next + gamma * V(next afterstate)  (or DeathPenalty if terminal),
/// with the target recomputed from the CURRENT weights at replay time (only the argmax is stored).
/// </summary>
public static class Trainer
{
    public const int TrainSeedBase = 100000;

    public static TrainSummary Train(TrainConfig cfg)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(cfg.Seed);
        var model = new Mlp(FeatureExtractor.Size, cfg.Hidden, cfg.Seed);
        var meta = new ModelMeta { Gamma = cfg.Gamma, Reward = cfg.Reward, HardDropOnly = cfg.HardDropOnly };
        var scorer = new PlacementScorer(model, cfg.Gamma, cfg.Reward);
        var evalBot = new NeuralBot(model, meta);
        var replay = new ReplayBuffer(cfg.ReplayCapacity, FeatureExtractor.Size, rng);
        var game = new TetrisGame(TrainSeedBase);
        var opts = new List<Placement>();
        var so = cfg.HardDropOnly ? SearchOptions.HardDrop : SearchOptions.Full;
        var prevF = new double[FeatureExtractor.Size];

        string dir = Path.GetDirectoryName(Path.GetFullPath(cfg.LogPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var log = new StreamWriter(cfg.LogPath, false, new UTF8Encoding(false));
        log.WriteLine("episode,epsilon,avg_loss,avg_reward,avg_pieces,avg_lines,avg_score,eval_lines,eval_score");

        double wLoss = 0, wReward = 0, wPieces = 0, wLines = 0, wScore = 0;
        int wCount = 0;
        double bestEval = double.NegativeInfinity, lastEvalLines = 0, lastEvalScore = 0, lastTrainLines = 0;

        if (!cfg.Quiet)
        {
            Console.WriteLine($"Training: {cfg.Episodes} episodes | hidden={cfg.Hidden} lr={cfg.LearningRate} gamma={cfg.Gamma} " +
                              $"search={(cfg.HardDropOnly ? "hard-drop" : "full (spins)")}");
            Console.WriteLine("episode | epsilon |   loss | reward | pieces | lines |  score | eval lines");
        }

        for (int ep = 1; ep <= cfg.Episodes; ep++)
        {
            double frac = Math.Min(1.0, ep / Math.Max(1.0, cfg.Episodes * cfg.EpsDecayFraction));
            double eps = cfg.EpsStart + (cfg.EpsEnd - cfg.EpsStart) * frac;

            game.Reset(TrainSeedBase + ep);
            bool havePrev = false;
            double epLoss = 0, epReward = 0;
            int lossN = 0;

            while (true)
            {
                game.GetPlacements(opts, so);
                if (opts.Count == 0 || game.GameOver)
                {
                    if (havePrev) replay.Add(prevF, null, 0, true);
                    break;
                }

                int best = scorer.ScoreAll(game, opts);

                // finalize the previous transition: its target is the best Q available from this state
                if (havePrev)
                {
                    if (scorer.Dead[best]) replay.Add(prevF, null, 0, true);
                    else replay.Add(prevF, scorer.Feats[best], scorer.R[best], false);
                }

                int a = rng.NextDouble() < eps ? rng.Next(opts.Count) : best;
                Array.Copy(scorer.Feats[a], prevF, prevF.Length);
                havePrev = true;
                epReward += scorer.R[a];
                game.Apply(opts[a]);

                if (replay.Count >= cfg.BatchSize)
                {
                    epLoss += TrainBatch(model, replay, cfg);
                    lossN++;
                }
                if (game.PiecesPlaced >= cfg.MaxPiecesPerEpisode) break;
            }

            wLoss += lossN > 0 ? epLoss / lossN : 0; wReward += epReward;
            wPieces += game.PiecesPlaced; wLines += game.Lines; wScore += game.Score; wCount++;

            bool logNow = ep % cfg.LogEvery == 0 || ep == cfg.Episodes;
            if (!logNow) continue;

            string evalL = "", evalS = "";
            if (ep % cfg.EvalEvery == 0 || ep == cfg.Episodes)
            {
                Evaluator.QuickEval(evalBot, cfg.EvalSeeds, cfg.EvalPieces, out lastEvalLines, out lastEvalScore);
                evalL = lastEvalLines.ToString("F2"); evalS = lastEvalScore.ToString("F0");
                if (lastEvalLines > bestEval)
                {
                    bestEval = lastEvalLines;
                    model.Save(cfg.WeightsPath);
                    meta.Save(cfg.WeightsPath + ".meta");
                }
            }

            lastTrainLines = wLines / wCount;
            log.WriteLine($"{ep},{eps:F4},{wLoss / wCount:F5},{wReward / wCount:F3},{wPieces / wCount:F1},{lastTrainLines:F2},{wScore / wCount:F0},{evalL},{evalS}");
            log.Flush();
            if (!cfg.Quiet)
                Console.WriteLine($"{ep,7} | {eps,7:F3} | {wLoss / wCount,6:F4} | {wReward / wCount,6:F2} | {wPieces / wCount,6:F1} | {lastTrainLines,5:F1} | {wScore / wCount,6:F0} | {(evalL == "" ? "-" : evalL)}");
            wLoss = wReward = wPieces = wLines = wScore = 0; wCount = 0;
        }

        model.Save(Path.ChangeExtension(cfg.WeightsPath, ".last.txt"));
        if (!File.Exists(cfg.WeightsPath)) { model.Save(cfg.WeightsPath); meta.Save(cfg.WeightsPath + ".meta"); }
        if (!cfg.Quiet) Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F0}s. Best weights: {cfg.WeightsPath} (eval lines {bestEval:F2}); log: {cfg.LogPath}");

        return new TrainSummary
        {
            FinalTrainLines = lastTrainLines, BestEvalLines = bestEval, FinalEvalLines = lastEvalLines,
            FinalEvalScore = lastEvalScore, Seconds = sw.Elapsed.TotalSeconds, WeightsPath = cfg.WeightsPath,
        };
    }

    private static double TrainBatch(IValueModel model, ReplayBuffer replay, TrainConfig cfg)
    {
        double loss = 0;
        for (int k = 0; k < cfg.BatchSize; k++)
        {
            var t = replay.Sample();
            double y = t.Terminal ? cfg.Reward.DeathPenalty : t.NextReward + cfg.Gamma * model.Predict(t.NextF);
            loss += model.TrainSample(t.F, y, cfg.LearningRate);
        }
        return loss / cfg.BatchSize;
    }
}

/// <summary>One-factor-at-a-time sweeps around the default config; results go to a CSV for the report.</summary>
public static class ExperimentRunner
{
    public static void Run(int episodes, int seed, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var runs = new List<(string group, string value, Action<TrainConfig> apply)>
        {
            ("baseline", "default", c => { }),
            ("learning_rate", "0.0005", c => c.LearningRate = 0.0005),
            ("learning_rate", "0.001", c => c.LearningRate = 0.001),
            ("learning_rate", "0.01", c => c.LearningRate = 0.01),
            ("hidden", "16", c => c.Hidden = 16),
            ("hidden", "32", c => c.Hidden = 32),
            ("hidden", "128", c => c.Hidden = 128),
            ("gamma", "0.90", c => c.Gamma = 0.90),
            ("gamma", "0.95", c => c.Gamma = 0.95),
            ("gamma", "0.99", c => c.Gamma = 0.99),
            ("hole_penalty", "0", c => c.Reward.HolePenalty = 0.0),
            ("hole_penalty", "0.3", c => c.Reward.HolePenalty = 0.3),
            ("eps_decay_fraction", "0.3", c => c.EpsDecayFraction = 0.3),
            ("eps_decay_fraction", "0.9", c => c.EpsDecayFraction = 0.9),
            ("search", "hard_drop_only", c => c.HardDropOnly = true),
        };

        var csv = new StringBuilder("group,value,episodes,final_eval_lines,final_eval_score,best_eval_lines,final_train_lines,seconds\n");
        Console.WriteLine($"Running {runs.Count} experiments x {episodes} episodes (seed {seed}). This takes a while.\n");
        Console.WriteLine($"{"group",-20}{"value",-16}{"eval lines",12}{"eval score",12}{"best lines",12}{"time(s)",9}");

        foreach (var (group, value, apply) in runs)
        {
            string name = $"{group}_{value}".Replace('.', 'p');
            var cfg = new TrainConfig
            {
                Episodes = episodes, Seed = seed, Quiet = true,
                LogEvery = Math.Max(10, episodes / 20), EvalEvery = Math.Max(10, episodes / 4),
                EvalSeeds = 20, EvalPieces = 150,
                LogPath = Path.Combine(outDir, name + "_log.csv"),
                WeightsPath = Path.Combine(outDir, name + ".txt"),
            };
            apply(cfg);
            var s = Trainer.Train(cfg);
            csv.AppendLine($"{group},{value},{episodes},{s.FinalEvalLines:F2},{s.FinalEvalScore:F0},{s.BestEvalLines:F2},{s.FinalTrainLines:F2},{s.Seconds:F0}");
            Console.WriteLine($"{group,-20}{value,-16}{s.FinalEvalLines,12:F2}{s.FinalEvalScore,12:F0}{s.BestEvalLines,12:F2}{s.Seconds,9:F0}");
        }

        string csvPath = Path.Combine(outDir, "experiments.csv");
        File.WriteAllText(csvPath, csv.ToString());
        Console.WriteLine($"\nSummary written to {csvPath}");
    }
}
