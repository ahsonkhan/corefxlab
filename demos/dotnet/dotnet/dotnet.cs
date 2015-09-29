// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace dotnet
{
    internal static class Program
    {
        private static readonly Log Log = new Log();
        private static readonly Settings Settings = new Settings();

        private static void Main(string[] args)
        {
#if WINDOWS
            if (Array.Exists(args, element => element == Commands.WinCommandNew))
#else
            if (Array.Exists(args, element => element == Commands.LinuxCommandNew))
#endif
            {
                OtherActions.CreateNewProject();
                return;
            }

#if WINDOWS
            if (Array.Exists(args, element => element == Commands.WinCommandClean))
#else
            if (Array.Exists(args, element => element == Commands.LinuxCommandClean))
#endif
            {
                OtherActions.Clean(Settings, Log);
                return;
            }

            if (!ParseArguments(args))
            {
                PrintUsage();
            }
            else
            {
                Build(Settings, Log);
            }
        }

        private static bool ParseArguments(string[] args)
        {
            if (Settings.NeedHelp(args) || !Settings.IsValid(args))
            {
                return false;
            }

            ParseSwitchesWindows(args);
            ParseSwitchesLinux(args);

            var currentDirectory = Directory.GetCurrentDirectory();

            var specifiedProjectFilename = Array.Find(args, element => element.EndsWith(".dotnetproj"));
            var specifiedProjectFile = specifiedProjectFilename == null
                ? null
                : Directory.GetFiles(currentDirectory, specifiedProjectFilename);
            var projectFiles = Directory.GetFiles(currentDirectory, "*.dotnetproj");
            Settings.ProjectFile = specifiedProjectFile == null
                ? projectFiles.Length == 1 ? projectFiles[0] : ""
                : specifiedProjectFile.Length == 1 ? specifiedProjectFile[0] : "";

#if WINDOWS
            var specifiedSourceFilenames = Array.FindAll(args, element => element.EndsWith(".cs") && !element.StartsWith(Commands.WinCommandStart));
#else
            var specifiedSourceFilenames = Array.FindAll(args, element => element.EndsWith(".cs") && !element.StartsWith(Commands.LinuxCommandStart));
#endif

            foreach (var sourceFilename in specifiedSourceFilenames)
            {
                Settings.SourceFiles.AddRange(Directory.GetFiles(currentDirectory, sourceFilename));
            }

#if WINDOWS
            return ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.WinCommandTarget)),
                Settings.SetTargetSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.WinCommandPlatform)),
                       Settings.SetPlatformSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.WinCommandDebug)),
                       Settings.SetDebugSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.WinCommandRecurse)),
                       Settings.SetRecurseSpecification);
#else
            return ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.LinuxCommandTarget)),
                Settings.SetTargetSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.LinuxCommandPlatform)),
                       Settings.SetPlatformSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.LinuxCommandDebug)),
                       Settings.SetDebugSpecification) &&
                   ValidateAndSetOptionSpecifications(Array.Find(args, element => element.StartsWith(Commands.LinuxCommandRecurse)),
                       Settings.SetRecurseSpecification);
