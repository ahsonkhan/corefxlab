// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace dotnet
{
    internal static class DnuAgent
    {
        public static bool GetDnuAndRestore(ProjectProperties properties, Log log)
        {
            var projectJsonFile = Path.Combine(properties.ProjectDirectory, "project.json");
            if (!File.Exists(projectJsonFile))
            {
                projectJsonFile = CreateDefaultProjectJson(properties);
            }

            if (RestorePackagesAction(properties, log, projectJsonFile)) return true;
            Console.WriteLine(Messages.DnuFailed);
            return false;
        }

        private static string CreateDefaultProjectJson(ProjectProperties properties)
        {
            var fileName = Path.Combine(properties.ToolsDirectory, "project.json");
            var fs = new FileStream(fileName, FileMode.Create);
            using (var file = new StreamWriter(fs, Encoding.UTF8))
            {
                file.WriteLine(@"{");
                file.WriteLine(@"    ""dependencies"": {");

                for (var index = 0; index < properties.Packages.Count; index++)
                {
                    var package = properties.Packages[index];
                    file.Write(@"        ");
                    file.Write(package);
                    if (index < properties.Packages.Count - 1)
                    {
                        file.WriteLine(",");
                    }
                    else
                    {
                        file.WriteLine();
                    }
                }
                file.WriteLine(@"    },");
                file.WriteLine(@"    ""frameworks"": {");
                file.WriteLine(@"        ""dnxcore50"": { }");
                file.WriteLine(@"    }");
                file.WriteLine(@"}");

                //"runtimes": {
                //"win7-x86": { },
                //"win7-x64": { }
                //},
            }
            return fileName;
        }

        private static bool RestorePackagesAction(ProjectProperties properties, ILog log, string jsonFile)
        {
            log.WriteLine("restoring packages");
            
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

        public static bool DnuListAction(ProjectProperties properties, Log log)
        {
            log.WriteLine("getting dependencies");

            var projectLockFile = Path.Combine(properties.ProjectDirectory, "project.lock.json");
            if (!File.Exists(projectLockFile))
            {
                projectLockFile = Path.Combine(properties.ToolsDirectory, "project.lock.json");

                if (!File.Exists(projectLockFile))
                {
                    Console.WriteLine(Messages.FileNotFound, projectLockFile);
                    return false;
                }
            }

            var processSettings = new ProcessStartInfo
            {
                FileName = Paths.DnuFileName,
                Arguments = "list -a " + projectLockFile,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            log.WriteLine("Executing {0}", processSettings.FileName);
            log.WriteLine("Arguments: {0}", processSettings.Arguments);

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
