using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ScottPlot;

namespace Lab08
{
    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }

    class Server
    {
        private PoolRecord[] pool;
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;
        private int n;
        private double mu;
        private DateTime startTime;
        private List<double> busyChannelsHistory = new List<double>();

        public Server(int channels, double serviceIntensity)
        {
            n = channels;
            mu = serviceIntensity;
            pool = new PoolRecord[n];
            startTime = DateTime.Now;
        }

        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;
                double currentBusy = GetBusyChannelsCount();
                busyChannelsHistory.Add(currentBusy);

                for (int i = 0; i < n; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        processedCount++;
                        return;
                    }
                }
                rejectedCount++;
            }
        }

        private void Answer(object arg)
        {
            int id = (int)arg;
            double serviceTime = -Math.Log(1.0 - new Random().NextDouble()) / mu;
            Thread.Sleep(TimeSpan.FromSeconds(serviceTime));
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].thread == Thread.CurrentThread)
                    pool[i].in_use = false;
        }

        private int GetBusyChannelsCount()
        {
            int count = 0;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].in_use) count++;
            return count;
        }

        public double GetAverageBusyChannels()
        {
            if (busyChannelsHistory.Count == 0) return 0;
            return busyChannelsHistory.Average();
        }

        public double GetExperimentDuration()
        {
            return (DateTime.Now - startTime).TotalSeconds;
        }
    }

    class Client
    {
        private Server server;
        private Random random = new Random();

        public Client(Server server)
        {
            this.server = server;
            this.request += server.proc;
        }

        public void send(int id)
        {
            procEventArgs args = new procEventArgs();
            args.id = id;
            OnProc(args);
        }

        protected virtual void OnProc(procEventArgs e)
        {
            EventHandler<procEventArgs> handler = request;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public event EventHandler<procEventArgs> request;

        public void GenerateRequests(double lambda, int durationSec)
        {
            DateTime endTime = DateTime.Now.AddSeconds(durationSec);
            int id = 1;
            while (DateTime.Now < endTime)
            {
                double interval = -Math.Log(1.0 - random.NextDouble()) / lambda;
                Thread.Sleep(TimeSpan.FromSeconds(interval));
                if (DateTime.Now < endTime)
                    send(id++);
            }
        }
    }

    class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }

    class Program
    {
        static int n = 3;
        static double mu = 1.0;
        static int simulationTime = 60;

        static void Main(string[] args)
        {
            Console.WriteLine("Lab 08. Queueing System Modeling");
            Console.WriteLine($"Channels: {n}, mu: {mu}, Time: {simulationTime}s\n");

            double[] lambdaValues = Enumerable.Range(1, 10).Select(i => i * 0.5).ToArray();
            
            var expP0 = new List<double>();
            var expPn = new List<double>();
            var expQ = new List<double>();
            var expA = new List<double>();
            var expK = new List<double>();

            var theorP0 = new List<double>();
            var theorPn = new List<double>();
            var theorQ = new List<double>();
            var theorA = new List<double>();
            var theorK = new List<double>();

            foreach (double lambda in lambdaValues)
            {
                Console.Write($"Lambda = {lambda:F2}... ");
                
                var (eP0, ePn, eQ, eA, eK) = RunExperiment(lambda);
                var (tP0, tPn, tQ, tA, tK) = CalculateTheoretical(lambda);

                expP0.Add(eP0); expPn.Add(ePn); expQ.Add(eQ); expA.Add(eA); expK.Add(eK);
                theorP0.Add(tP0); theorPn.Add(tPn); theorQ.Add(tQ); theorA.Add(tA); theorK.Add(tK);

                Console.WriteLine($"P_fail: exp={ePn:F4} theor={tPn:F4}");
            }

            Directory.CreateDirectory("result");
            
            CreateGraph(lambdaValues, expPn, theorPn, "Failure Probability", "result/p-1.png");
            CreateGraph(lambdaValues, expQ, theorQ, "Relative Throughput", "result/p-2.png");
            CreateGraph(lambdaValues, expA, theorA, "Absolute Throughput", "result/p-3.png");
            CreateGraph(lambdaValues, expK, theorK, "Avg Busy Channels", "result/p-4.png");
            CreateGraph(lambdaValues, expP0, theorP0, "Idle Probability", "result/p-5.png");

            SaveResults(lambdaValues, expP0, expPn, expQ, expA, expK, 
                       theorP0, theorPn, theorQ, theorA, theorK);

            Console.WriteLine("\nDone. Check result/ folder and results.txt");
        }

        static (double P0, double Pn, double Q, double A, double K) RunExperiment(double lambda)
        {
            Server server = new Server(n, mu);
            Client client = new Client(server);

            Thread clientThread = new Thread(() => client.GenerateRequests(lambda, simulationTime));
            clientThread.Start();
            clientThread.Join();

            Thread.Sleep(2000);

            double duration = server.GetExperimentDuration();
            double Pn = server.requestCount > 0 ? (double)server.rejectedCount / server.requestCount : 0;
            double Q = 1.0 - Pn;
            double A = server.processedCount / duration;
            double K = server.GetAverageBusyChannels();
            double P0 = 1.0 - (server.processedCount / (double)server.requestCount);

            return (P0, Pn, Q, A, K);
        }

        static (double P0, double Pn, double Q, double A, double K) CalculateTheoretical(double lambda)
        {
            double rho = lambda / mu;
            double sum = 0;
            for (int i = 0; i <= n; i++)
                sum += Math.Pow(rho, i) / Factorial(i);
            
            double P0 = 1.0 / sum;
            double Pn = (Math.Pow(rho, n) / Factorial(n)) * P0;
            double Q = 1.0 - Pn;
            double A = lambda * Q;
            double K = A / mu;

            return (P0, Pn, Q, A, K);
        }

        static int Factorial(int n)
        {
            int result = 1;
            for (int i = 2; i <= n; i++)
                result *= i;
            return result;
        }

       static void CreateGraph(double[] x, List<double> yExp, List<double> yTheor, string title, string filename)
        {
            var plt = new ScottPlot.Plot();
            plt.Title(title);
            plt.XLabel("Lambda (intensity)");
            plt.YLabel(title);

            var scatterExp = plt.Add.Scatter(x, yExp.ToArray());
            scatterExp.Color = ScottPlot.Colors.Red;
            scatterExp.MarkerSize = 5;
            scatterExp.LineWidth = 1;
            scatterExp.Label = "Experimental";

            var scatterTheor = plt.Add.Scatter(x, yTheor.ToArray());
            scatterTheor.Color = ScottPlot.Colors.Blue;
            scatterTheor.MarkerSize = 5;
            scatterTheor.LineWidth = 1;
            scatterTheor.Label = "Theoretical";

            plt.ShowLegend(ScottPlot.Alignment.LowerRight);
            plt.SavePng(filename, 800, 600);
        }

        static void SaveResults(double[] lambda, List<double> expP0, List<double> expPn, List<double> expQ, 
                               List<double> expA, List<double> expK, List<double> theorP0, List<double> theorPn, 
                               List<double> theorQ, List<double> theorA, List<double> theorK)
        {
            using (var sw = new StreamWriter("results.txt", false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine("Lab 08. Queueing System Modeling Results");
                sw.WriteLine($"Channels: {n}, mu: {mu}, Simulation time: {simulationTime}s");
                sw.WriteLine(new string('=', 100));
                sw.WriteLine("λ\tP0(exp)\tP0(theor)\tPn(exp)\tPn(theor)\tQ(exp)\tQ(theor)\tA(exp)\tA(theor)\tK(exp)\tK(theor)");
                sw.WriteLine(new string('-', 100));
                
                for (int i = 0; i < lambda.Length; i++)
                {
                    sw.WriteLine($"{lambda[i]:F2}\t{expP0[i]:F4}\t{theorP0[i]:F4}\t{expPn[i]:F4}\t{theorPn[i]:F4}\t" +
                                $"{expQ[i]:F4}\t{theorQ[i]:F4}\t{expA[i]:F4}\t{theorA[i]:F4}\t{expK[i]:F4}\t{theorK[i]:F4}");
                }
            }
        }
    }
}
