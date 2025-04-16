#comments-start
    Spawns UMT to patch the steam folder of UFO 50 and then runs the game on patch sucess
    Defaults to all patches, specify a comma-separated list of patches in `../build/patches.txt`

    Warnings:
        - This will terminate any running UFO 50 instances before patching
        - This probably will not work unless you're using a custom UMT that allows the command used here
#comments-end
#NoTrayIcon

#include <File.au3>
#include '_UMT.au3'

EnvSet('UFO50_PATCHES', '*')
If FileExists(@ScriptDir&'\..\build\patches.txt') Then EnvSet('UFO50_PATCHES', FileRead(@ScriptDir&'\..\build\patches.txt'))

ProcessClose('ufo50.exe')
$iRc = RunWait('"'&$UMT_EXE&'" "'&_PathFull('../AutoDetectAndPatch.csx')&'" _runscriptandexit', @ScriptDir)
If $iRc <> 0 Then Exit $iRc
ShellExecute('steam://rungameid/1147860')
