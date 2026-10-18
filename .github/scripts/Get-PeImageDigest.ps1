# Copyright (c) Files Community
# Licensed under the MIT License.

# Abstract:
#  Prints the Authenticode-stable SHA-256 digest of a PE image (lowercase hex). This is the value
#  Find My Files' ServiceExecutableTrust compares against the FmfServiceImageSha256 pinned into
#  Files.App.FastSearch before it starts fmf-service.exe elevated. ImageGetDigestStream leaves out
#  the certificate table, so the digest is the same before and after the image is signed.

#Requires -Version 7

param(
    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public static class PeImageDigest
{
    private const uint DigestLevelAll = 0x01 | 0x02 | 0x04;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool DigestFunction(IntPtr refdata, IntPtr data, uint length);

    [DllImport("imagehlp.dll", SetLastError = true)]
    private static extern bool ImageGetDigestStream(IntPtr fileHandle, uint digestLevel, DigestFunction digestFunction, IntPtr digestHandle);

    public static string Compute(string path)
    {
        using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            DigestFunction append = (refdata, data, length) =>
            {
                var buffer = new byte[length];
                if (length > 0)
                    Marshal.Copy(data, buffer, 0, (int)length);
                hash.AppendData(buffer);
                return true;
            };

            if (!ImageGetDigestStream(file.SafeFileHandle.DangerousGetHandle(), DigestLevelAll, append, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ImageGetDigestStream failed for " + path);

            GC.KeepAlive(append);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
    }
}
"@

[PeImageDigest]::Compute((Resolve-Path $Path).Path)
