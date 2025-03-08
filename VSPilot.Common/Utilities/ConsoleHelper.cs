using System;
using System.Runtime.InteropServices;

namespace VSPilot.Common.Utilities
{
    public static class ConsoleHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        public static void EnsureConsole()
        {
            AllocConsole();
        }
    }
}
