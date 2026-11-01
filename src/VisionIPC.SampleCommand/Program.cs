using System;
using System.Threading;

namespace VisionIPC.SampleCommand
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("VisionIPC sample command");
            for (var index = 1; index <= 3; index++)
            {
                Console.WriteLine("Step " + index + "/3");
                Thread.Sleep(350);
            }
            Console.WriteLine("Sample command finished.");
            return 0;
        }
    }
}
