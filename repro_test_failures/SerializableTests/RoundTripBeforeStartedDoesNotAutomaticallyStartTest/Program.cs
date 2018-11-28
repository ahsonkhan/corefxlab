using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace RoundTripBeforeStartedDoesNotAutomaticallyStartTest
{
    class Program
    {
        static void Main(string[] args)
        {
            for (int i = 0; i < 100_000; i++)
            {
                Console.WriteLine($"ITERATION: {i}");
                try
                {
                    RoundTripBeforeStartedDoesNotAutomaticallyStartTest();
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

        private static PollingFileSystemWatcher RoundTrip(PollingFileSystemWatcher watcher)
        {
            PollingFileSystemWatcher deserialized;
            IFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream())
            {
                formatter.Serialize(stream, watcher);

                stream.Position = 0;
                deserialized = (PollingFileSystemWatcher)formatter.Deserialize(stream);
            }

            return deserialized;
        }

        public static void RoundTripBeforeStartedDoesNotAutomaticallyStartTest()
        {
            string currentDir = GetRandomDirectory();
            string fileName = Path.GetRandomFileName();
            string fullName = Path.Combine(currentDir, fileName);
            bool eventRaised = false;
            AutoResetEvent signal = new AutoResetEvent(false);
            var watcher = new PollingFileSystemWatcher(currentDir) { PollingInterval = 0 };

            using (PollingFileSystemWatcher deserialized = RoundTrip(watcher))
            {
                watcher.Dispose();

                deserialized.Changed += (e, args) =>
                {
                    eventRaised = true;
                    watcher.PollingInterval = Timeout.Infinite;
                    signal.Set();
                };

                using (var file = File.Create(fullName)) { }
                signal.WaitOne(1000);
            }

            try
            {
                if (eventRaised)
                {
                    throw new InvalidOperationException("Test failed - watcher did not automatically start");
                }
                //Assert.False(eventRaised);  // watcher did not automatically start
            }
            finally
            {
                Directory.Delete(currentDir, true);
            }
        }
    }
}
