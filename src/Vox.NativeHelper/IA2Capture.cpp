#include <windows.h>
#include <oleacc.h>
#include <servprov.h>
#include <atlbase.h>
#include <atlcomcli.h>
#include <vector>
#include <string>
#include "IA2Capture.h"
#include "Ia2Json.h"

// IAccessible2 is declared by the IA2 IDL (ia2_api_all.h) and its proxy/stub comes with the IA2 SDK
// (https://github.com/LinuxA11y/IAccessible2); CI fetches both into third_party/ia2.
#include "ia2_api_all.h"

#pragma comment(lib, "oleacc.lib")

namespace
{
    struct Capture
    {
        std::wstring json;
        int count = 0;
        int max = 0;
    };

    // IA2 attribute strings are "key:value;key:value" with backslash escapes; written as a JSON object.
    void AttributesToJson(std::wstring& out, const wchar_t* attrs)
    {
        out += L'{';
        bool first = true;
        std::wstring s = attrs ? attrs : L"";
        size_t i = 0;
        while (i < s.size())
        {
            std::wstring key, val;
            std::wstring* cur = &key;
            for (; i < s.size() && s[i] != L';'; ++i)
            {
                if (s[i] == L'\\' && i + 1 < s.size()) { cur->push_back(s[++i]); continue; }
                if (s[i] == L':' && cur == &key) { cur = &val; continue; }
                cur->push_back(s[i]);
            }
            ++i;
            if (key.empty()) continue;
            if (!first) out += L',';
            first = false;
            ia2json::Escape(out, key.c_str());
            out += L':';
            ia2json::Escape(out, val.c_str());
        }
        out += L'}';
    }

    CComPtr<IAccessible2> ToIA2(IAccessible* acc)
    {
        CComPtr<IServiceProvider> sp;
        CComPtr<IAccessible2> ia2;
        if (SUCCEEDED(acc->QueryInterface(&sp)))
            sp->QueryService(IID_IAccessible2, IID_IAccessible2, reinterpret_cast<void**>(&ia2));
        return ia2;
    }

    void WriteRelations(Capture& cap, IAccessible2* ia2)
    {
        long nRelations = 0;
        if (FAILED(ia2->get_nRelations(&nRelations)) || nRelations <= 0) return;
        cap.json += L",\"relations\":{";
        bool firstRel = true;
        for (long r = 0; r < nRelations && r < 16; ++r)
        {
            CComPtr<IAccessibleRelation> rel;
            if (FAILED(ia2->get_relation(r, &rel)) || !rel) continue;
            CComBSTR type;
            long nTargets = 0;
            if (FAILED(rel->get_relationType(&type)) || FAILED(rel->get_nTargets(&nTargets))) continue;
            if (!firstRel) cap.json += L',';
            firstRel = false;
            ia2json::Escape(cap.json, type);
            cap.json += L":[";
            bool firstT = true;
            for (long t = 0; t < nTargets && t < 32; ++t)
            {
                CComPtr<IUnknown> target;
                if (FAILED(rel->get_target(t, &target)) || !target) continue;
                CComPtr<IAccessible2> targetIa2;
                long tid = 0;
                if (SUCCEEDED(target->QueryInterface(&targetIa2)) && SUCCEEDED(targetIa2->get_uniqueID(&tid)))
                {
                    if (!firstT) cap.json += L',';
                    firstT = false;
                    cap.json += std::to_wstring(tid);
                }
            }
            cap.json += L']';
        }
        cap.json += L'}';
    }

    void WriteTextRuns(Capture& cap, IAccessible2* ia2)
    {
        CComPtr<IAccessibleText> text;
        long nChars = 0;
        if (FAILED(ia2->QueryInterface(&text)) || FAILED(text->get_nCharacters(&nChars)) || nChars <= 0) return;
        cap.json += L",\"textRuns\":[";
        bool firstRun = true;
        long offset = 0;
        for (int guard = 0; offset < nChars && guard < 256; ++guard)
        {
            long start = 0, end = 0;
            CComBSTR runAttrs;
            if (FAILED(text->get_attributes(offset, &start, &end, &runAttrs)) || end <= offset) break;
            if (runAttrs.Length() > 0)
            {
                if (!firstRun) cap.json += L',';
                firstRun = false;
                cap.json += L"{\"start\":" + std::to_wstring(start) + L",\"end\":" + std::to_wstring(end) + L",\"attributes\":";
                AttributesToJson(cap.json, runAttrs);
                cap.json += L'}';
            }
            offset = end;
        }
        cap.json += L']';
    }

