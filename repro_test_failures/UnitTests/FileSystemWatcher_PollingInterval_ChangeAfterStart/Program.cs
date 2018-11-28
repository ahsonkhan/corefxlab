using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace FileSystemWatcher_PollingInterval_ChangeAfterStart
{
    class Program
    {

        private const int MillisecondsTimeout = 1000;

        static void Main(string[] args)
        {
            for (int i = 0; i < 1_000; i++)
            {
                Console.WriteLine($"ITERATION: {i}");
                try
                {
                    FileSystemWatcher_PollingInterval_ChangeAfterStart();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed at iteration: {i} | " + ex.Message);
                    break;
                }
            }
        }

        public static string GetRandomDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(path);
            return path;
        }

        public static void FileSystemWatcher_PollingInterval_ChangeAfterStart()
        {
            string currentDir = GetRandomDirectory();
            string fileName = Path.GetRandomFileName();
            string fullName = Path.Combine(currentDir, fileName);
            bool eventRaised = false;

            using (PollingFileSystemWatcher watcher = new PollingFileSystemWatcher(currentDir) { PollingInterval = Timeout.Infinite })
            {
                AutoResetEvent signal = new AutoResetEvent(false);

                watcher.Changed += (e, changes) =>
                {
                    eventRaised = true;
                    watcher.PollingInterval = Timeout.Infinite;
                    signal.Set();
                };

                watcher.Start();

                using (FileStream file = File.Create(fullName)) { }
                watcher.PollingInterval = 0;
                signal.WaitOne(1000);
            }

            try
            {
                if (!eventRaised)
                {
                    throw new InvalidOperationException("Test failed ");
                }
                //Assert.True(eventRaised);
            }
            finally
            {
                Directory.Delete(currentDir, true);
            }
        }
    }
}
