#include "framework.h"
#include "minhook/include/MinHook.h"

#define __YYDEFINE_EXTENSION_FUNCTIONS__
#include "Extension_Interface.h"
#include "Ref.h"
#include "YYRValue.h"
#define YYEXPORT __declspec(dllexport)

#include "version.h"

YYRunnerInterface* g_pYYRunnerInterface;

namespace
{
    constexpr char c_ExtensionName[] = "CoffeeTools";
    constexpr char c_ExtensionVersion[] = MOD_VERSION;

    constexpr size_t c_exe_run_loop = 0x14017c390;
    constexpr size_t c_exe_wndproc = 0x1400b7ab0;
    constexpr size_t c_exe_poll_messages = 0x1403a0520;
    constexpr size_t c_exe_refresh_screen = 0x140009c60;
    constexpr size_t c_exe_check_audio_groups_loaded = 0x140494120;
    constexpr size_t c_exe_update_texture_status = 0x1400e2b80;
    constexpr size_t c_exe_perform_game_load = 0x140177e20;
    constexpr size_t c_exe_yygml_exception_handler = 0x14028c480;

    YYRunnerInterface g_runnerInterface;
    CInstance* g_selfinst;

    void* g_origRunloop;
    void* g_origWndproc;
    void* g_origCheckAudioGroupsLoaded;
    void* g_origUpdateTextureStatus;

    int gml_Script_incrementFrame;
    int gml_Script_performActions;
    int gml_Script_refreshScreen;
    int gml_game_load;

    int g_savestateSlot;
    int g_changeSpeed;

    bool g_inRunloop;
    bool g_isQuitting;
    bool g_isPaused;
    bool g_isFrameAdvancing;
    bool g_isSaving;
    bool g_resetSpeed;

    bool g_prevInput[256];
    bool g_input[256];

