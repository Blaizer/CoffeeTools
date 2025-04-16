/*
Imports code in `UFO50_Code` subfolder next to the currently opened file

If `modified.txt` present in that folder, only imports files newer than the timestamp found in that file

Use in pair with the builtin code export script in UMT and `tool/MoveExportedCode.au3` to quickly edit GML outside of UMT
*/

#load "./lib/_Utils.csx"
#load "./lib/_Patch.csx"
#load "./lib/_UFO50.csx"

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Threading.Tasks;

EnsureDataLoaded();

var scriptDir = Path.Join(GetCurrentUFO50Dir(), "UFO50_Code");

DateTime modifiedDate = DateTime.MinValue;
var modifiedFilePath = Path.Join(scriptDir, "modified.txt");
if(File.Exists(modifiedFilePath)) {
    var modifiedDateRaw = Regex.Replace(File.ReadAllText(modifiedFilePath), @"[^\d]+", "");
    if(modifiedDateRaw.Length >= 14) {
        modifiedDate = new DateTime(
            Int32.Parse(modifiedDateRaw.Substring(0, 4)),
            Int32.Parse(modifiedDateRaw.Substring(4, 2)),
            Int32.Parse(modifiedDateRaw.Substring(6, 2)),
            Int32.Parse(modifiedDateRaw.Substring(8, 2)),
            Int32.Parse(modifiedDateRaw.Substring(10, 2)),
            Int32.Parse(modifiedDateRaw.Substring(12, 2)),
            DateTimeKind.Utc
        );
    }
}

modifiedDate = modifiedDate.AddSeconds(1);

var scripts = new List<string>();
SetProgressBar("Finding modified scripts", scriptDir, 0, 1);
foreach (string scriptPath in Directory.EnumerateFiles(scriptDir, "*.gml", SearchOption.AllDirectories))
{
    if(File.GetLastWriteTimeUtc(scriptPath) > modifiedDate) {
        scripts.Add(scriptPath);
    }
}
IncrementProgress();

if (scripts.Count > 0) {
    SetProgressBar("Importing modified scripts", "", 0, scripts.Count);
    await ImportCodeFiles(scripts.ToArray(), true);
}
