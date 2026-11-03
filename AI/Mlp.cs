using System.Globalization;
using System.Text;

namespace TetrisBot.AI;

/// <summary>
/// Stand-in 3-layer perceptron: Input -> Hidden (tanh) -> Output (linear), trained with
/// plain backpropagation (SGD, MSE loss, gradient clipping for stability).
/// Replace with your own classes through <see cref="IValueModel"/> if the assignment requires it.
/// </summary>
public sealed class Mlp : IValueModel
{
    private readonly int _nIn, _nHid;
    private readonly double[] _w1, _b1, _w2, _hid;
    private double _b2;

    /// <summary>Output error is clipped to +-GradClip before backprop (Huber-style).</summary>
    public double GradClip = 1.0;

    public int InputSize => _nIn;

    public Mlp(int nIn, int nHid, int seed = 0)
    {
        _nIn = nIn; _nHid = nHid;
        _w1 = new double[nHid * nIn];
        _b1 = new double[nHid];
        _w2 = new double[nHid];
        _hid = new double[nHid];
        var rng = new Random(seed);
        double a1 = 1.0 / Math.Sqrt(nIn), a2 = 1.0 / Math.Sqrt(nHid);
        for (int i = 0; i < _w1.Length; i++) _w1[i] = (rng.NextDouble() * 2 - 1) * a1;
        for (int i = 0; i < _w2.Length; i++) _w2[i] = (rng.NextDouble() * 2 - 1) * a2;
    }

    public double Predict(double[] x)
    {
        double o = _b2;
        for (int h = 0; h < _nHid; h++)
        {
            double s = _b1[h];
            int off = h * _nIn;
            for (int i = 0; i < _nIn; i++) s += _w1[off + i] * x[i];
            o += _w2[h] * Math.Tanh(s);
        }
        return o;
    }

    public double TrainSample(double[] x, double target, double lr)
    {
        // forward
        double o = _b2;
        for (int h = 0; h < _nHid; h++)
        {
            double s = _b1[h];
            int off = h * _nIn;
            for (int i = 0; i < _nIn; i++) s += _w1[off + i] * x[i];
            _hid[h] = Math.Tanh(s);
            o += _w2[h] * _hid[h];
        }

        // loss + output gradient
        double err = o - target;
        double d = Math.Max(-GradClip, Math.Min(GradClip, err));

        // backward
        for (int h = 0; h < _nHid; h++)
        {
            double a = _hid[h];
            double dh = d * _w2[h] * (1.0 - a * a);   // uses w2 BEFORE it is updated
            _w2[h] -= lr * d * a;
            int off = h * _nIn;
            for (int i = 0; i < _nIn; i++) _w1[off + i] -= lr * dh * x[i];
            _b1[h] -= lr * dh;
        }
        _b2 -= lr * d;
        return 0.5 * err * err;
    }

    // ---------- persistence (plain text, human readable) ----------
    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MLP {_nIn} {_nHid}");
        AppendArr(sb, _w1); AppendArr(sb, _b1); AppendArr(sb, _w2);
        sb.AppendLine(_b2.ToString("R", CultureInfo.InvariantCulture));
        File.WriteAllText(path, sb.ToString());
    }

    public static Mlp Load(string path)
    {
        var lines = File.ReadAllLines(path);
        var head = lines[0].Split(' ');
        var m = new Mlp(int.Parse(head[1]), int.Parse(head[2]));
        ReadArr(lines[1], m._w1); ReadArr(lines[2], m._b1); ReadArr(lines[3], m._w2);
        m._b2 = double.Parse(lines[4], CultureInfo.InvariantCulture);
        return m;
    }

    private static void AppendArr(StringBuilder sb, double[] a)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(a[i].ToString("R", CultureInfo.InvariantCulture));
        }
        sb.AppendLine();
    }

    private static void ReadArr(string line, double[] dst)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < dst.Length; i++) dst[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
    }
}
