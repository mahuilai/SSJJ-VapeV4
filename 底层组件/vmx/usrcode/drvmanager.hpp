#include <Windows.h>
#include <iostream>
#include <Winternl.h>
#include <vector>
#include <Psapi.h>
#include "lazy_importer.hpp"
#include "xorstr.hpp"

__declspec(noinline) bool CreateServiceEx(const char* path, const char* name) noexcept {
	HKEY handle;

	char* RegKey = (char*)SecureAlloc(MAX_PATH);
	LI_FN(wsprintfA)(RegKey, xorstr_("System\\CurrentControlSet\\Services\\%s"), name);
	LSTATUS result = LI_FN(RegCreateKeyA).in(LI_MODULE("ADVAPI32.dll").get())(HKEY_LOCAL_MACHINE, RegKey, &handle);
	SecureFree(RegKey);
	if (result != ERROR_SUCCESS) return FALSE;

	unsigned char Type = 1;
	result = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(handle, xorstr_("Type"), NULL, REG_DWORD, &Type, 4u);
	if (result != ERROR_SUCCESS) return FALSE;

	unsigned char ErrorControl = 3;
	result = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(handle, xorstr_("ErrorControl"), NULL, REG_DWORD, &ErrorControl, 4u);
	if (result != ERROR_SUCCESS) return FALSE;

	unsigned char Start = 3;
	result = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(handle, xorstr_("Start"), NULL, REG_DWORD, &Start, 4u);
	if (result != ERROR_SUCCESS) return FALSE;

	char* Path = (char*)SecureAlloc(MAX_PATH);
	LI_FN(wsprintfA)(Path, xorstr_("\\??\\%s"), path);
	result = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(handle, xorstr_("ImagePath"), NULL, REG_SZ, (BYTE*)Path, (DWORD)LI_FN(lstrlenA)(Path));
	SecureFree(Path);
	if (result != ERROR_SUCCESS) return FALSE;

	return ERROR_SUCCESS == LI_FN(RegCloseKey).in(LI_MODULE("ADVAPI32.dll").get())(handle);
}

__declspec(noinline) NTSTATUS StartServiceEx(const char* name) {
	FARPROC NtLoadDriver = LI_FN(GetProcAddress)(LI_FN(GetModuleHandleA)(xorstr_("ntdll.dll")), xorstr_("NtLoadDriver"));
	if (!NtLoadDriver) return FALSE;
	
	ANSI_STRING Ansi_Path = { 0 };
	UNICODE_STRING Unicode_Path = { 0 };

	char* RegPath = (char*)SecureAlloc(MAX_PATH);
	LI_FN(sprintf)(RegPath, xorstr_("\\Registry\\Machine\\System\\CurrentControlSet\\Services\\%s"), name);
	LI_FN(RtlInitAnsiString)(&Ansi_Path, RegPath);
	LI_FN(RtlAnsiStringToUnicodeString)(&Unicode_Path, &Ansi_Path, true);
	NTSTATUS Result = ((NTSTATUS(__stdcall*)(PUNICODE_STRING))NtLoadDriver)(&Unicode_Path);
	SecureFree(RegPath);

	return Result;
}

__declspec(noinline) bool StopServiceEx(const char* name) {
	FARPROC NtUnloadDriver = LI_FN(GetProcAddress)(LI_FN(GetModuleHandleA)(xorstr_("ntdll.dll")), xorstr_("NtUnloadDriver"));
	if (!NtUnloadDriver) return FALSE;
	ANSI_STRING Ansi_Path = { 0 };
	UNICODE_STRING Unicode_Path = { 0 };

	char* RegPath = (char*)SecureAlloc(MAX_PATH);
	LI_FN(wsprintfA)(RegPath, xorstr_("\\Registry\\Machine\\System\\CurrentControlSet\\Services\\%s"), name);
	LI_FN(RtlInitAnsiString)(&Ansi_Path, RegPath);
	LI_FN(RtlAnsiStringToUnicodeString)(&Unicode_Path, &Ansi_Path, true);
	bool Result = ((NTSTATUS(__stdcall*)(PUNICODE_STRING))NtUnloadDriver)(&Unicode_Path) == ERROR_SUCCESS;
	SecureFree(RegPath);

	return Result;
}

__declspec(noinline) bool DeleteServiceEx(const char* name) {
	HKEY handle;
	LSTATUS result = LI_FN(RegOpenKeyA).in(LI_MODULE("ADVAPI32.dll").get())(HKEY_LOCAL_MACHINE, 
		xorstr_("System\\CurrentControlSet\\Services\\"), &handle);
	return 
		LI_FN(RegDeleteKeyA).in(LI_MODULE("ADVAPI32.dll").get())(handle, name) == ERROR_SUCCESS 
		&& LI_FN(RegCloseKey).in(LI_MODULE("ADVAPI32.dll").get())(handle) == ERROR_SUCCESS;
}

__declspec(noinline) bool IsDriverLoaded(const char* driverName) {
	LPVOID* drivers = static_cast<LPVOID*>(SecureAlloc(1024 * sizeof(LPVOID)));
	if (drivers == nullptr) return false;

	DWORD cbNeeded = 0;

	if (!LI_FN(K32EnumDeviceDrivers)(drivers, 1024 * sizeof(LPVOID), &cbNeeded)) {
		SecureFree(drivers);
		return false;
	}

	int numDrivers = cbNeeded / sizeof(LPVOID);
	if (numDrivers > 1024) {
		SecureFree(drivers);
		drivers = static_cast<LPVOID*>(SecureAlloc(numDrivers * sizeof(LPVOID)));
		if (drivers == nullptr) return false;

		if (!LI_FN(K32EnumDeviceDrivers)(drivers, numDrivers * sizeof(LPVOID), &cbNeeded)) {
			SecureFree(drivers);
			return false;
		}
	}

	char* baseName = (char*)SecureAlloc(520);
	for (int i = 0; i < numDrivers; i++) {
		if (LI_FN(K32GetDeviceDriverBaseNameA)(drivers[i], baseName, 260)) {
			if (LI_FN(lstrcmpA)(driverName, baseName) == 0) {
				SecureFree(baseName);
				SecureFree(drivers);

				return true;
			}
		}
	}

	SecureFree(baseName);
	SecureFree(drivers);
	return false;
}