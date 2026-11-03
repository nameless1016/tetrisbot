using Backprop;

namespace TetrisBot.AI;

public sealed class BackpropModel : IValueModel
{
    private readonly NeuralNet _engine;

    public int InputSize { get; }

    public BackpropModel(int inputs = FeatureExtractor.Size, int hidden = 64)
    {
        InputSize = inputs;
        _engine = new NeuralNet(inputs, hidden, 1);
    }

    public double Predict(double[] x)
    {
        for (int i = 0; i < x.Length; i++)
            _engine.setInputs(i, x[i]);
        _engine.run();
        return _engine.getOuputData(0);
    }

    public double TrainSample(double[] x, double target, double lr)
    {
        for (int i = 0; i < x.Length; i++)
            _engine.setInputs(i, x[i]);
        _engine.setDesiredOutput(0, target);
        _engine.learn();
        double pred = _engine.getOuputData(0);
        double err = target - pred;
        return err * err;
    }

    public void Save(string path) => _engine.saveWeights(path);
    public void Load(string path) => _engine.loadWeights(path);
}
