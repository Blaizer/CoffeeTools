/*
[GUI only]
Patches the installed game from Steam, doing a backup of the game data in the `versions` folder beforehand
Wrapper for ./Patcher.csx
*/

#load "./lib/_Utils.csx"
#load "./lib/_MainWindow.csx"

using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

var ufo50Dir = Environment.GetEnvironmentVariable("UFO50_DIR");
if(ufo50Dir == null) {
    ufo50Dir = FindUFO50Dir();
}

if(ufo50Dir == null) {
    ufo50Dir = PromptChooseDirectory();
}

if(ufo50Dir == null) {
    throw new ScriptException("Cancelled");
}

if(!IsUFO50Dir(ufo50Dir)) {
    throw new ScriptException("Invalid UFO50 dir");
}

Environment.SetEnvironmentVariable("UFO50_DIR", ufo50Dir);

var rawExeVersion = FileVersionInfo.GetVersionInfo(Path.Join(ufo50Dir, "ufo50.exe")).ProductVersion;
var exeVersion = Version.Parse(rawExeVersion);

var ufo50Version = new Version(exeVersion.Major, exeVersion.Minor, exeVersion.Build, exeVersion.Revision);
Environment.SetEnvironmentVariable("UFO50_EXPECTED_VERSION", $"{ufo50Version}");

var versionDir = GetVersionDir(ufo50Version);
var ufo50BackupDataFile = Path.Join(versionDir, "data.win");
var ufo50MainDataFile = Path.Join(ufo50Dir, "data.win");

if(!File.Exists(ufo50BackupDataFile)) {
    SetProgressBar("Copying original file", ufo50BackupDataFile, 0, 1);
    Directory.CreateDirectory(versionDir);
    File.Copy(ufo50MainDataFile, ufo50BackupDataFile);
    IncrementProgress();
}

await MainWindowLoadFile(ufo50BackupDataFile, true);
await MainWindowRunScript(Path.Join(Path.GetDirectoryName(GetCurrentScript()), "Patcher.csx"));
if(ScriptExecutionSuccess) {
    await MainWindowSaveFile(ufo50MainDataFile, true);
}

// -----------------------------------
bool IsUFO50Dir(string dir) {
    return File.Exists(Path.Join(dir, "ufo50.exe")) && File.Exists(Path.Join(dir, "data.win"));
}

string FindUFO50Dir() {
    foreach(var steamDir in ListSteamDirectories()) {
        var ufo50Dir = Path.Join(steamDir, @"steamapps\common\UFO 50");
        if(IsUFO50Dir(ufo50Dir)) {
            return ufo50Dir;
        }
    }

    return null;
}

IEnumerable<string> ListSteamDirectories()
{
    var defaultSteamDir = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", @"C:\Program Files (x86)\Steam");
    if(!Directory.Exists(defaultSteamDir)) {
        yield break;
    }
    yield return defaultSteamDir;

    var libraryFoldersVdfPath = Path.Join(defaultSteamDir, @"steamapps\libraryfolders.vdf");
    if(!File.Exists(libraryFoldersVdfPath)) {
        yield break;
    }

    var libraryFoldersVdf = File.ReadAllText(libraryFoldersVdfPath);
    string pattern = @"^\s*""path""\s*""([A-Z]:[^""]+)""\s*$";
    foreach (Match m in Regex.Matches(libraryFoldersVdf, pattern, RegexOptions.Multiline)) {
        yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }
}
