# TetrisBot — Reinforcement-Learning Tetris in C# (custom MLP)

A guideline-style Tetris engine plus a bot that learns to play it with **afterstate TD learning**
(a DQN-style value network + experience replay), written in plain C# / .NET 8 with no ML libraries.
You can watch the bot, play yourself in the console, or race the bot on the **same piece sequence**.

## Quick start

```bash
dotnet build -c Release
dotnet run -c Release -- train --episodes 3000          # writes weights.txt + training_log.csv
dotnet run -c Release -- eval --seeds 50                # benchmarks vs. heuristic & random bots
dotnet run -c Release -- watch --bot-pps 3              # watch the bot
dotnet run -c Release -- play --mode sprint             # you play
dotnet run -c Release -- duel --mode sprint --bot-pps 2 # you vs. the bot, same seed
dotnet run -c Release -- experiment --episodes 600      # hyperparameter sweeps -> experiments/experiments.csv
dotnet run -c Release                                   # interactive menu
```

Console needs about 100 columns x 32 rows. Use Windows Terminal / any ANSI terminal for colors,
or add `--mono` for plain ASCII. Modes: `sprint` (40 lines, fixed speed), `ultra` (best score in 2:00),
`endless` (speed ramps up forever).

Controls: Left/Right (or A/D) move · Down (or S) soft drop · Up/X rotate CW · Z rotate CCW ·
Space hard drop · C hold · Esc quit. (Key repeat comes from the OS, so DAS/ARR are not tunable.)

## Game rules implemented

SRS rotation and full wall-kick tables (separate I table), 7-bag with seeded RNG, hold, 5-piece preview,
ghost piece, lock delay with a 15-reset limit, block-out / lock-out top-out, 10x40 field (20 hidden rows).

Scoring: single/double/triple/Tetris, T-spin and T-spin mini (3-corner rule, 5th-kick upgrade),
back-to-back x1.5, combo (50 x combo x level), all-clear bonuses, soft drop 1/cell, hard drop 2/cell, level multiplier.

Speed: guideline gravity curve until level 20 (= 20G, instant), then the **lock delay shrinks by 25 ms per
level until it hits 0 at level 40**. Level rises every 10 lines with no cap. In `sprint` the level is fixed.

## How the bot works

**Move generation.** For the current piece (and the hold piece) a BFS over `(x, y, rotation, last-move-was-rotation)`
finds every reachable resting position using real SRS kicks, so tucks, slides and T-spins are all candidate moves
(~35-70 placements per decision). `--hard-drop` restricts this to straight drops for faster training.

**Value network.** An MLP (35 inputs -> hidden tanh -> 1 linear output) estimates the value `V` of the board *after*
a placement (the "afterstate"). The bot plays `argmax  r + gamma * V(afterstate)`.

**Features (preprocessing, all normalized to [0,1]):** 10 column heights, holes, bumpiness, max height, deepest well,
row transitions, lines cleared, spin flag, back-to-back flag, combo, all-clear flag, hold piece one-hot (8), next piece one-hot (7).

**Learning.** TD(0) with a replay buffer. For each transition the target is
`y = r_next + gamma * V(next best afterstate)` (or `DeathPenalty` if the game ended), recomputed with the
*current* weights at replay time; only the argmax afterstate is stored. Exploration is epsilon-greedy with linear decay.
Backprop is plain SGD on squared error with the error clipped to ±1 for stability.

**Reward:** `ScoreWeight * basePoints/800 + SurviveBonus - HolePenalty * newHoles`, and `DeathPenalty` (-2) for topping out.
Base points include T-spins, B2B, combos and all-clears, so the bot is paid for advanced play.

**Why a linear output?** State values are not confined to [0,1]; a sigmoid output would silently clip them.

### Plugging in your own MLP

The RL code only needs `IValueModel` (`Predict`, `TrainSample`, `Save`). To use your course classes, write an adapter and
construct it where `new Mlp(...)` appears in `Trainer.Train` (and in `NeuralBot.FromFile` for loading):

```csharp
public sealed class MyMlpAdapter : IValueModel
{
    private readonly YourNetwork _net;                       // your input/hidden/output classes
    public int InputSize => FeatureExtractor.Size;           // 35
    public double Predict(double[] x) => _net.Forward(x)[0];
    public double TrainSample(double[] x, double target, double lr)
    {
        double o = _net.Forward(x)[0];
        _net.Backpropagate(new[] { target }, lr);            // whatever your API calls it
        return 0.5 * (o - target) * (o - target);
    }
    public void Save(string path) { /* write your weights */ }
}
```
If your output layer is sigmoid, rescale targets into (0,1) (e.g. `(y + 3) / 25`) and invert after `Forward`.

## Benchmarks (fixed held-out seeds 50000+, never used in training)

| Mode | What is measured |
|---|---|
| 40-line sprint | clear rate, pieces used, simulated time, pieces/line |
| Score attack | score, lines, T-spins, Tetrises within 2:00 of simulated time |
| Survival | pieces / lines / level reached / score until top-out with the speed ramp |

Reported as mean ± std over N seeds (`--seeds`, default 50) against `Random` and `Heuristic` (El-Tetris weights) baselines.
**Simulated time = pieces / PPS** (`--pps`, default 3.0), so results do not depend on your hardware. In survival, once the game
reaches 20G the bot's search is limited to the number of inputs that fit in the shrinking lock delay (15 inputs/s assumed),
so it eventually tops out like a human would. Per-run rows go to `benchmark_results.csv`.

## Deliverables / rubric map

| Rubric item | Where |
|---|---|
| Training log | `training_log.csv`: episode, epsilon, loss, reward, pieces, lines, score, eval lines/score (learning curve) |
| Data description | The "dataset" is self-generated experience: seeded 7-bag games, episodes use seeds 100000+, periodic eval uses 90000+, benchmarks use 50000+ (no overlap = proper train/test split) |
| Preprocessing | `AI/Features.cs` (normalization to [0,1], one-hot encoding) |
| Backprop | `AI/Mlp.cs` |
| Hyperparameter tuning | `experiment` command: one-factor sweeps of learning rate, hidden size, gamma, hole penalty, epsilon decay, search mode -> `experiments/experiments.csv`; write up the winner and why |
| Evaluation | `eval` command, metrics above |

## Layout

```
Game/  PieceData (shapes, SRS kicks, rules)  Board  BagRandomizer  Scoring  PlacementSearch (BFS)  TetrisGame (engine)
AI/    IValueModel  Mlp  Features  Bots (reward, scorer, Neural/Heuristic/Random)  Trainer (+ExperimentRunner)  Evaluator
UI/    ConsoleRenderer  LivePlay (human / bot / duel loop, real-time gravity & lock delay)
Program.cs   CLI
```

## Known limitations

- Single-player only (no garbage/attack). Scoring is the modern single-player guideline.
- The bot places pieces instantly, so PPS is an assumed constant rather than finger speed.
- Single training run per config in `experiment`; for the report, repeat with `--seed 1/2/3` to show variance.
- No target network (replay + recomputed targets only); add one if training is unstable.
