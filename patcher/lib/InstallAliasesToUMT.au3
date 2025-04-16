#comments-start
    Installs aliases on your default UMT script folder for the scripts contained in this repo

	This will make the scripts appear as a "UFO 50" context menu entry in the script menu of UMT
#comments-end
#NoTrayIcon

#include <File.au3>
#include '_UMT.au3'

Func _AliasUMTScripts($sScriptDir, $sAliasDir)
	$aScripts = _FileListToArray($sScriptDir, '*.csx', $FLTA_FILES)
	For $iA = 1 To $aScripts[0]
		If StringLeft($aScripts[$iA], 1) == '_' Then ContinueLoop
		$hFile = FileOpen($sAliasDir&'\'&$aScripts[$iA], $FO_OVERWRITE + $FO_CREATEPATH)
		If $hFile == -1 Then _Fatal('Failed to create alias for script: '&$aScripts[$iA])
		FileWriteLine($hFile, '#load "'&$sScriptDir&'\'&$aScripts[$iA]&'"')
		FileClose($hFile)
	Next
EndFunc

$sAliasDir = $UMT_DIR&'\Scripts\UFO50'
_AliasUMTScripts(@ScriptDir, $sAliasDir)
_AliasUMTScripts(_PathFull('..', @ScriptDir), $sAliasDir)

MsgBox(0, @ScriptName, 'Scripts installed to '&$sAliasDir)
