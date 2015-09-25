// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;

namespace dotnet
{
    internal static class FileSystemHelpers
    {
        internal static void CopyAllFiles(string sourceFolder, string destinationFolder)
        {
            foreach (var sourceFilePath in Directory.EnumerateFiles(sourceFolder))
            {
                CopyFile(sourceFilePath, destinationFolder);
            }
        }

        internal static void CopyFile(string file, string destinationFolder)
        {
                var sourceFileName = Path.GetFileName(file);
                if (sourceFileName == null) return;
                var destinationFilePath = Path.Combine(destinationFolder, sourceFileName);
                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }
                File.Copy(file, destinationFilePath);
        }
    }
}