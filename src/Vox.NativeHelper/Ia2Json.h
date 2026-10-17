#pragma once
#include <string>
#include <cstdio>

// Minimal JSON writer helper for the snapshot (strings escaped; control characters as \u00XX).
namespace ia2json
{
    inline void Escape(std::wstring& out, const wchar_t* s)
    {
        out += L'"';
        for (; s && *s; ++s)
        {
            wchar_t c = *s;
            switch (c)
            {
            case L'"': out += L"\\\""; break;
            case L'\\': out += L"\\\\"; break;
            case L'\n': out += L"\\n"; break;
            case L'\r': out += L"\\r"; break;
            case L'\t': out += L"\\t"; break;
            default:
                if (c < 0x20)
                {
                    wchar_t buf[8];
                    swprintf_s(buf, L"\\u%04x", static_cast<unsigned>(c));
                    out += buf;
                }
                else
                {
                    out += c;
                }
            }
        }
        out += L'"';
    }
}
