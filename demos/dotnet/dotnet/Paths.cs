// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//#define WINDOWS
#define LINUX

namespace dotnet
{
    public static class Paths
    {
        //public const string CscPath = @"D:\git\roslyn\Binaries\Debug\core-clr\csc.exe";
        public const string OutputLocation = "bin";
        public const string Packages = "packages";
        public const string Tools = "tools";
        public const string NugetPath = @"http://dist.nuget.org/win-x86-commandline/v3.1.0-beta/nuget.exe";
        public const string NugetFileName = "nuget.exe";

#if WINDOWS
        public const string CscPath = FileLocations + @"\roslyn\rcsc.exe";
        public const string FileLocations = @"C:\Users\t-ahkh\Documents\NativeCompilation";
        public const string DnuFileName = @"C:\Users\t-ahkh\.dnx\runtimes\dnx-coreclr-win-x86.1.0.0-beta7\bin\dnu.cmd";
        public const string IlToCppFilePath = FileLocations + @"\ILToCpp\ILToCPP.exe";
        public const string ClangPath = "\"C:\\Program Files\\LLVM\\bin\\clang.exe\"";
        public const string ClangInc = "-I" + FileLocations + @"\inc";
        public const string ClangIncGc = "-I" + FileLocations + @"\inc\GC";
        public const string ClangIncGcEnv = "-I" + FileLocations + @"\inc\GC\env";
        public const string ClanglxstubsPath = FileLocations + @"\inc\lxstubs.cpp";
        public const string ClangMainPath = FileLocations + @"\inc\main.cpp";

        public const string ClanglibSystem =
            FileLocations + @"\sharedlibs\libSystem.Native.a";

        public const string Clanglibclr =
            FileLocations + @"\sharedlibs\libclrgc.a";

        public const string CustomMsCoreLib = FileLocations + @"\mscorlib\mscorlib.dll";
#else
        public const string FileLocations = @"/home/ddcloud/Documents/dotnet/dotnet/bin/NativeCompilation";
        public const string CscPath = FileLocations + @"/roslyn/rcsc.exe";
        public const string DnuFileName = @"dnu";
        public const string IlToCppFilePath = FileLocations + @"\ILToCpp\ILToCPP.exe";
        public const string ClangPath = "clang";
        public const string ClangInc = "-I" + FileLocations + @"/inc";
        public const string ClangIncGc = "-I" + FileLocations + @"/inc/C";
        public const string ClangIncGcEnv = "-I" + FileLocations + @"/inc/GC/env";
        public const string ClanglxstubsPath = FileLocations + @"/inc/lxstubs.cpp";
        public const string ClangMainPath = FileLocations + @"/inc/main.cpp";

        public const string ClanglibSystem =
            FileLocations + @"/sharedlibs/libSystem.Native.a";

        public const string Clanglibclr =
            FileLocations + @"/sharedlibs/libclrgc.a";

        public const string CustomMsCoreLib = FileLocations + @"/mscorlib/mscorlib.dll";
        public const string CorerunPath = @"/home/ddcloud/Documents/dotnet/dotnet/bin/cscn/coreclr/corerun";
#endif

    }
}
