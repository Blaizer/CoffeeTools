#NoTrayIcon

#include <File.au3>

Func _Fatal($sMsg)
	MsgBox(16, @ScriptName, $sMsg)
	Exit 1
EndFunc

Func _FindUMTExe()
	$sUMTCmd = RegRead('HKEY_CURRENT_USER\Software\Classes\UndertaleModTool\shell\open\command', '')
	If $sUMTCmd == '' Then Return ''

	$sUMTCmdPrefix = '"'
	$sUMTCmdSuffix = '" "%1"'
	If Not StringLeft($sUMTCmd, StringLen($sUMTCmdPrefix)) == $sUMTCmdPrefix Then Return ''
	If Not StringRight($sUMTCmd, StringLen($sUMTCmdSuffix)) == $sUMTCmdSuffix Then Return ''
	Return StringTrimRight(StringTrimLeft($sUMTCmd, StringLen($sUMTCmdPrefix)), StringLen($sUMTCmdSuffix))
EndFunc


Global Const $UMT_EXE = _FindUMTExe()
If $UMT_EXE == "" Or Not FileExists($UMT_EXE) Then _Fatal('Could not find UMT exe')
Global Const $UMT_DIR = _PathFull('..', $UMT_EXE)
