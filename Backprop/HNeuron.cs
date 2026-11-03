using System;

namespace Backprop
{
    public class HNeuron
    {
        private double []weights;
        private int idno;
        private int wsize;
        private double hactivation;
        private double bias;
        private double error;
        static System.Random rand;
        public HNeuron()
        {
            idno=0;
            hactivation=0.0;
            bias=this.randomweight();
            error=0.0;
            wsize=10;
            weights=new double[10];
            this.setRandomWeights(10);
        }
        public HNeuron(int idnodata,int size)
        {
            idno=idnodata;
            hactivation=0.0;
            bias=0.01;
            wsize=size;
            error=0.0;
            weights=new double[wsize];
            this.setRandomWeights(wsize);
        }
        public void calculateErr(double [] der)
        {
            double result=0.0;
            for(int x=0;x<der.Length;x++)
            {
                result+=(der[x]*weights[x]);
            }
            error=(result*hactivation*(1-hactivation));
        }
        public void setWeight(double lrpout,double [] der)
        {
            double temp=hactivation*lrpout;
            for(int x=0;x<der.Length;x++)
            {
                weights[x]+=(temp*der[x]);
            }
        }
        public void setWeight(int pos,double dat)
        {
            weights[pos]=dat;
        }
        public double getWeight(int outid)
        {
            return weights[outid];
        }
        public void setErr(double x)
        {
            error=x;
        }
        public double getErr()
        {
            return error;
        }
        public void setHactivation(double data)
        {
            this.hactivation=data;
        }
        public double getHactivation()
        {
            return hactivation;
        }
        public void setIdno(int x)
        {
            idno=x;
        }
        public int getIdno()
        {
            return idno;
        }
        public void setBias(double b)
        {
            bias=b;
        }
        public double getBias()
        {
            return bias;
        }
        public void changeBias(double lrpin)
        {
            bias+=(error*lrpin);
        }
        public void setRandomWeights(int size)
        {
            for(int x=0;x<size;x++)
            {
                weights[x]=this.randomweight();
            }
        }
        private double randomweight()
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
