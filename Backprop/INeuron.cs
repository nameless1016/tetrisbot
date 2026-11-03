using System;

namespace Backprop
{
    public class INeuron
    {
        private int idno;
        private double input;
        private double [] weights;
        private int wsize;
        private int num;
        static System.Random rand;
        public INeuron()
        {
            idno=0;
            input=0;
            wsize=64;
            weights=new double[wsize];
            this.setRandomWeights(wsize);
        }
        public INeuron(int idnodata,int size)
        {
            idno=idnodata;
            input=0;
            wsize=size;
            weights=new double[wsize];
            this.setRandomWeights(wsize);
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
        public void setRandomWeights(int size)
        {
            for(int x=0;x<size;x++)
            {
                weights[x]=this.randomweight();
            }        
        }
        public void setWeight(int hidno,double err,double lrpin)
        {
            double errlrpin= err*lrpin;
            weights[hidno]+=(errlrpin*input);
        }
        public void setID(int no)
        {
            idno=no;
        }
        public void setInput(double data)
        {
            input=data;
        }
        public double getInput()
        {
            return input;
        }
        public double getWeight(int hidno)
        {
            return weights[hidno];
        }
        public int getID()
        {
            return idno;
        }
        public void setWeight(int pos,double dat)
        {
            weights[pos]=dat;
        }
    }
}
