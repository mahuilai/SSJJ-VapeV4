#ifndef _AMD64_
#define Cracker_EXPORT
#else
#define Cracker_EXPORT
#endif

__declspec(noinline) int Cracker_InitAndCheckEnvironment() {
    LoadAndImportDlls();

    if (!IsRunningAsAdmin()) return StatusCode::NoUAC;
    if (!ChangePrivilege(true)) return StatusCode::NoPrivilege;

    return StatusCode::Success;
}

__declspec(noinline) int Cracker_PreVerifySecretKey(const char* SecretKey, ULONG_PTR* Key, _Globals** out_Globals) {
    int result = Cracker_InitAndCheckEnvironment();

    if (result != StatusCode::Success) return result;

    _Globals* Globals = (_Globals*)(SecureAlloc(sizeof(_Globals)));

    if (!SecretKey) {
        SecureFree(Globals);
        ChangePrivilege(false);
        return StatusCode::NoSecretKeyPointer;
    }
    else if (LI_FN(lstrlenA)(SecretKey) != 16) {
        SecureFree(Globals);
        ChangePrivilege(false);
        return StatusCode::MismatchSecretKeyLength;
    }
    __movsb((PBYTE)&Globals->SecretKey, (PBYTE)SecretKey, 17);

    char* consolelist_response = 0;
    __int64 consolelist_response_size = 0;
    if (
        !httpRequest(
            xorstr_(""),
            &consolelist_response,
            &consolelist_response_size
        )
        )
    {
        SecureFree(Globals);
        ChangePrivilege(false);
        return StatusCode::QuestVmxlistHTTPError;
    }

    if (
        !tcpTestLatencyForEachLine(consolelist_response, &Globals->console_server, &Globals->console_port)) 
    {
        SecureFree(Globals);
        ChangePrivilege(false);
        return StatusCode::TestLatencyAndNoValidServer;
    }
    
    SecureFree(consolelist_response);

    char* hwid = GetMachineCode(false);
    __movsb((PBYTE)&Globals->hwid, (PBYTE)hwid, 18);
    SecureFree(hwid);

    char* request_buffer = (char*)SecureAlloc(MAX_PATH);
    LI_FN(sprintf)(request_buffer, xorstr_("vmx2\n"));

    char* pipapi_response = 0;
    int pipapi_response_size = 0;

    if (!tcpRequest(Globals->console_server, Globals->console_port, request_buffer, &pipapi_response, &pipapi_response_size)) {
        SecureFree(request_buffer);
        SecureFree(Globals->console_server);
        SecureFree(Globals);

        return StatusCode::QuestVmxpIPAPITCPError;
    }
        
    char* ipapi_response = 0;
    __int64 ipapi_response_size = 0;
    if (
        !httpRequest(
            pipapi_response,
            &ipapi_response,
            &ipapi_response_size
        )
        )
    {
        SecureFree(request_buffer);
        SecureFree(pipapi_response);
        SecureFree(Globals->console_server);
        SecureFree(Globals);

        return StatusCode::QuestVmxIPAPIHTTPError;
    }
   
    SecureFree(pipapi_response);
    Globals->ip = ipapi_response;

    __stosb((PBYTE)request_buffer, 0, MAX_PATH);
    LI_FN(sprintf)(request_buffer, xorstr_("p1v1*%s\n"), Globals->SecretKey);

    StatusCode licensevalid_status = StatusCode::ImPossibleReason;
    char* licensevalid_response = 0;
    int licensevalid_response_size = 0;
    if (!tcpRequest(Globals->console_server, Globals->console_port, request_buffer, &licensevalid_response, &licensevalid_response_size)) licensevalid_status = StatusCode::QuestVmxSecretKeyValidTCPError;
    if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Internal"))) licensevalid_status = StatusCode::ServerInternalError;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Banned"))) licensevalid_status = StatusCode::UserBanned;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("InValidSecretKey"))) licensevalid_status = StatusCode::InValidSecretKey;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Allow"))) goto licensevalid;

    SecureFree(request_buffer);
    SecureFree(licensevalid_response);
    SecureFree(Globals->ip);
    SecureFree(Globals->console_server);
    SecureFree(Globals);

    ChangePrivilege(false);
    return licensevalid_status;

licensevalid:
    SecureFree(request_buffer);
    SecureFree(licensevalid_response);

    Globals->source = 0x91A;
    *out_Globals = Globals;

    return StatusCode::Success;
}

// 初始化应用密匙
__declspec(noinline) int Cracker_InitAppKey(const char* SecretKey, ULONG_PTR* AppKey) {
    int result = StatusCode::ImPossibleReason;
    result = Cracker_InitAndCheckEnvironment();
    if (result != StatusCode::Success) return result;

    _Globals* Globals = 0;
    result = Cracker_PreVerifySecretKey(SecretKey, AppKey, &Globals);
    if (result == StatusCode::Success)
        *AppKey = (ULONG_PTR)Globals ^ (ULONG_PTR)0xFE4A5956A9F18671;

    return result;
}

__declspec(noinline) int Cracker_GetMachineCodeFeature(char* buffer) {
    int result = StatusCode::ImPossibleReason;
    result = Cracker_InitAndCheckEnvironment();
    if (result != StatusCode::Success) return result;

    char* MachineCode = GetMachineCode(true);
    LI_FN(strcpy)(buffer, MachineCode);
    SecureFree(MachineCode);

    return StatusCode::Success;
}

__declspec(noinline) int Cracker_PreVerifySecretKeyForProduct(const char* ProductName, _Globals* Globals) {
    if (Globals->source != 0x91A) return StatusCode::AppKeyInvalid;

    LocalPtr<char*> request_buffer(MAX_PATH);
    LI_FN(sprintf)(&request_buffer, xorstr_("p1v2*%s*%s*%s\n"), Globals->SecretKey, ProductName, Globals->hwid);

    StatusCode licensevalid_status = StatusCode::ImPossibleReason;
    char* licensevalid_response = 0;
    int licensevalid_response_size = 0;
    if (!tcpRequest(Globals->console_server, Globals->console_port, &request_buffer, &licensevalid_response, &licensevalid_response_size)) licensevalid_status = StatusCode::QuestVmxSecretKeyValidTCPError;
    if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Internal"))) licensevalid_status = StatusCode::ServerInternalError;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Banned"))) licensevalid_status = StatusCode::UserBanned;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("InValidSecretKey"))) licensevalid_status = StatusCode::InValidSecretKey;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("InValidProduct"))) licensevalid_status = StatusCode::InValidProduct;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Pause"))) licensevalid_status = StatusCode::PauseProduct;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("DisAllow"))) licensevalid_status = StatusCode::ExpiredLicense;
    else if (!LI_FN(lstrcmpA)(licensevalid_response, xorstr_("Allow"))) goto licensevalid;

    SecureFree(licensevalid_response);
    SecureFree(Globals->ip);
    SecureFree(Globals->console_server);
    SecureFree(Globals);

    ChangePrivilege(false);
    return licensevalid_status;

licensevalid:
    SecureFree(licensevalid_response);

    return StatusCode::Success;
}