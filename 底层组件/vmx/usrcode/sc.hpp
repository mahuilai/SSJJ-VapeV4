#pragma once
#define SC_EXPORT extern "C" _declspec(dllexport)
#define SC_EXPORT_DATA(type, data) extern "C" _declspec(dllexport) type data;