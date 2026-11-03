using System.Globalization;
using TetrisBot.Game;

namespace TetrisBot.AI;

/// <summary>Reward shaping terms (each documented in the README).</summary>
public sealed class RewardConfig
{
    public double ScoreWeight = 1.0;     // reward per 800 base points (a Tetris = 1.0)
    public double SurviveBonus = 0.05;   // small bonus for every piece survived
    public double HolePenalty = 0.1;     // per hole created (negative when holes are removed)
    public double DeathPenalty = -2.0;   // value of a terminal (topped-out) afterstate

    public double Compute(ClearResult c, int holeDelta)
        => ScoreWeight * c.BasePoints / 800.0 + SurviveBonus - HolePenalty * holeDelta;
}

/// <summary>Settings needed to use a saved network the same way it was trained (stored next to the weights).</summary>
public sealed class ModelMeta
{
    public double Gamma = 0.97;
    public RewardConfig Reward = new();
    public bool HardDropOnly = false;

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public void Save(string path) => File.WriteAllLines(path, new[]
    {
        "gamma=" + F(Gamma), "score_weight=" + F(Reward.ScoreWeight), "survive_bonus=" + F(Reward.SurviveBonus),
        "hole_penalty=" + F(Reward.HolePenalty), "death_penalty=" + F(Reward.DeathPenalty),
        "hard_drop_only=" + (HardDropOnly ? "1" : "0"),
    });

    public static ModelMeta Load(string path)
    {
        var m = new ModelMeta();
        if (!File.Exists(path)) return m;
        foreach (var line in File.ReadAllLines(path))
        {
            var kv = line.Split('=');
            if (kv.Length != 2) continue;
            double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
            switch (kv[0])
            {
                case "gamma": m.Gamma = v; break;
                case "score_weight": m.Reward.ScoreWeight = v; break;
                case "survive_bonus": m.Reward.SurviveBonus = v; break;
                case "hole_penalty": m.Reward.HolePenalty = v; break;
                case "death_penalty": m.Reward.DeathPenalty = v; break;
                case "hard_drop_only": m.HardDropOnly = v > 0.5; break;
            }
        }
        return m;
    }
}

public interface IBot
{
    string Name { get; }
    bool HardDropOnly { get; }
    /// <summary>Returns the index of the chosen placement in <paramref name="options"/>.</summary>
    int Choose(TetrisGame game, List<Placement> options);
}

/// <summary>
/// Scores every legal placement: Q = reward + gamma * V(afterstate). Shared by training and play.
/// </summary>
public sealed class PlacementScorer
{
    public readonly FeatureExtractor Fx = new();
    public IValueModel Model;
    public double Gamma;
    public RewardConfig Reward;

    public readonly List<double[]> Feats = new();
    public double[] Q = new double[128];
    public double[] R = new double[128];
    public bool[] Dead = new bool[128];

    public PlacementScorer(IValueModel model, double gamma, RewardConfig reward)
    {
        Model = model; Gamma = gamma; Reward = reward;
    }

    /// <summary>Fills Feats/Q/R/Dead for every option and returns the index of the best Q.</summary>
    public int ScoreAll(TetrisGame g, List<Placement> opts)
    {
        int n = opts.Count;
        while (Feats.Count < n) Feats.Add(new double[FeatureExtractor.Size]);
        if (Q.Length < n)
        {
            Array.Resize(ref Q, n * 2); Array.Resize(ref R, n * 2); Array.Resize(ref Dead, n * 2);
        }

        int holesBefore = Fx.Analyze(g.Field).Holes;
        int best = 0;
        double bestQ = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var p = opts[i];
            var after = g.Simulate(p, out var clr, out var sc);
            var st = Fx.Extract(after, p, clr, sc, Feats[i]);
            R[i] = Reward.Compute(clr, st.Holes - holesBefore);
            Dead[i] = PieceData.IsAllHidden(p.Piece, p.Land.Rot, p.Land.Y);
            Q[i] = Dead[i] ? Reward.DeathPenalty : R[i] + Gamma * Model.Predict(Feats[i]);
            if (Q[i] > bestQ) { bestQ = Q[i]; best = i; }
        }
        return best;
    }
}

/// <summary>Greedy player driven by a trained value network.</summary>
public sealed class NeuralBot : IBot
{
    public string Name { get; }
    public bool HardDropOnly => Meta.HardDropOnly;
    public readonly ModelMeta Meta;
    public readonly PlacementScorer Scorer;

    public NeuralBot(IValueModel model, ModelMeta meta, string name = "Neural")
    {
        Meta = meta; Name = name;
        Scorer = new PlacementScorer(model, meta.Gamma, meta.Reward);
    }

    public int Choose(TetrisGame game, List<Placement> options) => Scorer.ScoreAll(game, options);

    public static NeuralBot FromFile(string weightsPath, string name = "Neural")
        => new NeuralBot(Mlp.Load(weightsPath), ModelMeta.Load(weightsPath + ".meta"), name);
}

/// <summary>Baseline: uniformly random placement.</summary>
public sealed class RandomBot : IBot
{
    private readonly Random _rng;
    public RandomBot(int seed = 1) { _rng = new Random(seed); }
    public string Name => "Random";
    public bool HardDropOnly => false;
    public int Choose(TetrisGame game, List<Placement> options) => _rng.Next(options.Count);
}

/// <summary>Baseline: hand-tuned El-Tetris weights (height, lines, holes, bumpiness).</summary>
public sealed class HeuristicBot : IBot
{
    private readonly FeatureExtractor _fx = new();
    public string Name => "Heuristic";
    public bool HardDropOnly => false;

    public int Choose(TetrisGame game, List<Placement> options)
    {
        int best = 0;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < options.Count; i++)
        {
            var after = game.Simulate(options[i], out var clr, out _);
            var st = _fx.Analyze(after);
            double s = -0.510066 * st.AggHeight + 0.760666 * clr.Lines - 0.35663 * st.Holes - 0.184483 * st.Bumpiness;
            if (s > bestScore) { bestScore = s; best = i; }
        }
        return best;
    }
}
