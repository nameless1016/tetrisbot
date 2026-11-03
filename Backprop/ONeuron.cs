using System;

namespace Backprop
{
    public class ONeuron
    {
        private double bias;
        private int idno;
        private double OActivation;
        static System.Random rand;
        public ONeuron()
        {
            bias=this.randomBias();
            idno=0;
            OActivation=0.0;
        }
        public ONeuron(int idnodata)
        {
            bias=0.01;
            idno=idnodata;
            OActivation=0.0;
        }

        public void setIdno(int data)
        {
            idno=data;
        }
        public int getIdno()
        {
            return idno;
        }
        public void setOActivation(double data)
        {
            OActivation=data;
        }
        public double getOActivation()
        {
            return OActivation;
        }
        public double getBias()
        {
            return bias;
        }
        public void setBias(double dat)
        {
            bias=dat;
        }
        public void changeBias(double lrpout, double []der)
        {
            bias+=lrpout*der[idno];
        }
        private double randomBias()
        {
            if(rand == null)
            {
                rand = new System.Random();
            }
            int MaxLimit = + 1000; 
            int MinLimit = - 1000; 
            double number = (double) (rand.Next(MinLimit,MaxLimit))/2000;
            return number; 
        }
    }
}
