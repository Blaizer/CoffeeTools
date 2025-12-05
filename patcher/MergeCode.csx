#load "./lib/_Utils.csx"
#load "./lib/_Patch.csx"
#load "./lib/_UFO50.csx"

using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Input;

var diff3Path = "C:/Program Files/Git/usr/bin/diff3.exe";

EnsureDataLoaded();
var oldVersion = GetGameVersion();
var newVersion = GetUFO50Version(Data);
var modName = Path.GetFileName(Directory.GetDirectories(GetPatchesDir())[0]);

SetProgressBar("Loading base", "...", 0, 0);
StartProgressBarUpdater();
var baseFilePath = Path.Combine(GetVersionDir(oldVersion), "data.win");
var baseData = await LoadExternalData(baseFilePath, true);

SetProgressBar("Loading new", "...", 0, 0);
var newFilePath = Path.Combine(GetVersionDir(newVersion), "data.win");
var newData = await LoadExternalData(newFilePath, true);

var mergeDir = GetMergeDir();
var conflicts = new List<string>();

string GetRenamedScriptName(string script)
{
    var renameFrom = "gml_Object_oSteamController_";
    var renameTo = "gml_Object_oController_";

    if (script.StartsWith(renameFrom))
    {
        script = renameTo + script.Substring(renameFrom.Length);
    }

    return script;
}

{
    SetProgressBar("Exporting code", "...", 0, 0);
    var patchPath = Path.Join(GetPatchesDir(), modName, $"{oldVersion}.code.diff");
    var scriptNames = _ReadScriptNamesInCodePatch(patchPath);

    using var tempDir = new TempDirectory(GetBuildDir());

    var newTempDir = Path.Combine(tempDir.Path, "theirs");
    Directory.CreateDirectory(newTempDir);
    await ExportSpecificCodeToDir(newData, scriptNames.ConvertAll(s => GetRenamedScriptName(s)), newTempDir);

    var baseTempDir = Path.Combine(tempDir.Path, "base");
    Directory.CreateDirectory(baseTempDir);
    await ExportSpecificCodeToDir(baseData, scriptNames, baseTempDir);

    var patchedTempDir = Path.Combine(tempDir.Path, "ours");
    Directory.CreateDirectory(patchedTempDir);
    foreach (string filePath in Directory.GetFiles(baseTempDir))
    {
        string fileName = Path.GetFileName(filePath);
        string destPath = Path.Combine(patchedTempDir, fileName);

        File.Copy(filePath, destPath, overwrite: true);
    }

    await BusyBox("patch", patchedTempDir, new[] {"-i", patchPath});

    Directory.CreateDirectory(mergeDir);
    if (Directory.GetFiles(mergeDir).Length != 0)
    {
        throw new ScriptException($"Merge dir '${mergeDir}' is not empty");
    }

    SetProgressBar("Merging code", "...", 0, 0);
    foreach (var script in scriptNames)
    {
        var scriptName = $"{script}.gml";
        var newScript = GetRenamedScriptName(script);
        var newScriptName = $"{newScript}.gml";

        if (!File.Exists(Path.Join(newTempDir, newScriptName)))
        {
            conflicts.Add(script + " (deleted)");
            continue;
        }

        var startInfo = new ProcessStartInfo { 
            FileName = diff3Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = tempDir.Path,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(Path.Join(Path.GetFileName(newTempDir), newScriptName));
        startInfo.ArgumentList.Add(Path.Join(Path.GetFileName(baseTempDir), scriptName));
        startInfo.ArgumentList.Add(Path.Join(Path.GetFileName(patchedTempDir), scriptName));
        startInfo.EnvironmentVariables["PATH"] = Path.GetDirectoryName(diff3Path);

        using (Process p = Process.Start(startInfo))
        {
            string res = null;
            string err = null;

            var tasks = new List<Task>();
            tasks.Add(Task.Run(() => res = p.StandardOutput.ReadToEnd()));
            tasks.Add(Task.Run(() => err = p.StandardError.ReadToEnd()));
            tasks.Add(Task.Run(() => p.WaitForExit()));
            p.StandardInput.Close();
            await Task.WhenAll(tasks);

            if (p.ExitCode == 1)
            {
                conflicts.Add(newScript);
                res = RemoveNewFileDiffs(res, "||||||| ", "=======");
            }
            else if (p.ExitCode != 0)
            {
                throw new ScriptException($"diff3 exited with code {p.ExitCode}:\n\nfile: {newScript}\n\n{err}");
            }

            File.WriteAllText(Path.Join(mergeDir, newScriptName), res);
        }
    }
}

await StopProgressBarUpdater();
HideProgressBar();

var message = $"Merge of {newVersion} into {oldVersion} created at {mergeDir}";
if (conflicts.Count > 0)
{
    message += "\n\nConflicts:\n";
    foreach (var conflict in conflicts)
    {
        message += $"  {conflict}\n";
    }
}
ScriptMessage(message);
