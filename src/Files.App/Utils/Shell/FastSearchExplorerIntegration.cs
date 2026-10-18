// Copyright (c) Files FastSearch contributors.
// Licensed under the MIT License.

using Microsoft.Win32;
using System.IO;
using System.Security.Principal;
using Windows.ApplicationModel;

namespace Files.App.Utils.Shell;

internal static class FastSearchExplorerIntegration
{
	private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesFastSearch", "ShellIntegration");
	private static string LauncherPath => Path.Combine(StateDirectory, "Files.FastSearch.Launcher.exe");

	internal static bool IsEnabled()
	{
		for (var i = 0; i < ExplorerRegistrationBackup.CommandKeys.Length; i++)
		{
			using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ExplorerRegistrationBackup.CommandKeys[i]);
			var expected = $"\"{LauncherPath}\"" + (i < 2 ? " \"%1\"" : string.Empty);
			if (string.Equals(key?.GetValue(string.Empty) as string, expected, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	internal static async Task<bool> SetEnabledAsync(bool enabled)
	{
		var source = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "FilesOpenDialog", "Files.App.Launcher.exe");
		if (enabled)
		{
			Directory.CreateDirectory(StateDirectory);
			File.Copy(source, LauncherPath, true);
			if (!IsEnabled())
			{
				var history = Path.Combine(StateDirectory, "Backups");
				Directory.CreateDirectory(history);
				var archive = Path.Combine(history, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bin");
				using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes")!;
				ExplorerRegistrationBackup.Save(classes, archive);
				File.Copy(archive, Path.Combine(StateDirectory, "ExplorerRegistrationBackup.bin"), true);
			}
		}

		// Run the protected package copy elevated, never a helper from a user-writable directory.
		var start = new ProcessStartInfo(source)
		{
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden,
		};
		start.ArgumentList.Add(enabled ? "--set-default-fastsearch" : "--restore-fastsearch");
		start.ArgumentList.Add(WindowsIdentity.GetCurrent().User!.Value);
		using var process = Process.Start(start);
		if (process is null)
			return false;
		await process.WaitForExitAsync();
		return process.ExitCode == 0 && IsEnabled() == enabled;
	}
}
