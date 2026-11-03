using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TetrisBot.AI;
using TetrisBot.UI;

namespace TetrisBot;

public sealed class CliArgs
{
    private readonly Dictionary<string, string> _o = new();
    public string Command;

    public CliArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string s = args[i];
            if (s.StartsWith("--"))
            {
                string key = s.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) _o[key] = args[++i];
                else _o[key] = "true";
            }
            else if (Command == null) Command = s;
        }
    }

    public string Str(string k, string d) => _o.TryGetValue(k, out var v) ? v : d;
    public int Int(string k, int d) => _o.TryGetValue(k, out var v) && int.TryParse(v, out var r) ? r : d;
    public double Dbl(string k, double d)
        => _o.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : d;
    public bool Flag(string k) => _o.ContainsKey(k);
}

public static class Program
{
    private const string Usage = @"
TetrisBot - reinforcement-learning Tetris bot (C#, custom MLP)

  train       Train the bot            --episodes 3000 --hidden 64 --lr 0.003 --gamma 0.97 --seed 1
                                       --hard-drop (disable spin search) --out weights.txt --log training_log.csv
  experiment  Hyperparameter sweeps    --episodes 600 --seed 1 --out experiments
  eval        Benchmark vs baselines   --weights weights.txt --seeds 50 --pps 3.0 --csv benchmark_results.csv
  watch       Watch the bot play       --weights weights.txt --bot-pps 3 --mode sprint|ultra|endless --seed N
  play        You play (console)       --mode sprint|ultra|endless --seed N
  duel        You vs the bot, same pieces  --weights weights.txt --bot-pps 2 --mode sprint|ultra|endless --seed N
  (add --mono for plain ASCII if colors/box characters look wrong)
";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var a = new CliArgs(args);
        ConsoleRenderer.UseColor = !a.Flag("mono");
        string cmd = a.Command ?? Menu();
        if (cmd == null) return 0;

        string weights = a.Str("weights", "weights.txt");
        var mode = ParseMode(a.Str("mode", "sprint"));
        int seed = a.Int("seed", new Random().Next(1, 49000));

        switch (cmd.ToLowerInvariant())
        {
            case "train":
                Trainer.Train(new TrainConfig
                {
                    Episodes = a.Int("episodes", 3000), 
                    Hidden = a.Int("hidden", 64),
                    LearningRate = a.Dbl("lr", 0.003), 
                    Gamma = a.Dbl("gamma", 0.97), 
                    Seed = a.Int("seed", 1),
                    HardDropOnly = a.Flag("hard-drop"), 
                    WeightsPath = a.Str("out", "weights.txt"),
                    LogPath = a.Str("log", "training_log.csv"),
                    LogEvery = a.Int("log-every", 100), 
                    EvalEvery = a.Int("eval-every", 250),
                });
                return 0;

            case "experiment":
                ExperimentRunner.Run(a.Int("episodes", 600), a.Int("seed", 1), a.Str("out", "experiments"));
                return 0;

            case "eval":
            {
                var bots = new List<IBot>();
                if (File.Exists(weights)) bots.Add(NeuralBot.FromFile(weights));
                else Console.WriteLine($"(no weights at '{weights}' - run 'train' first; evaluating baselines only)");
                bots.Add(new HeuristicBot());
                bots.Add(new RandomBot());
                Evaluator.RunBenchmarks(bots, a.Int("seeds", 50), a.Dbl("pps", 3.0), a.Str("csv", "benchmark_results.csv"));
                return 0;
            }

            case "watch":
                new LivePlay(mode, false, LoadBot(weights), a.Dbl("bot-pps", 3.0)).Run(seed);
                return 0;

            case "play":
                new LivePlay(mode, true, null, 1).Run(seed);
                return 0;

            case "duel":
                new LivePlay(mode, true, LoadBot(weights), a.Dbl("bot-pps", 2.0)).Run(seed);
                return 0;

            default:
                Console.WriteLine(Usage);
                return 1;
        }
    }

    private static PlayMode ParseMode(string s) => s.ToLowerInvariant() switch
    {
        "ultra" => PlayMode.Ultra,
        "endless" => PlayMode.Endless,
        _ => PlayMode.Sprint40,
    };

    private static IBot LoadBot(string weights)
    {
        if (File.Exists(weights)) return NeuralBot.FromFile(weights);
        Console.WriteLine($"Weights '{weights}' not found - using the heuristic bot instead. Train first with: train");
        Thread.Sleep(1500);
        return new HeuristicBot();
    }

    private static string Menu()
    {
        Console.WriteLine(Usage);
        Console.WriteLine("Pick: 1) train  2) eval  3) watch bot  4) play  5) duel  6) experiment  (anything else = quit)");
        Console.Write("> ");
        return Console.ReadLine()?.Trim() switch
        {
            "1" => "train", 
            "2" => "eval", 
            "3" => "watch", 
            "4" => "play", 
            "5" => "duel", 
            "6" => "experiment",
            _ => null,
        };
    }
}