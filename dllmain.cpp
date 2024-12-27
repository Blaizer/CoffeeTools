#include "framework.h"
#include "minhook/include/MinHook.h"
#include "YYRValue.h"

YYRunnerInterface* g_pYYRunnerInterface;

namespace
{
    YYRunnerInterface g_runnerInterface;
    CInstance* g_selfinst;

    void* g_origRunloop;
    void* g_origWndproc;
    void* g_origCheckAudioGroupsLoaded;

    int gml_Script_savestateSlot;
    int gml_Script_modifyFps;
    int gml_Script_incrementFrame;
    int gml_game_load;

    int g_savestateSlot;
    int g_changeSpeed;

    bool g_inRunloop;
    bool g_isQuitting;
    bool g_isPaused;
    bool g_isFrameAdvancing;
    bool g_isSaving;
    bool g_resetSpeed;

    LRESULT Wndproc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WM_QUIT || message == WM_CLOSE)
        {
            g_isQuitting = true;
        }
        else if (message == WM_KEYDOWN)
        {
            if (wParam == VK_SPACE)
            {
                if (GetKeyState(VK_CONTROL) < 0)
                {
                    g_isPaused = false;
                    g_isFrameAdvancing = true;
                }
                else if (g_isPaused)
                {
                    g_isFrameAdvancing = true;
                }
                else
                {
                    g_isPaused = true;
                }
            }
            else if (wParam == VK_BACK)
            {
                if (((lParam >> 16) & KF_REPEAT) == 0)
                {
                    if (g_isPaused)
                    {
                        g_isPaused = false;
                    }
                    else
                    {
                        g_resetSpeed = true;
                        g_changeSpeed = 0;
                    }
                }
            }
            else if (wParam == VK_OEM_MINUS || wParam == VK_OEM_PLUS)
            { 
                g_changeSpeed = wParam == VK_OEM_PLUS ? 1 : -1;
            }
            else if (wParam >= '0' && wParam <= '9')
            {
                if (((lParam >> 16) & KF_REPEAT) == 0)
                {
                    g_savestateSlot = wParam == '0' ? 10 : (int)wParam - '0';
                    g_isSaving = GetKeyState(VK_SHIFT) < 0;
                }
            }
        }

        return ((decltype(&Wndproc))g_origWndproc)(hwnd, message, wParam, lParam);
    }

    void PerformActions()
    {
        if (g_selfinst)
        {
            if (g_savestateSlot)
            {
                RValue result = {};
                RValue arg[2] = {};
                arg[0].val = g_isSaving;
                arg[0].kind = VALUE_BOOL;
                arg[1].val = g_savestateSlot;
                Script_Perform(gml_Script_savestateSlot, g_selfinst, g_selfinst, 2, &result, arg);
                FREE_RValue(&result);
                g_savestateSlot = 0;
            }

            if (g_resetSpeed || g_changeSpeed || g_isFrameAdvancing)
            {
                RValue result = {};
                RValue arg[2] = {};
                arg[0].val = g_resetSpeed;
                arg[0].kind = VALUE_BOOL;
                arg[1].val = g_changeSpeed;
                Script_Perform(gml_Script_modifyFps, g_selfinst, g_selfinst, 2, &result, arg);
                FREE_RValue(&result);
                g_resetSpeed = false;
                g_changeSpeed = 0;
            }
        }
    }

    void RefreshScreen()
    {
        ((decltype(&RefreshScreen))0x140009540)(); // this seems to be the function that refreshes the screen
    }

    int Runloop()
    {
        DWORD lastRefreshTime = timeGetTime();

        g_inRunloop = true;

        PerformActions();
        while (g_isPaused && !g_isQuitting && !g_isFrameAdvancing)
        {
            ((void (*)())0x140716c50)(); // poll message queue
            PerformActions();

            DWORD time = timeGetTime();
            DWORD timeSinceLastRefresh = time - lastRefreshTime;
            if (timeSinceLastRefresh >= 16)
            {
                lastRefreshTime = time;
                RefreshScreen(); // so that the steam overlay still works while paused
            }
            else
            {
                MsgWaitForMultipleObjects(0, nullptr, FALSE, 16 - timeSinceLastRefresh, QS_ALLINPUT);
            }
        }
        g_isFrameAdvancing = false;

        if (g_selfinst)
        {
            RValue result = {};
            Script_Perform(gml_Script_incrementFrame, g_selfinst, g_selfinst, 0, &result, nullptr);
            FREE_RValue(&result);
        }

        g_inRunloop = false;

        return ((decltype(&Runloop))g_origRunloop)();
    }

    void CheckAudioGroupsLoaded(intptr_t arg)
    {
        struct Group
        {
            Group* next;
            intptr_t unk1;
            intptr_t something;
            intptr_t val;
        };

        // wait until all loading audio groups have finished loading (happens in a worker thread)
        // this essentially makes audio group loading synchronous and prevents desyncs
        auto a = *(Group**)(arg + 8);
        for (auto g = a->next; g != a; g = g->next)
        {
            if (g->val != 0 && g->something != 0 && *(int*)g->val == 1)
            {
                while (*(bool*)(g->val + 0x11) == 0)
                {
                    MsgWaitForMultipleObjects(0, nullptr, FALSE, 1, 0);
                }
            }
        }

        ((decltype(&CheckAudioGroupsLoaded))g_origCheckAudioGroupsLoaded)(arg);
    }
}

