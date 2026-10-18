// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Same native search-path pinning as Find My Files: never the current directory or PATH
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
[assembly: SupportedOSPlatform("windows")]
