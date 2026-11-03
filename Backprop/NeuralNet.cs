using System;
using System.IO;

namespace Backprop
{
    public class NeuralNet
    {
        private INeuron [] ineuron;
        private HNeuron [] hneuron;
        private ONeuron [] oneuron;
        private const double LRPOUT =0.2;
        private const double LRPIN = 0.15;
        private double [] errorComponent;
        private double [] errorDerivative;
        private double [] desiredout;

        public NeuralNet()
        {
            ineuron=new INeuron[3072];
            hneuron=new HNeuron[64];
            oneuron=new ONeuron[10];
            desiredout=new double[10];
            errorComponent=new double[10];
            errorDerivative=new double[10];
            createNeurons(ineuron.Length,hneuron.Length,oneuron.Length);
        }
        public NeuralNet(int input,int hidden,int output)
        {
            ineuron=new INeuron[input];
            hneuron=new HNeuron[hidden];
            oneuron=new ONeuron[output];
            errorComponent=new double[output];
            errorDerivative=new double[output];
            desiredout=new double[output];
            createNeurons(ineuron.Length,hneuron.Length,oneuron.Length);
        }
        public void createNeurons(int i,int h,int o)
        {
            for (int x=0;x<i;x++)
            {
                ineuron[x]=new INeuron(x,h);
            }
            for (int x=0;x<h;x++)
            {
                hneuron[x]=new HNeuron(x,o);
            }
            for (int x=0;x<o;x++)
            {
                oneuron[x]=new ONeuron(x);
            }
        }
        public double getOuputData(int pos)
        {
            return oneuron[pos].getOActivation();
        }
        public void setInputs(int pos,double data)
        {
            ineuron[pos].setInput(data);
        }
        public void setDesiredOutput(int pos,double data)
        {
            desiredout[pos]=data;
        }
        public double sigmoid(double dat)
        {
            if (dat>=20.00)
            {
                dat=32;
            }
            return (double)(1.0/(1.0 + System.Math.Exp(-dat)));
        }
        public void calculateHA()
        {
            for(int x=0;x<hneuron.Length;x++)
            {
                double summation=0.0;
                for(int y=0;y<ineuron.Length;y++)
                {
                    summation+=ineuron[y].getInput()*ineuron[y].getWeight(x);
                }
                hneuron[x].setHactivation(sigmoid(summation+hneuron[x].getBias()));
            }
        }
        public void calculateOA()
        {
            for(int x=0;x<oneuron.Length;x++)
            {
                double summation=0.0;
                for(int y=0;y<hneuron.Length;y++)
                {
                    summation+=hneuron[y].getHactivation()*hneuron[y].getWeight(x);
                }
                oneuron[x].setOActivation(sigmoid(summation+oneuron[x].getBias()));
            }
        }
        public void calculateEC()
        {
            for (int x=0;x<oneuron.Length;x++)
            {
                errorComponent[x]=(desiredout[x])-(oneuron[x].getOActivation());
            }
        }
        public void calculateDER()
        {
            for (int x=0;x<oneuron.Length;x++)
            {
                errorDerivative[x]=oneuron[x].getOActivation()*(1-(oneuron[x].getOActivation()))*errorComponent[x];
            }
        }
        public void learn()
        {
            this.run();
            this.calculateEC();
            this.calculateDER();
            for(int x=0;x<hneuron.Length;x++)
                hneuron[x].calculateErr(errorDerivative);
            for(int x=0;x<hneuron.Length;x++)
                hneuron[x].setWeight(LRPOUT,errorDerivative);
            for(int x=0;x<ineuron.Length;x++)
            {
                for (int y=0;y<hneuron.Length;y++)
                {
                    ineuron[x].setWeight(y,hneuron[y].getErr(),LRPIN);
                }
            }
            for (int x=0;x<oneuron.Length;x++)
                oneuron[x].changeBias(LRPOUT,errorDerivative);
            for (int x=0;x<hneuron.Length;x++)
                hneuron[x].changeBias(LRPIN);
        }
        public void run()
        {
            this.calculateHA();
            this.calculateOA();
        }
        public bool countgood()
        {
            bool result=true;
            for(int x=0;x<oneuron.Length;x++)
            {
                if((errorComponent[x]-errorDerivative[x])!=0)
                    result=false;
            }
            return result;
        }
        public void saveWeights(String path)
        {
            using (StreamWriter sw = new StreamWriter(path)) 
            {
                for (int x=0;x<ineuron.Length;x++)
                {
                    for(int y=0;y<hneuron.Length;y++)
                    sw.WriteLine(ineuron[x].getWeight(y));
                }
                for(int x=0;x<hneuron.Length;x++)
                {
                    for (int y=0;y<oneuron.Length;y++)
                    {
                        sw.WriteLine(hneuron[x].getWeight(y));
                    }
                }
                for (int x=0;x<hneuron.Length;x++)
                {
                    sw.WriteLine(hneuron[x].getBias());
                }
                for (int x=0;x<oneuron.Length;x++)
                {
                    sw.WriteLine(oneuron[x].getBias());
                }
            }
        }
        public void loadWeights(String path)
        {
            using (StreamReader sr = new StreamReader(path)) 
            {
                for (int x=0;x<ineuron.Length;x++)
                {
                    for(int y=0;y<hneuron.Length;y++)
                    {
                        ineuron[x].setWeight(y,Convert.ToDouble(sr.ReadLine()));
                    }
                }
                for(int x=0;x<hneuron.Length;x++)
                {
                    for (int y=0;y<oneuron.Length;y++)
                    {
                        hneuron[x].setWeight(y,Convert.ToDouble(sr.ReadLine()));
                    }
                }
                for (int x=0;x<hneuron.Length;x++)
                {
                    hneuron[x].setBias(Convert.ToDouble(sr.ReadLine()));
                }
                for (int x=0;x<oneuron.Length;x++)
                {
                    oneuron[x].setBias(Convert.ToDouble(sr.ReadLine()));
                }
            }
        }
    }
}
