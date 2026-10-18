// Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
using Microsoft.Win32;
using System;
using System.IO;
using System.Text;

namespace Files.App.Utils.Shell;

internal static class ExplorerRegistrationBackup
{
	internal static readonly string[] CommandKeys =
	[
		@"Folder\shell\open\command",
		@"Folder\shell\explore\command",
		@"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command",
	];

	internal static void Save(RegistryKey classes, string path)
	{
		using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using var writer = new BinaryWriter(stream);
		writer.Write(Encoding.ASCII.GetBytes("FFSREG1\0"));
		writer.Write((uint)CommandKeys.Length);
		foreach (var command in CommandKeys)
		{
			using var key = classes.OpenSubKey(command);
			WriteValue(writer, key, string.Empty);
			WriteValue(writer, key, "DelegateExecute");
		}
		stream.Flush(true);
	}

	private static void WriteValue(BinaryWriter writer, RegistryKey? key, string name)
	{
		if (key is null || Array.IndexOf(key.GetValueNames(), name) < 0)
		{
			writer.Write(0u); writer.Write(0u); writer.Write(0u);
			return;
		}
		var kind = key.GetValueKind(name);
		var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!;
		var bytes = kind switch
		{
			RegistryValueKind.String or RegistryValueKind.ExpandString => Encoding.Unicode.GetBytes((string)value + '\0'),
			RegistryValueKind.MultiString => Encoding.Unicode.GetBytes(string.Join('\0', (string[])value) + "\0\0"),
			RegistryValueKind.DWord => BitConverter.GetBytes((int)value),
			RegistryValueKind.QWord => BitConverter.GetBytes((long)value),
			RegistryValueKind.Binary or RegistryValueKind.None => (byte[])value,
			_ => throw new InvalidDataException("Unsupported registry value type."),
		};
		writer.Write(1u);
		writer.Write(kind is RegistryValueKind.None ? 0u : (uint)kind);
		writer.Write((uint)bytes.Length);
		writer.Write(bytes);
	}
}
