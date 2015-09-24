using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dotnet
{
    public static class Commands
    {
        public const string CommandCleanDescription = "- deletes tools, packages, and bin project subdirectories";
        public const string CommandDebugDescription = "- generates debugging information";
        public const string CommandHelpDescription = "- help";
        public const string CommandLogDescription = "- logs diagnostics info";
        public const string CommandNewDescription = "- creates template sources for a new console app";
        public const string CommandOptimizeDescription = "- enables optimizations performed by the compiler";

        public const string CommandPlatformDescription =
            "- specifies which platform this code can run on, default is anycpu";

        public const string CommandProjectFile = "ProjectFile";

        public const string CommandRecurseDescription =
            "- compiles the sources in the current directory and subdirectories specified by the wildcard";

        public const string CommandSourceFiles = "SourceFiles";

        public const string CommandTargetDescription =
            "- compiles the sources in the current directory into an exe(default) or dll";

        public const string CommandUnsafeDescription = "- allows compilation of code that uses the unsafe keyword";

        public const string LinuxCommandAllOptionsUsage =
            "[-l] [-t:{exe|library}] [-r:<wildcard>] [-d:{full|pdbonly}] [-o] [-u] [-p:{anycpu|anycpu32bitpreferred|x86|x64}] [ProjectFile] [SourceFiles]";

        public const string LinuxCommandClean = "-c";
        public const string LinuxCommandDebug = "-d";
        public const string LinuxCommandHelp1 = "-?";
        public const string LinuxCommandHelp2 = "-help";
        public const string LinuxCommandHelp3 = "-h";
        public const string LinuxCommandLog = "-l";
        public const string LinuxCommandNew = "-n";
        public const string LinuxCommandOptimize = "-o";
        public const string LinuxCommandPlatform = "-p";
        public const string LinuxCommandRecurse = "-r";
        public const string LinuxCommandStart = "-";
        public const string LinuxCommandTarget = "-t";
        public const string LinuxCommandUnsafe = "-u";

        public const string ProjectFileDescription =
            "- specifies which project file to use, default to the one in the current directory, if only one exists";

        public const string SourceFileDescription = "- specifices which source files to compile";

        public const string WinCommandAllOptionsUsage =
            "[/ log] [/target:{exe|library}] [/recurse:<wildcard>] [/debug:{full|pdbonly}] [/optimize] [/unsafe] [/platform:{anycpu|anycpu32bitpreferred|x86|x64}] [ProjectFile] [SourceFiles]";

        public const string WinCommandClean = "/clean";
        public const string WinCommandDebug = "/debug";
        public const string WinCommandHelp1 = "/?";
        public const string WinCommandHelp2 = "/help";
        public const string WinCommandHelp3 = "/h";
        public const string WinCommandLog = "/log";
        public const string WinCommandNew = "/new";
        public const string WinCommandOptimize = "/optimize";
        public const string WinCommandPlatform = "/platform";
        public const string WinCommandRecurse = "/recurse";
        public const string WinCommandStart = "/";
        public const string WinCommandTarget = "/target";
        public const string WinCommandUnsafe = "/unsafe";

    }
}
