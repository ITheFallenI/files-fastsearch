// Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
#pragma once
#include <Windows.h>
#include <array>
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>
#include <cstdint>
#include <cstring>

namespace FastSearchRegistration
{
	inline constexpr const wchar_t* CommandKeys[] = {
		L"Folder\\shell\\open\\command",
		L"Folder\\shell\\explore\\command",
		L"CLSID\\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\\shell\\opennewwindow\\command"
	};
	struct Value { bool present = false; DWORD kind = 0; std::vector<BYTE> bytes; };
	using Backup = std::array<std::array<Value, 2>, 3>;

	inline bool ReadBackup(const std::wstring& path, Backup& backup)
	{
		std::ifstream input(std::filesystem::path(path), std::ios::binary);
		if (!input) return false;
		input.seekg(0, std::ios::end);
		const auto size = input.tellg();
		if (size < 12 || size > 1048576) return false;
		input.seekg(0);
		char magic[8]; DWORD count;
		if (!input.read(magic, 8) || memcmp(magic, "FFSREG1\0", 8) != 0 || !input.read(reinterpret_cast<char*>(&count), 4) || count != 3) return false;
		for (auto& entry : backup)
			for (auto& value : entry)
			{
				DWORD present, length;
				if (!input.read(reinterpret_cast<char*>(&present), 4) || !input.read(reinterpret_cast<char*>(&value.kind), 4)
					|| !input.read(reinterpret_cast<char*>(&length), 4) || present > 1 || length > 1048576) return false;
				if (!present && (length || value.kind)) return false;
				if (value.kind != REG_NONE && value.kind != REG_SZ && value.kind != REG_EXPAND_SZ && value.kind != REG_BINARY
					&& value.kind != REG_DWORD && value.kind != REG_MULTI_SZ && value.kind != REG_QWORD) return false;
				value.present = present != 0;
				value.bytes.resize(length);
				if (length && !input.read(reinterpret_cast<char*>(value.bytes.data()), length)) return false;
			}
		return input.peek() == std::char_traits<char>::eof();
	}

	inline std::wstring Command(const std::wstring& launcher, size_t index)
	{
		return L"\"" + launcher + L"\"" + (index < 2 ? L" \"%1\"" : L"");
	}

	inline bool Owns(HKEY key, const std::wstring& expected)
	{
		std::vector<wchar_t> text(32768, 0);
		DWORD kind = 0, bytes = static_cast<DWORD>(text.size() * sizeof(wchar_t));
		return RegQueryValueExW(key, NULL, NULL, &kind, reinterpret_cast<BYTE*>(text.data()), &bytes) == ERROR_SUCCESS
			&& kind == REG_SZ && bytes < text.size() * sizeof(wchar_t) && _wcsicmp(text.data(), expected.c_str()) == 0;
	}

	inline LSTATUS RestoreValue(HKEY key, const wchar_t* name, const Value* value)
	{
		if (value && value->present)
			return RegSetValueExW(key, name, 0, value->kind, value->bytes.data(), static_cast<DWORD>(value->bytes.size()));
		const auto status = RegDeleteValueW(key, name);
		return status == ERROR_FILE_NOT_FOUND ? ERROR_SUCCESS : status;
	}

	inline LSTATUS Restore(HKEY classes, const std::wstring& launcher, const Backup* backup)
	{
		LSTATUS result = ERROR_SUCCESS;
		for (size_t i = 0; i < _countof(CommandKeys); ++i)
		{
			HKEY key;
			const auto opened = RegOpenKeyExW(classes, CommandKeys[i], 0, KEY_QUERY_VALUE | KEY_SET_VALUE, &key);
			if (opened == ERROR_FILE_NOT_FOUND) continue;
			if (opened != ERROR_SUCCESS) { result = opened; continue; }
			if (Owns(key, Command(launcher, i)))
			{
				wchar_t delegate[2] = {}; DWORD bytes = sizeof(delegate), kind = 0;
				if (RegQueryValueExW(key, L"DelegateExecute", NULL, &kind, reinterpret_cast<BYTE*>(delegate), &bytes) == ERROR_SUCCESS
					&& kind == REG_SZ && bytes == sizeof(wchar_t) && delegate[0] == L'\0')
				{
					const auto status = RestoreValue(key, L"DelegateExecute", backup ? &(*backup)[i][1] : nullptr);
					if (status != ERROR_SUCCESS) result = status;
				}
				const auto status = RestoreValue(key, NULL, backup ? &(*backup)[i][0] : nullptr);
				if (status != ERROR_SUCCESS) result = status;
			}
			RegCloseKey(key);
		}
		return result;
	}

	inline LSTATUS Enable(HKEY classes, const std::wstring& launcher, const Backup& backup)
	{
		for (size_t i = 0; i < _countof(CommandKeys); ++i)
		{
			HKEY key;
			auto status = RegCreateKeyExW(classes, CommandKeys[i], 0, NULL, 0, KEY_QUERY_VALUE | KEY_SET_VALUE, NULL, &key, NULL);
			if (status == ERROR_SUCCESS)
			{
				const auto command = Command(launcher, i);
				status = RegSetValueExW(key, NULL, 0, REG_SZ, reinterpret_cast<const BYTE*>(command.c_str()), static_cast<DWORD>((command.size() + 1) * sizeof(wchar_t)));
				if (status == ERROR_SUCCESS) status = RegSetValueExW(key, L"DelegateExecute", 0, REG_SZ, reinterpret_cast<const BYTE*>(L""), sizeof(wchar_t));
				RegCloseKey(key);
			}
			if (status != ERROR_SUCCESS) { Restore(classes, launcher, &backup); return status; }
		}
		return ERROR_SUCCESS;
	}
}
