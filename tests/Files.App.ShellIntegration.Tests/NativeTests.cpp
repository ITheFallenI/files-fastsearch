// Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
#include "../../src/Files.App.Launcher/FastSearchExplorerRegistration.h"

int wmain(int argc, wchar_t** argv)
{
	if (argc != 5 || std::wstring(argv[2]).find(L"Software\\FilesFastSearchTests\\") != 0) return ERROR_INVALID_PARAMETER;
	HKEY classes;
	const auto opened = RegOpenKeyExW(HKEY_CURRENT_USER, argv[2], 0, KEY_READ | KEY_WRITE, &classes);
	if (opened != ERROR_SUCCESS) return opened;
	FastSearchRegistration::Backup backup;
	const bool valid = FastSearchRegistration::ReadBackup(argv[3], backup);
	LSTATUS result;
	if (std::wstring(argv[1]) == L"enable") result = valid ? FastSearchRegistration::Enable(classes, argv[4], backup) : ERROR_INVALID_DATA;
	else if (std::wstring(argv[1]) == L"restore") result = FastSearchRegistration::Restore(classes, argv[4], valid ? &backup : nullptr);
	else result = ERROR_INVALID_PARAMETER;
	RegCloseKey(classes);
	return result;
}