YYEXPORT void YYExtensionInitialise(const struct YYRunnerInterface* _pFunctions, size_t _functions_size)
{
    if (_functions_size < sizeof(YYRunnerInterface))
    {
        YYError("RunnerInterface size mismatch in CoffeeTools extension DLL!");
        return;
    }

    memcpy(&g_runnerInterface, _pFunctions, sizeof(YYRunnerInterface));
    g_pYYRunnerInterface = &g_runnerInterface;

    if (MH_Initialize() != MH_OK)
    {
        YYError("MH_Initialize failed!");
        return;
    }

    if (MH_CreateHook((void*)0x1401c8090, &Runloop, &g_origRunloop) != MH_OK)
    {
        YYError("MH_CreateHook Runloop failed!");
        return;
    }

    if (MH_CreateHook((void*)0x1400d3d90, &Wndproc, &g_origWndproc) != MH_OK)
    {
        YYError("MH_CreateHook Wndproc failed!");
        return;
    }

    if (MH_CreateHook((void*)0x14083b400, &CheckAudioGroupsLoaded, &g_origCheckAudioGroupsLoaded) != MH_OK)
    {
        YYError("MH_CreateHook CheckAudioGroupsLoaded failed!");
        return;
    }

    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
    {
        YYError("MH_EnableHook failed!");
        return;
    }

    // start the game paused if you launch the game while holding space   
    if (GetKeyState(VK_SPACE) < 0)
    {
        g_isPaused = true;
    }

    DebugConsoleOutput("CoffeeTools YYExtensionInitialise CONFIGURED\n");
}

YYEXPORT void ct_update(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    if (!g_selfinst)
    {
        Code_Function_Find("game_load", &gml_game_load);
        if (gml_game_load == -1)
        {
            YYError("Failed to find game_load!");
            return;
        }

        gml_Script_savestateSlot = Script_Find_Id("gml_Script_savestateSlot");
        if (gml_Script_savestateSlot == -1)
        {
            YYError("Failed to find gml_Script_savestateSlot!");
            return;
        }

        gml_Script_modifyFps = Script_Find_Id("gml_Script_modifyFps");
        if (gml_Script_modifyFps == -1)
        {
            YYError("Failed to find gml_Script_modifyFps!");
            return;
        }

        gml_Script_incrementFrame = Script_Find_Id("gml_Script_incrementFrame");
        if (gml_Script_incrementFrame == -1)
        {
            YYError("Failed to find gml_Script_incrementFrame!");
            return;
        }
    }

    g_selfinst = selfinst;
}

YYEXPORT void ct_refresh_screen(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    RefreshScreen();
}

YYEXPORT void ct_is_paused(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.val = g_isPaused;
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_in_runloop(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.val = g_inRunloop;
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_output_debug_string(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    OutputDebugStringA(arg->GetString());
}

YYEXPORT void ct_game_load(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    Script_Perform(gml_game_load, selfinst, otherinst, argc, &result, arg);

    ((int(*)())0x1401c1f40)(); // this function actually loads the save file
}