    LRESULT Wndproc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WM_QUIT || message == WM_CLOSE)
        {
            g_isQuitting = true;
        }

        return ((decltype(&Wndproc))g_origWndproc)(hwnd, message, wParam, lParam);
    }

    void PerformActions()
    {
        memcpy(g_prevInput, g_input, sizeof(g_input));

        if (GetActiveWindow())
        {
            for (int i = 0; i < ARRAYSIZE(g_input); i++)
            {
                g_input[i] = GetKeyState(i) < 0;
            }
        }
        else
        {
            memset(g_input, 0, sizeof(g_input));
        }

        if (g_selfinst)
        {
            RValue result = {};
            Script_Perform(gml_Script_performActions, g_selfinst, g_selfinst, 0, &result, nullptr);
            FREE_RValue(&result);
        }
        else
        {
            // handle input when the game first launches before performAction takes over (hold space when running the game to start paused)
            if (g_input[VK_SPACE] && !g_prevInput[VK_SPACE])
            {
                if (g_input[VK_CONTROL])
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
            else if (g_input[VK_BACK] && !g_prevInput[VK_BACK])
            {
                g_isPaused = false;
            }
        }
    }

    void RefreshScreen()
    {
        if (g_selfinst)
        {
            RValue result = {};
            Script_Perform(gml_Script_refreshScreen, g_selfinst, g_selfinst, 0, &result, nullptr);
            FREE_RValue(&result);
        }

        ((decltype(&RefreshScreen))c_exe_refresh_screen)();
    }

    int Runloop()
    {
        try
        {
            auto lastRefreshTime = Timing_Time();

            g_inRunloop = true;

            while (true)
            {
                PerformActions();

                if (!g_isPaused || g_isQuitting || g_isFrameAdvancing)
                {
                    break;
                }

                auto time = Timing_Time();
                auto timeSinceLastRefresh = time - lastRefreshTime;
                if (timeSinceLastRefresh >= 16667)
                {
                    lastRefreshTime = time;
                    RefreshScreen(); // so that the steam overlay still works while paused
                }
                else
                {
                    Timing_Sleep(16667 - timeSinceLastRefresh);
                }

                ((void (*)())c_exe_poll_messages)();
            }
            g_isFrameAdvancing = false;

            if (g_selfinst)
            {
                RValue result = {};
                Script_Perform(gml_Script_incrementFrame, g_selfinst, g_selfinst, 0, &result, nullptr);
                FREE_RValue(&result);
            }

            g_inRunloop = false;
        }
        catch (YYGMLException e)
        {
            ((void (*)(YYGMLException))c_exe_yygml_exception_handler)(e);
        }

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
                    Timing_Sleep(1000);
                }
            }
        }

        ((decltype(&CheckAudioGroupsLoaded))g_origCheckAudioGroupsLoaded)(arg);
    }

    int UpdateTextureStatus(intptr_t arg1, intptr_t arg2, bool arg3)
    {
        // wait until the texture status has finished being updated (happens in a worker thread)
        // (status 2 updates to status 3 in a separate thread, and then afterwards status 4 updates to status 6 in another thread)
        // with this, we essentially make texture loading as fast as possible, since we always update our status in as few frames as possible, fixing desyncs
        if (arg2 != 0)
        {
            auto status = *(int*)(arg2 + 0x3c);
            if (status == 2 || status == 4)
            {
                // the function that calls this function enters us into a critical section, but we need to get out in order to let the worker thread do its thing
                if (arg1 != 0) LeaveCriticalSection(**(LPCRITICAL_SECTION**)(arg1 + 0x50));

                do
                {
                    Timing_Sleep(1000);
                    status = *(int*)(arg2 + 0x3c);
                }
                while (status == 2 || status == 4);

                if (arg1 != 0) EnterCriticalSection(**(LPCRITICAL_SECTION**)(arg1 + 0x50));
            }
        }

        return ((decltype(&UpdateTextureStatus))g_origUpdateTextureStatus)(arg1, arg2, arg3);
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

    auto version = extGetVersion(c_ExtensionName);
    if (strncmp(version, c_ExtensionVersion, ARRAYSIZE(c_ExtensionVersion) - 1) != 0)
    {
        YYError("Extension DLL version mismatch: expected v%s but CoffeeTools.dll is v%s\nMake sure you copied the matching CoffeeTools.dll into the same folder as data.win.", version, c_ExtensionVersion);
        return;
    }

    Code_Function_Find("game_load", &gml_game_load);
    if (gml_game_load == -1)
    {
        YYError("Failed to find game_load!");
        return;
    }

    if (MH_Initialize() != MH_OK)
    {
        YYError("MH_Initialize failed!");
        return;
    }

    if (MH_CreateHook((void*)c_exe_run_loop, &Runloop, &g_origRunloop) != MH_OK)
    {
        YYError("MH_CreateHook Runloop failed!");
        return;
    }

    if (MH_CreateHook((void*)c_exe_wndproc, &Wndproc, &g_origWndproc) != MH_OK)
    {
        YYError("MH_CreateHook Wndproc failed!");
        return;
    }

    if (MH_CreateHook((void*)c_exe_check_audio_groups_loaded, &CheckAudioGroupsLoaded, &g_origCheckAudioGroupsLoaded) != MH_OK)
    {
        YYError("MH_CreateHook CheckAudioGroupsLoaded failed!");
        return;
    }

    if (MH_CreateHook((void*)c_exe_update_texture_status, &UpdateTextureStatus, &g_origUpdateTextureStatus) != MH_OK)
    {
        YYError("MH_CreateHook UpdateTextureStatus failed!");
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

YYEXPORT void ct_init(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    if (!g_selfinst)
    {
        gml_Script_incrementFrame = Script_Find_Id("gml_Script_incrementFrame");
        if (gml_Script_incrementFrame == -1)
        {
            YYError("Failed to find gml_Script_incrementFrame!");
            return;
        }

        gml_Script_performActions = Script_Find_Id("gml_Script_performActions");
        if (gml_Script_performActions == -1)
        {
            YYError("Failed to find gml_Script_performActions!");
            return;
        }

        gml_Script_refreshScreen = Script_Find_Id("gml_Script_refreshScreen");
        if (gml_Script_refreshScreen == -1)
        {
            YYError("Failed to find gml_Script_refreshScreen!");
            return;
        }
    }

    g_selfinst = selfinst;

    result.val = 1;
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_is_paused(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.val = g_isPaused;
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_set_paused(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    g_isPaused = YYGetBool(arg, 0);

    if (argc > 1)
    {
        g_isFrameAdvancing = YYGetBool(arg, 1);
    }
}

YYEXPORT void ct_in_runloop(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.val = g_inRunloop;
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_output_debug_string(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    OutputDebugStringA(YYGetString(arg, 0));
}

YYEXPORT void ct_game_load(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    Script_Perform(gml_game_load, selfinst, otherinst, argc, &result, arg);

    ((int(*)())c_exe_perform_game_load)();
}

YYEXPORT void ct_is_ref(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.val = arg[0].kind == VALUE_REF;
    result.kind = VALUE_REF;
}

YYEXPORT void ct_ref_to_int64(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.v64 = arg[0].v64;
    result.kind = VALUE_INT64;
}

YYEXPORT void ct_int64_to_ref(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    result.v64 = arg[0].v64;
    result.kind = VALUE_REF;
}

YYEXPORT void ct_keyboard_check(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    int vk = YYGetInt32(arg, 0);
    result.val = g_input[vk];
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_keyboard_check_pressed(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    int vk = YYGetInt32(arg, 0);
    result.val = g_input[vk] && !g_prevInput[vk];
    result.kind = VALUE_BOOL;
}

YYEXPORT void ct_keyboard_check_released(RValue& result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    int vk = YYGetInt32(arg, 0);
    result.val = !g_input[vk] && g_prevInput[vk];
    result.kind = VALUE_BOOL;
}
