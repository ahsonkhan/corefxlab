// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Remoting.Messaging;

namespace dotnet
{
    public static class Paths
    {
        public const string CscPath = @"D:\git\roslyn\Binaries\Debug\core-clr\csc.exe";
        public const string OutputLocation = "bin";
        public const string Packages = "packages";
        public const string Tools = "tools";
        public const string NugetPath = @"http://dist.nuget.org/win-x86-commandline/v3.1.0-beta/nuget.exe";
        public const string DnuFileName = @"C:\Users\t-ahkh\.dnx\runtimes\dnx-coreclr-win-x86.1.0.0-beta7\bin\dnu.cmd";
        public const string NugetFileName = "nuget.exe";
        public const string IlToCppFilePath = @"C:\Users\t-ahkh\Documents\NativeCompilation\ILToCpp\ILToCPP.exe";
        public const string ClangPath = @"C:\Program Files\LLVM\bin\clang.exe";
        public const string ClangInc = @"-IC:\Users\t-ahkh\Documents\NativeCompilation\inc";
        public const string ClangIncGc = @"-IC:\Users\t-ahkh\Documents\NativeCompilation\inc\GC";
        public const string ClangIncGcEnv = @"-IC:\Users\t-ahkh\Documents\NativeCompilation\inc\GC\env";
        public const string ClanglxstubsPath = @"C:\Users\t-ahkh\Documents\NativeCompilation\inc\lxstubs.cpp";
        public const string ClangMainPath = @"C:\Users\t-ahkh\Documents\NativeCompilation\inc\main.cpp";

        public const string ClanglibSystem =
            @"C:\Users\t-ahkh\Documents\NativeCompilation\sharedlibs\libSystem.Native.a";

        public const string Clanglibclr =
            @"C:\Users\t-ahkh\Documents\NativeCompilation\sharedlibs\libclrgc.a";

        public const string CustomMsCoreLib = @"C:\Users\t-ahkh\Documents\NativeCompilation\mscorlib\mscorlib.dll";
    }
}
