using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace FileSystemWatcher_Recursive
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
                    FileSystemWatcher_Recursive();
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

        public static void FileSystemWatcher_Recursive()
        {
            string currentDir = GetRandomDirectory();
            string fileName = Path.GetRandomFileName();
            string subDirectory = new DirectoryInfo(currentDir).CreateSubdirectory("sub").FullName;
            string fullName = Path.Combine(subDirectory, fileName);

            bool eventRaised = false;

            using (PollingFileSystemWatcher watcher = new PollingFileSystemWatcher(currentDir, options: new EnumerationOptions { RecurseSubdirectories = true }) { PollingInterval = 1 })
            {
                AutoResetEvent signal = new AutoResetEvent(false);

                watcher.Error += (e, error) =>
                {
                    throw  error.GetException();
                };

                watcher.ChangedDetailed += (e, changes) =>
                {
                    Equal(1, changes.Changes.Length, "a");
                    FileChange change = changes.Changes[0];
                    Equal(WatcherChangeTypes.Created, change.ChangeType, "b");
                    Equal(fileName, change.Name, "c");
                    Equal(subDirectory, change.Directory, "d");
                    eventRaised = true;
                    watcher.PollingInterval = Timeout.Infinite;
                    signal.Set();
                };

                watcher.Start();

                using (FileStream file = File.Create(fullName)) { }
                signal.WaitOne(10000);
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
