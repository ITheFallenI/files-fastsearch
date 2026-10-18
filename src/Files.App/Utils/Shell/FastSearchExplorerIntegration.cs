// Copyright (c) Files FastSearch contributors.
// Licensed under the MIT License.

using Microsoft.Win32;
using System.IO;
using Windows.ApplicationModel;

namespace Files.App.Utils.Shell;

internal static class FastSearchExplorerIntegration
{
	private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesFastSearch", "ShellIntegration");
	private static string LauncherPath => Path.Combine(StateDirectory, "Files.FastSearch.Launcher.exe");
	private static readonly string[] CommandKeys =
	[
		@"Software\Classes\Folder\shell\open\command",
		@"Software\Classes\Folder\shell\explore\command",
		@"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command",
	];

	internal static bool IsEnabled()
	{
		for (var i = 0; i < CommandKeys.Length; i++)
		{
			using var key = Registry.CurrentUser.OpenSubKey(CommandKeys[i]);
			var expected = $"\"{LauncherPath}\"" + (i < 2 ? " \"%1\"" : string.Empty);
			if (string.Equals(key?.GetValue(string.Empty) as string, expected, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	internal static async Task<bool> SetEnabledAsync(bool enabled)
	{
		if (enabled)
		{
			var source = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "FilesOpenDialog");
			Directory.CreateDirectory(StateDirectory);
			File.Copy(Path.Combine(source, "Files.App.Launcher.exe"), LauncherPath, true);
			File.Copy(Path.Combine(source, "RestoreFastSearchExplorer.ps1"), Path.Combine(StateDirectory, "RestoreFastSearchExplorer.ps1"), true);
		}

		var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(StateDirectory, "RestoreFastSearchExplorer.ps1"), "-Action", enabled ? "Enable" : "Restore" })
			start.ArgumentList.Add(argument);
		using var process = Process.Start(start);
		if (process is null)
			return false;
		await process.WaitForExitAsync();
		return process.ExitCode == 0 && IsEnabled() == enabled;
	}
}
