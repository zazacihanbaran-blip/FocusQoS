using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AFOBenchmarkWorker
{
    internal static class Program
    {
        private static volatile bool running = true;

        private static void Main(string[] args)
        {
            int seconds = ReadInt(args, "--seconds", 10, 5, 60);
            int memoryMb = ReadInt(args, "--memory-mb", 128, 0, 512);
            int threads = ReadInt(args, "--threads", Environment.ProcessorCount + 2, 1, 64);
            List<byte[]> memory = new List<byte[]>();
            try
            {
                int blocks = memoryMb / 8;
                for (int i = 0; i < blocks; i++)
                {
                    byte[] block = new byte[8 * 1024 * 1024];
                    for (int page = 0; page < block.Length; page += 4096) block[page] = (byte)(page + i);
                    memory.Add(block);
                }
            }
            catch (OutOfMemoryException) { }

            List<Thread> workers = new List<Thread>();
            for (int i = 0; i < threads; i++)
            {
                Thread thread = new Thread(BurnCpu);
                thread.IsBackground = true;
                workers.Add(thread);
                thread.Start();
            }

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            running = false;
            foreach (Thread thread in workers) thread.Join(1000);
            GC.KeepAlive(memory);
        }

        private static void BurnCpu()
        {
            ulong value = 1469598103934665603UL;
            while (running)
            {
                for (int i = 1; i < 100000; i++) value = (value ^ (uint)i) * 1099511628211UL;
                if (value == 0) Thread.Yield();
            }
        }

        private static int ReadInt(string[] args, string name, int fallback, int minimum, int maximum)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                int value;
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out value))
                    return Math.Max(minimum, Math.Min(maximum, value));
            }
            return fallback;
        }
    }
}

