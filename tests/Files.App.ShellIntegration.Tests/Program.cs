// Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
using Files.App.Utils.Shell;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

var id = Guid.NewGuid().ToString("N");
var keyPath = @"Software\FilesFastSearchTests\" + id;
var directory = Path.Combine(Path.GetTempPath(), "FilesFastSearchTests-" + id);
Directory.CreateDirectory(directory);
var backupPath = Path.Combine(directory, "backup.bin");
var launcher = Path.Combine(directory, "Files.FastSearch.Launcher.exe");
var project = new DirectoryInfo(AppContext.BaseDirectory);
while (project is not null && !File.Exists(Path.Combine(project.FullName, "NativeTests.vcxproj"))) project = project.Parent;
var native = Path.Combine(project?.FullName ?? throw new DirectoryNotFoundException("Native test project"), "bin", "native", "NativeTests.exe");
if (!File.Exists(native)) throw new FileNotFoundException("Build NativeTests.vcxproj first", native);
using var root = Registry.CurrentUser.CreateSubKey(keyPath);
void Equal(object expected, object? actual, string message)
{
	if (!Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, actual {actual}");
}
void Run(string action, int expected = 0)
{
	var start = new ProcessStartInfo(native) { UseShellExecute = false, CreateNoWindow = true };
	foreach (var argument in new[] { action, keyPath, backupPath, launcher }) start.ArgumentList.Add(argument);
	using var process = Process.Start(start)!;
	process.WaitForExit();
	Equal(expected, process.ExitCode, "Native " + action);
}
try
{
	ExplorerRegistrationBackup.Save(root, backupPath);
	Run("enable"); Run("enable"); Run("restore");
	foreach (var path in ExplorerRegistrationBackup.CommandKeys)
	{
		using var key = root.OpenSubKey(path)!;
		Equal(0, key.GetValueNames().Length, "Restore absent values");
	}
	Console.WriteLine("PASS: native enable, repeated enable and restoration of absent values");
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[0], true)!)
	{
		key.SetValue("", "%LOCALAPPDATA%\\Other Files\\Launcher.exe \"%1\"", RegistryValueKind.ExpandString);
		key.SetValue("DelegateExecute", "original delegate");
		key.SetValue("Unrelated", "preserved");
	}
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[1], true)!) key.SetValue("", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
	var typedBackup = Path.Combine(directory, "typed.bin");
	ExplorerRegistrationBackup.Save(root, typedBackup);
	File.Copy(typedBackup, backupPath, true);
	Run("enable"); Run("restore");
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[0])!)
	{
		Equal(RegistryValueKind.ExpandString, key.GetValueKind(""), "Preserve type");
		Equal("%LOCALAPPDATA%\\Other Files\\Launcher.exe \"%1\"", key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames), "Preserve raw environment string");
		Equal("original delegate", key.GetValue("DelegateExecute"), "Restore delegate");
		Equal("preserved", key.GetValue("Unrelated"), "Preserve unrelated values");
	}
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[1])!) Equal("1,2,3", string.Join(',', (byte[])key.GetValue("")!), "Preserve binary data");
	Console.WriteLine("PASS: managed backup/native restore preserves types, raw data and unrelated values");
	Run("enable");
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[0], true)!) { key.SetValue("", "new manager"); key.SetValue("DelegateExecute", "new delegate"); }
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[1], true)!) key.SetValue("DelegateExecute", "changed delegate");
	Run("restore");
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[0])!) { Equal("new manager", key.GetValue(""), "Keep newer manager"); Equal("new delegate", key.GetValue("DelegateExecute"), "Keep newer delegate"); }
	using (var key = root.OpenSubKey(ExplorerRegistrationBackup.CommandKeys[1])!) Equal("changed delegate", key.GetValue("DelegateExecute"), "Keep changed delegate");
	Console.WriteLine("PASS: ownership guard preserves a later file-manager choice");
	Run("enable");
	File.WriteAllBytes(backupPath, new byte[] { 1, 2, 3 });
	Run("enable", 13);
	Run("restore");
	foreach (var path in ExplorerRegistrationBackup.CommandKeys)
	{
		using var key = root.OpenSubKey(path)!;
		Equal(null!, key.GetValue(""), "Missing-backup recovery removes only own defaults");
	}
	Console.WriteLine("PASS: invalid backup refuses enable; recovery reveals Windows defaults");
}
finally
{
	root.Dispose();
	Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
}
