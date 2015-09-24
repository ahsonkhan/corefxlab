// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace dotnet
{
    public static class Messages
    {
        public const string AppName = "dotnet.exe";
        public const string AppNameSpacing = "           ";
        public const string CleanUpException = "Exception during cleanup: {0}";

        public const string CommandNote1 =
            "NOTE #1: uses csc.exe in <project>\\tools subdirectory, or csc.exe on the path.";

        public const string CommandNote2 = "NOTE #2: dependencies.txt, references.txt can be used to override details.";
        public const string Compiling = "compiling";
        public const string CscNotFound = "ERROR: csc.exe needs to be on the path.";
        public const string FileNotFound = "Could not find file {0}.";
        public const string NoSource = "no sources found";
        public const string NugetFailed = "Failed to get nuget or restore packages.";
        public const string ProcessExit = "Process exit code: {0}";
        public const string SingleSpace = " ";
        public const string UsageString = "Usage:";
        public const string OptionsString = "Options:";
        public const string CommandsString = "Commands:";

    }
}
