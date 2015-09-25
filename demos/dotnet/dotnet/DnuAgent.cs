// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;

namespace dotnet
{
    internal static class DnuAgent
    {
        public static bool GetDnuAndRestore(ProjectProperties properties, Log log)
        {
            if (RestorePackagesAction(properties, log)) return true;
            Console.WriteLine(Messages.DnuFailed);
            return false;
        }

        private static bool RestorePackagesAction(ProjectProperties properties, ILog log)
        {
            log.WriteLine("restoring packages");

            string jsonFile = Path.Combine(properties.ProjectDirectory, "project.json");

            if (!File.Exists(jsonFile))
            {
                Console.WriteLine(Messages.FileNotFound, jsonFile);
                return false;
            }

            var processSettings = new ProcessStartInfo
            {
                FileName = Paths.DnuFileName,
                Arguments ="restore " + jsonFile + " --packages " + properties.PackagesDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            log.WriteLine("Executing {0}", processSettings.FileName);
            log.WriteLine("Arguments: {0}", processSettings.Arguments);
            log.WriteLine("project.json:\n{0}", File.ReadAllText(jsonFile));

            using (var process = Process.Start(processSettings))
            {
                try
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        log.WriteLine(output);
                        log.Error(error);
                        process.WaitForExit();
                        var exitCode = process.ExitCode;
                        if (exitCode != 0) Console.WriteLine(Messages.ProcessExit, exitCode);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }
            return true;
        }

    }
}