#endif
        }

        [Conditional("WINDOWS")]
        private static void ParseSwitchesWindows(string[] args)
        {
            Settings.Log = Log.IsEnabled = Array.Exists(args, element => element == Commands.WinCommandLog);
            Settings.Optimize = Array.Exists(args, element => element == Commands.WinCommandOptimize);
            Settings.Unsafe = Array.Exists(args, element => element == Commands.WinCommandUnsafe);
        }

        [Conditional("LINUX")]
        private static void ParseSwitchesLinux(string[] args)
        {
            Settings.Log = Log.IsEnabled = Array.Exists(args, element => element == Commands.LinuxCommandLog);
            Settings.Optimize = Array.Exists(args, element => element == Commands.LinuxCommandOptimize);
            Settings.Unsafe = Array.Exists(args, element => element == Commands.LinuxCommandUnsafe);
        }

        private static bool ValidateAndSetOptionSpecifications(string option, Func<string, bool> setFunction)
        {
            if (option == null) return true;
            if (option.Split(':').Length == 2)
            {
                var optionSpecification = option.Split(':')[1];
                if (!setFunction(optionSpecification))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.UsageString);
            PrintUsageWindows();
            PrintUsageLinux();
            Console.WriteLine(Messages.AppNameSpacing + Commands.CommandProjectFile + Messages.SingleSpace + Commands.ProjectFileDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.CommandSourceFiles + Messages.SingleSpace + Commands.SourceFileDescription);
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.CommandNote1);
            Console.WriteLine(Messages.CommandNote2);
        }

        [Conditional("WINDOWS")]
        private static void PrintUsageWindows()
        {
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.WinCommandAllOptionsUsage);
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.CommandsString);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.WinCommandHelp1 + Messages.SingleSpace + Commands.CommandHelpDescription);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.WinCommandNew + Messages.SingleSpace + Commands.CommandNewDescription);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.WinCommandClean + Messages.SingleSpace + Commands.CommandCleanDescription);
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.OptionsString);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandLog + Messages.SingleSpace + Commands.CommandLogDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandTarget + Messages.SingleSpace + Commands.CommandTargetDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandRecurse + Messages.SingleSpace + Commands.CommandRecurseDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandDebug + Messages.SingleSpace + Commands.CommandDebugDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandOptimize + Messages.SingleSpace + Commands.CommandOptimizeDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandUnsafe + Messages.SingleSpace + Commands.CommandUnsafeDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.WinCommandPlatform + Messages.SingleSpace + Commands.CommandPlatformDescription);
        }

        [Conditional("LINUX")]
        private static void PrintUsageLinux()
        {
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.LinuxCommandAllOptionsUsage);
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.CommandsString);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.LinuxCommandHelp1 + Messages.SingleSpace + Commands.CommandHelpDescription);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.LinuxCommandNew + Messages.SingleSpace + Commands.CommandNewDescription);
            Console.WriteLine(Messages.AppName + Messages.SingleSpace + Commands.LinuxCommandClean + Messages.SingleSpace + Commands.CommandCleanDescription);
            Console.WriteLine(string.Empty);
            Console.WriteLine(Messages.OptionsString);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandLog + Messages.SingleSpace + Commands.CommandLogDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandTarget + Messages.SingleSpace + Commands.CommandTargetDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandRecurse + Messages.SingleSpace + Commands.CommandRecurseDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandDebug + Messages.SingleSpace + Commands.CommandDebugDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandOptimize + Messages.SingleSpace + Commands.CommandOptimizeDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandUnsafe + Messages.SingleSpace + Commands.CommandUnsafeDescription);
            Console.WriteLine(Messages.AppNameSpacing + Commands.LinuxCommandPlatform + Messages.SingleSpace + Commands.CommandPlatformDescription);
        }

        private static void Build(Settings settings, Log log)
        {
            var properties = ProjectPropertiesHelpers.InitializeProperties(settings, log);

            if (properties.Sources.Count == 0)
            {
                Console.WriteLine(Messages.NoSource);
                return;
            }

            if (!Directory.Exists(properties.OutputDirectory))
            {
                Directory.CreateDirectory(properties.OutputDirectory);
            }

            if (!Directory.Exists(properties.ToolsDirectory))
            {
                Directory.CreateDirectory(properties.ToolsDirectory);
            }

            if (!Directory.Exists(properties.PackagesDirectory))
            {
                Directory.CreateDirectory(properties.PackagesDirectory);
            }
            
            var projectJsonFile = Path.Combine(properties.ProjectDirectory, "project.json");
            if (!File.Exists(projectJsonFile))
            {
                CreateDefaultProjectJson(properties);
            }

            if (!DnuAgent.GetDnuAndRestore(properties, log))
            {
                return;
            }

            if (!GetDependencies(properties))
            {
                Console.WriteLine(Messages.FailedToGetDependencies);
                return;
            }

#if WINDOWS
#else
            for(var i = 0; i <properties.Dependencies.Count; i++)
            {
                var temp = properties.Dependencies[i].Replace("\\", "/").Replace("~/.dnx/","");
                properties.Dependencies[i] = temp;
            }

            for(var i = 0; i <properties.References.Count; i++)
            {
                var temp = properties.References[i].Replace("\\", "/").Replace("~/.dnx/", "");
                properties.References[i] = temp;
            }
#endif

            if (!CscAction.Execute(properties, Log)) return;
            if (Settings.Target != "library")
            {
                ConvertToCoreConsoleAction(properties);
            }
            OutputRuntimeDependenciesAction(properties);
            log.WriteLine(Paths.OutputLocation + "\\{0} created", properties.AssemblyNameAndExtension);


            IntermediateLanguagetoCpp(properties, settings, log);
            CpptoNative(properties, settings, log);
        }

        private static void OutputRuntimeDependenciesAction(ProjectProperties properties)
        {
            foreach (var reference in properties.References)
            {
                // properties.References.Add(reference);
                Console.WriteLine(reference);
            }
            foreach (var outputAssembly in properties.Dependencies)
            {
                //properties.Dependencies.Add(outputAssembly);
                Console.WriteLine(outputAssembly);
            }
            foreach (var file in properties.Dependencies)
            {
                FileSystemHelpers.CopyFile(file, properties.OutputDirectory);
            }
        }

        private static bool GetDependencies(ProjectProperties properties)
        {
            var t = new[] { "DNXCore,Version=v5.0" };
            var getDependencies = new GetDependencies
            {
                ProjectLockFile = Path.Combine(properties.ProjectDirectory, "project.lock.json"),
                TargetMonikers = t,
#if WINDOWS
                RuntimeIdentifier = "win7-x64",
#else
                RuntimeIdentifier = "ubuntu.14.04-x64",
#endif
                AllowFallbackOnTargetSelection = true
            };
            try
            {
                getDependencies.ExecuteCore();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            foreach (var reference in getDependencies.ResolvedReferences)
            {
                properties.References.Add(reference);
                //Console.WriteLine(reference);
            }
            foreach (var outputAssembly in getDependencies.ResolvedCopyLocalItems)
            {
                properties.Dependencies.Add(outputAssembly);
                //Console.WriteLine(outputAssembly);
            }
            
            return true;
        }

        private static void ConvertToCoreConsoleAction(ProjectProperties properties)
        {
            var dllPath = Path.ChangeExtension(properties.OutputAssemblyPath, "dll");
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
            }
            File.Move(properties.OutputAssemblyPath, dllPath);

            var coreConsolePath =
                ProjectPropertiesHelpers.GetConsoleHostNative(ProjectPropertiesHelpers.GetPlatformOption(Settings.Platform), "win7") +
                "//CoreConsole.exe";
            File.Copy(Path.Combine(properties.PackagesDirectory, coreConsolePath), properties.OutputAssemblyPath);
        }


        private static void CreateDefaultProjectJson(ProjectProperties properties)
        {
            var fileName = Path.Combine(properties.ProjectDirectory, "project.json");
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
                file.WriteLine(@"    },");
 
                file.WriteLine(@"    ""runtimes"": {");
                file.WriteLine(@"        ""win7-x86"": { },");
                file.WriteLine(@"        ""win7-x64"": { },");
                file.WriteLine(@"        ""ubuntu.14.04-x64"": { }");
                file.WriteLine(@"    }");
                file.WriteLine(@"}");
            }
        }

        private static void IntermediateLanguagetoCpp(ProjectProperties properties, Settings settings, Log log)
        {
            var processSettings = new ProcessStartInfo
            {
                FileName = Paths.IlToCppFilePath,
                Arguments = Path.Combine(properties.OutputDirectory, properties.AssemblyName) +".dll" + " -r " + 
                properties.OutputDirectory+ @"/*.dll" + " -r " + Paths.CustomMsCoreLib + " -llvm " + "-out " + Path.Combine(properties.OutputDirectory, properties.AssemblyName) + ".cpp",
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
                }
            }
        }

        private static void CpptoNative(ProjectProperties properties, Settings settings, Log log)
        {
            var processSettings = new ProcessStartInfo
            {
                FileName = Paths.ClangPath,
                Arguments = " -g -lstdc++ -lrt -Wno-invalid-offsetof " + " " + Paths.ClangInc + " " + Paths.ClangIncGc + " " + Paths.ClangIncGcEnv + " " + Paths.ClanglxstubsPath + " " + Paths.ClangMainPath + " " + Path.Combine(properties.OutputDirectory, properties.AssemblyName) + ".cpp" + " " + Paths.ClanglibSystem + " " + Paths.Clanglibclr + " -o " + "./" + properties.AssemblyName,
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
                }
            }
        }
    }
}