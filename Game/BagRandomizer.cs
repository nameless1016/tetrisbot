namespace TetrisBot.Game;

/// <summary>Seeded 7-bag randomizer: same seed = same piece sequence (needed for fair benchmarks).</summary>
public sealed class BagRandomizer
{
    private readonly Random _rng;
    private readonly Queue<PieceType> _queue = new();

    public BagRandomizer(int seed) { _rng = new Random(seed); }

    public PieceType Next()
    {
        if (_queue.Count == 0)
        {
            var bag = new[] { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L };
            for (int i = bag.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            foreach (var p in bag) _queue.Enqueue(p);
        }
        return _queue.Dequeue();
    }
}
