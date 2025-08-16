/*
Generates a .diff file inside the `build` folder

This compares code between what is in UMT and the actual file
*/

#load "./lib/_Utils.csx"
#load "./lib/_Patch.csx"
#load "./lib/_UFO50.csx"

using System;
using System.IO;
using System.Windows.Input;

EnsureDataLoaded();
var ufo50Version = GetUFO50Version(Data);

SetProgressBar("Loading original", "...", 0, 0);
StartProgressBarUpdater();
var originalFilePath = Path.Combine(GetVersionDir(ufo50Version), "data.win");
var originalData = await LoadExternalData(originalFilePath, true);

var originalUfo50Version = GetUFO50Version(originalData);

if(ufo50Version != originalUfo50Version) {
    throw new ScriptException($"UFO50 version mismatch (patched = {ufo50Version}; original = {originalUfo50Version})");
}

SetProgressBar("Generating code patch", "...", 0, 0);
var changed = await CompareCode(Data, originalData, true);
changed.Sort();
var diff = await GenerateCodePatch(Data, originalData, changed, true);
var diffPath = Path.Join(GetBuildDir(), $"{ufo50Version}.code.diff");
File.WriteAllText(diffPath, diff);
var fileList = GenerateFileList(diff);
var fileListPath = Path.Join(GetBuildDir(), "files.txt");
File.WriteAllText(fileListPath, fileList);
await StopProgressBarUpdater();
HideProgressBar();
ScriptMessage($"Diff created at {diffPath}");
