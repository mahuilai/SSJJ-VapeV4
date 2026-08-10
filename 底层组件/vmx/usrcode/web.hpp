__declspec(noinline) bool httpRequest(const char* urlA, char** response, __int64* responseSize, bool isPost = false, const char* postData = nullptr) {
    wchar_t* url = AnsiToWide(urlA);

    LocalPtr<URL_COMPONENTS*> urlComp(sizeof(URL_COMPONENTS));
    (&urlComp)->dwStructSize = sizeof(URL_COMPONENTS);
    LocalPtr<wchar_t*> serverNameBuff(512);
    LocalPtr<wchar_t*> serverPathBuff(4096);
    (&urlComp)->lpszHostName = &serverNameBuff;
    (&urlComp)->dwHostNameLength = 256;
    (&urlComp)->lpszUrlPath = &serverPathBuff;
    (&urlComp)->dwUrlPathLength = 2048;

    if (!LI_FN(WinHttpCrackUrl).in(LI_MODULE("WINHTTP.dll").get())(url, LI_FN(lstrlenW)(url), 0, &urlComp)) return false;

    SecureFree(url);

    INTERNET_PORT serverPort = (&urlComp)->nPort ? (&urlComp)->nPort :
        ((&urlComp)->nScheme == INTERNET_SCHEME_HTTPS ? INTERNET_DEFAULT_HTTPS_PORT : INTERNET_DEFAULT_HTTP_PORT);

    HINTERNET hSession = LI_FN(WinHttpOpen).in(LI_MODULE("WINHTTP.dll").get())(xorstr_(L"HTTP-Client"), WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
        WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession)
        return false;

    HINTERNET hConnect = LI_FN(WinHttpConnect).in(LI_MODULE("WINHTTP.dll").get())(hSession, (&urlComp)->lpszHostName, serverPort, 0);
    if (!hConnect) {
        LI_FN(WinHttpCloseHandle)(hSession);
        return false;
    }

    const wchar_t* requestMethod = isPost ? xorstr_(L"POST") : xorstr_(L"GET");
    DWORD requestFlags = ((&urlComp)->nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hRequest = LI_FN(WinHttpOpenRequest).in(LI_MODULE("WINHTTP.dll").get())(hConnect, requestMethod, (&urlComp)->lpszUrlPath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, requestFlags);
    if (!hRequest) {
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hConnect);
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hSession);
        return false;
    }

    LPVOID requestData = nullptr;
    DWORD requestDataLength = 0;
    if (isPost && postData) {
        requestData = const_cast<char*>(postData);
        requestDataLength = static_cast<DWORD>(LI_FN(lstrlenA)(postData));
    }
    if (!LI_FN(WinHttpSendRequest).in(LI_MODULE("WINHTTP.dll").get())(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
        requestData, requestDataLength, requestDataLength, 0)) {
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hRequest);
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hConnect);
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hSession);
        return false;
    }

    if (!LI_FN(WinHttpReceiveResponse).in(LI_MODULE("WINHTTP.dll").get())(hRequest, NULL)) {
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hRequest);
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hConnect);
        LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hSession);
        return false;
    }

    *responseSize = 0;
    DWORD bufferSize = 0;
    do {
        DWORD bytesRead = 0;
        LI_FN(WinHttpQueryDataAvailable).in(LI_MODULE("WINHTTP.dll").get())(hRequest, &bufferSize);
        if (bufferSize > 0) {
            char* buffer = static_cast<char*>(SecureAlloc(bufferSize + 1));
            if (LI_FN(WinHttpReadData).in(LI_MODULE("WINHTTP.dll").get())(hRequest, buffer, bufferSize, &bytesRead)) {
                char* newResponse = static_cast<char*>(SecureAlloc(*responseSize + bytesRead + 1));
                if (*response != nullptr) {
                    __movsb((PBYTE)newResponse, (PBYTE)*response, *responseSize);
                    __stosb((PBYTE)*response, 0, *responseSize);
                    SecureFree(*response);
                }
                __movsb((PBYTE)newResponse + *responseSize, (PBYTE)buffer, bytesRead);
                *response = newResponse;
                *responseSize += bytesRead;
                (*response)[*responseSize] = 0;
            }
            SecureFree(buffer);
        }
    } while (bufferSize > 0);

    // ¹Ø±ÕËùÓÐ¾ä±ú
    LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hRequest);
    LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hConnect);
    LI_FN(WinHttpCloseHandle).in(LI_MODULE("WINHTTP.dll").get())(hSession);

    return true;
}

__declspec(noinline) int tcpTestLatency(const char* ip, const char* port) {
    HMODULE hWs2_32 = LI_FN(GetModuleHandleA)(xorstr_("Ws2_32.dll"));
    PVOID pWSAStartup = LI_FN(GetProcAddress)(hWs2_32, xorstr_("WSAStartup"));
    PVOID pConnect = LI_FN(GetProcAddress)(hWs2_32, xorstr_("connect"));

    LocalPtr<WSADATA*> wsaData(sizeof(WSADATA));

    int result = ((int(__stdcall*)(WORD, LPWSADATA))pWSAStartup)(MAKEWORD(2, 2), &wsaData);
    if (result != 0) return -1;

    LocalPtr<ADDRINFOA*> hints(sizeof(ADDRINFOA));
    (&hints)->ai_family = AF_UNSPEC;
    (&hints)->ai_socktype = SOCK_STREAM;
    (&hints)->ai_protocol = IPPROTO_TCP;

    ADDRINFOA* addrResult = NULL;
    result = LI_FN(getaddrinfo)(ip, port, &hints, &addrResult);
    if (result != 0) {
        LI_FN(WSACleanup)();
        return -1;
    }

    SOCKET connectSocket = LI_FN(socket)(addrResult->ai_family, addrResult->ai_socktype, addrResult->ai_protocol);
    if (connectSocket == INVALID_SOCKET) {
        LI_FN(freeaddrinfo)(addrResult);
        LI_FN(WSACleanup)();
        return -1;
    }

    ULONG64 startTime = LI_FN(GetTickCount64)();
    result = ((int (WSAAPI*)(SOCKET, const struct sockaddr*, int))pConnect)(connectSocket, addrResult->ai_addr, (int)addrResult->ai_addrlen);
    if (result == SOCKET_ERROR) {
        LI_FN(closesocket)(connectSocket);
        LI_FN(freeaddrinfo)(addrResult);
        LI_FN(WSACleanup)();
        return -1;
    }

    ULONG64 latency = LI_FN(GetTickCount64)() - startTime;

    LI_FN(closesocket)(connectSocket);
    LI_FN(freeaddrinfo)(addrResult);
    LI_FN(WSACleanup)();

    return (int)latency;
}