    void Walk(Capture& cap, IAccessible* acc, int depth)
    {
        if (cap.count >= cap.max || depth > 256)
        {
            cap.json += L"{}";
            return;
        }
        ++cap.count;

        CComPtr<IAccessible2> ia2 = ToIA2(acc);
        CComVariant self(CHILDID_SELF);
        CComBSTR name, value, description;
        acc->get_accName(self, &name);
        acc->get_accValue(self, &value);
        acc->get_accDescription(self, &description);
        CComVariant role, state;
        acc->get_accRole(self, &role);
        acc->get_accState(self, &state);

        long id = 0;
        long ia2Role = 0;
        long long states = state.vt == VT_I4 ? static_cast<unsigned long>(state.lVal) : 0;
        long roleValue = role.vt == VT_I4 ? role.lVal : 0;
        if (ia2)
        {
            ia2->get_uniqueID(&id);
            if (SUCCEEDED(ia2->role(&ia2Role)) && ia2Role != 0) roleValue = ia2Role;
            AccessibleStates ia2State = 0;
            if (SUCCEEDED(ia2->get_states(&ia2State))) states |= static_cast<long long>(ia2State) << 32;
        }

        cap.json += L"{\"id\":" + std::to_wstring(id);
        cap.json += L",\"role\":" + std::to_wstring(roleValue);
        cap.json += L",\"states\":" + std::to_wstring(states);
        cap.json += L",\"name\":";
        ia2json::Escape(cap.json, name);
        cap.json += L",\"value\":";
        ia2json::Escape(cap.json, value);
        cap.json += L",\"description\":";
        ia2json::Escape(cap.json, description);

        if (ia2)
        {
            CComBSTR attrs;
            if (SUCCEEDED(ia2->get_attributes(&attrs)) && attrs.Length() > 0)
            {
                cap.json += L",\"attributes\":";
                AttributesToJson(cap.json, attrs);
            }
            WriteRelations(cap, ia2);
            WriteTextRuns(cap, ia2);
        }

        long childCount = 0;
        if (SUCCEEDED(acc->get_accChildCount(&childCount)) && childCount > 0)
        {
            std::vector<CComVariant> children(childCount);
            long obtained = 0;
            if (SUCCEEDED(AccessibleChildren(acc, 0, childCount, children.data(), &obtained)) && obtained > 0)
            {
                cap.json += L",\"children\":[";
                bool firstChild = true;
                for (long i = 0; i < obtained; ++i)
                {
                    if (children[i].vt != VT_DISPATCH || !children[i].pdispVal) continue;
                    CComPtr<IAccessible> child;
                    if (FAILED(children[i].pdispVal->QueryInterface(&child))) continue;
                    if (!firstChild) cap.json += L',';
                    firstChild = false;
                    Walk(cap, child, depth + 1);
                }
                cap.json += L']';
            }
        }
        cap.json += L'}';
    }

    CComPtr<IAccessible> RootOf(HWND window)
    {
        CComPtr<IAccessible> acc;
        if (FAILED(AccessibleObjectFromWindow(window, OBJID_CLIENT, IID_IAccessible, reinterpret_cast<void**>(&acc))))
            return nullptr;
        return acc;
    }
}

namespace Vox
{
namespace NativeHelper
{
    bool IA2Capture::IsAvailable(IntPtr window)
    {
        try
        {
            auto root = RootOf(static_cast<HWND>(window.ToPointer()));
            return root && ToIA2(root) != nullptr;
        }
        catch (...)
        {
            return false;
        }
    }

    String^ IA2Capture::CaptureDocument(IntPtr window, int maxNodes)
    {
        try
        {
            auto root = RootOf(static_cast<HWND>(window.ToPointer()));
            if (!root || !ToIA2(root)) return nullptr;
            Capture cap;
            cap.max = maxNodes > 0 ? maxNodes : 20000;
            Walk(cap, root, 0);
            return gcnew String(cap.json.c_str());
        }
        catch (...)
        {
            return nullptr;
        }
    }
}
}
