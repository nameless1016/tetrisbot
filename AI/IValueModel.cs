namespace TetrisBot.AI;

/// <summary>
/// The only thing the RL code needs from a neural network. To use YOUR course MLP classes
/// (input/hidden/output + backprop), write a small adapter that implements this interface
/// (see README, "Plugging in your own MLP"). Output must be LINEAR (unbounded) because
/// state values are not limited to [0,1].
/// </summary>
public interface IValueModel
{
    int InputSize { get; }
    /// <summary>Forward pass: features in, scalar state value out.</summary>
    double Predict(double[] x);
    /// <summary>One SGD backprop step toward <paramref name="target"/>. Returns 0.5*error^2 (for the loss log).</summary>
    double TrainSample(double[] x, double target, double learningRate);
    void Save(string path);
}
