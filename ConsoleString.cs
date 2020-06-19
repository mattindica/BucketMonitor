using System.Linq;

namespace BucketMonitor
{
    using System;

    public class ConsoleString
    {
        public ConsoleString()
        {
        }

        private int MaxCount { get; set; } = 0;

        public void Update(string value)
        {
            if (this.MaxCount > value.Length)
            {
                Console.Write($"\r{value}{new string(' ', value.Count())}");
            }
            else
            {
                Console.Write($"\r{value}");
                this.MaxCount = value.Length;
            }
        }
    }
}
