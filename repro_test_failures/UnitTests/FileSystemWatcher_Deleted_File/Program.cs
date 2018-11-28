using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace FileSystemWatcher_Deleted_File
{
    class Program
    {

        private const int MillisecondsTimeout = 1000;
        
        static void Main(string[] args)
        {
            for (int i = 0; i < 100_000; i++)
            {
                Console.WriteLine($"ITERATION: {i}");
                try
                {
                    FileSystemWatcher_Deleted_File();
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

        public static void FileSystemWatcher_Deleted_File()
        {
            string currentDir = GetRandomDirectory();
            string fileName = Path.GetRandomFileName();
            string fullName = Path.Combine(currentDir, fileName);
            bool eventRaised = false;

            using (PollingFileSystemWatcher watcher = new PollingFileSystemWatcher(currentDir) { PollingInterval = 0 })
            {
                AutoResetEvent signal = new AutoResetEvent(false);

                watcher.ChangedDetailed += (e, changes) =>
                {
                    Equal(1, changes.Changes.Length, "a");
                    FileChange change = changes.Changes[0];
                    Equal(WatcherChangeTypes.Deleted, change.ChangeType, "b");
                    Equal(fileName, change.Name, "c");
                    Equal(currentDir, change.Directory, "d");
                    eventRaised = true;
                    watcher.PollingInterval = Timeout.Infinite;
                    signal.Set();
                };

                using (FileStream file = File.Create(fullName)) { }
                watcher.Start();
                File.Delete(fullName);
                signal.WaitOne(MillisecondsTimeout);
            }

            if (!eventRaised)
            {
                throw new InvalidOperationException("Test failed ");
            }
            //Assert.True(eventRaised);

            Directory.Delete(currentDir, true);
        }

        private static bool Equal(object obj1, object obj2, string message)
        {
            if (!obj1.Equals(obj2))
            {
                throw new InvalidOperationException("Test failed: " + message);
            }
            return true;
        }
    }
}