__declspec(noinline) bool tcpTestLatencyForEachLine(const char* serverlist, char** output_serverip, int* output_port) {
    const char* currentPos = serverlist;
    int minLatency = -1;
    LocalPtr<char*> minLatencyAddr;

    while (*currentPos) {
        const char* endOfLine = currentPos;
        while (*endOfLine != '\n' && *endOfLine != '\0') endOfLine++;
        int addrLength = (int)(endOfLine - currentPos);
        if (addrLength > 0 && currentPos[addrLength - 1] == '\n') addrLength--;

        if (addrLength > 0) {
            LocalPtr<char*> currentAddr(addrLength + 1);
            __movsb((PBYTE)&currentAddr, (PBYTE)currentPos, addrLength);
            (&currentAddr)[addrLength] = '\0';

            char* colonPos = m_strchr(&currentAddr, ':');
            if (colonPos) {
                *colonPos = '\0';
                int latency = tcpTestLatency(&currentAddr, colonPos + 1);
                if (latency >= 0 && (minLatency == -1 || latency < minLatency)) {
                    minLatency = latency;
                    minLatencyAddr = currentAddr;
                }
            }
        }

        currentPos = (*endOfLine == '\n') ? endOfLine + 1 : endOfLine;
    }

    if (minLatency == -1) return false;

    char* colonPos = m_strchr(&minLatencyAddr, '\0') + 1;
    *output_serverip = (char*)SecureAlloc(colonPos - &minLatencyAddr);
    if (*output_serverip) {
        __movsb((PBYTE)*output_serverip, (PBYTE)&minLatencyAddr, colonPos - &minLatencyAddr);
        *output_port = LI_FN(atoi)(colonPos);
    }

    return true;
}

bool tcpRequest(const char* serverAddress, int port, const char* message, char** response, int* length) {
    HMODULE hWs2_32 = LI_FN(GetModuleHandleA)(xorstr_("Ws2_32.dll"));
    PVOID pWSAStartup = LI_FN(GetProcAddress)(hWs2_32, xorstr_("WSAStartup"));
    PVOID pConnect = LI_FN(GetProcAddress)(hWs2_32, xorstr_("connect"));

    LocalPtr<WSADATA*> wsaData(sizeof(WSADATA));

    SOCKET sock = INVALID_SOCKET;
    LocalPtr<sockaddr_in*> serverAddr(sizeof(sockaddr_in));

    bool result = false;

    if (((int(__stdcall*)(WORD, LPWSADATA))pWSAStartup)(MAKEWORD(2, 2), &wsaData) != 0) return false;

    sock = LI_FN(socket)(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock == INVALID_SOCKET) {
        LI_FN(WSACleanup)();
        return false;
    }

    (&serverAddr)->sin_family = AF_INET;
    (&serverAddr)->sin_port = LI_FN(htons)(port);

    if (LI_FN(inet_pton)(AF_INET, serverAddress, &(&serverAddr)->sin_addr) <= 0) {
        LI_FN(closesocket)(sock);
        LI_FN(WSACleanup)();
        return false;
    }

    if (((int (WSAAPI*)(SOCKET, const struct sockaddr*, int))pConnect)(sock, (sockaddr*)&serverAddr, sizeof(sockaddr)) == SOCKET_ERROR) {
        LI_FN(closesocket)(sock);
        LI_FN(WSACleanup)();
        return false;
    }

    int messageLen = LI_FN(lstrlenA)(message);

    if (LI_FN(send)(sock, message, messageLen, 0) != messageLen) {
        LI_FN(closesocket)(sock);
        LI_FN(WSACleanup)();
        return false;
    }

    *length = 0;
    *response = NULL;
    int bufferSize = 1024;

    do {
        char* buffer = (char*)SecureAlloc(bufferSize);
        int bytesReceived = LI_FN(recv)(sock, buffer, bufferSize - 1, 0);
        if (bytesReceived > 0) {
            char* newResponse = (char*)SecureAlloc(*length + bytesReceived + 1);
            if (*response != NULL) {
                __movsb((PBYTE)newResponse, (PBYTE)*response, *length);
                SecureFree(*response);
            }
            __movsb((PBYTE)newResponse + *length, (PBYTE)buffer, bytesReceived);
            *response = newResponse;
            *length += bytesReceived;
            (*response)[*length] = 0;
            result = true;
        }
        SecureFree(buffer);
        if (bytesReceived <= 0) break;
    } while (true);

    LI_FN(closesocket)(sock);
    LI_FN(WSACleanup)();

    return result;
}