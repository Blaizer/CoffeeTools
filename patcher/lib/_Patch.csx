#load "_Utils.csx"
#load "_Code.csx"
#load "_DiffPatch.csx"

using System;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;

class PatchVersionRange {
    public readonly Version Min;
    public readonly Version Max;

    public PatchVersionRange(Version min, Version max = null) {
        this.Min = min;
        this.Max = max == null ? min : max;
    }

    public PatchVersionRange(string min, string max = null) : this(Version.Parse(min), Version.Parse(max == null ? min : max)) {}

    public bool Matches(Version v) {
        return v >= this.Min && v <= this.Max;
    }

    public bool Matches(string v) {
        return this.Matches(Version.Parse(v));
    }


    public override string ToString() {
        if(this.Min.Equals(this.Max)) {
            return this.Min.ToString();
        }
        return $"{this.Min}-{this.Max}";
    }
}

async Task<KeyValuePair<PatchVersionRange, string>> ApplyCompatibleCodePatch(Version version, string dir, IEnumerable<PatchVersionRange> ranges, bool updateStatus = false) {
    foreach(var range in ranges) {
        if(range.Matches(version)) {
            var scriptFile = Path.Join(dir, $"{range.Min}.code.diff");
            await ApplyCodePatch(scriptFile, updateStatus);
            return new(range, scriptFile);
        }
    }

    throw new ScriptException($"Failed to find compatible patch in {dir}, compatible versions are:\n\n{String.Join(", ", ranges)}");
}

List<string> _ReadScriptNamesInCodePatch(string codePatchPath) {
    var scriptNames = new List<string>();
    var patchesSrc = File.ReadAllText(codePatchPath).ReplaceLineEndings("\n");

    string pattern = @"^\+\+\+ patched/(gml_[\w_]+)\.gml$";
    foreach (Match m in Regex.Matches(patchesSrc, pattern, RegexOptions.Multiline)) {
        scriptNames.Add(m.Groups[1].Value);
    }
    return scriptNames;
}

async Task ApplyCodePatch(string patchPath, bool updateStatus = false) {
    var scriptNames = _ReadScriptNamesInCodePatch(patchPath);

    using var tempDir = new TempDirectory(GetBuildDir());
    var tempDirPath = tempDir.Path;
    await ExportSpecificCodeToDir(Data, scriptNames, tempDirPath, updateStatus ? "Exporting code to be patched" : null);

    {
        string diffText = File.ReadAllText(patchPath);
        var patchFiles = DiffParserHelper.Parse(diffText);

        var conflicts = new List<string>();

        foreach (var patchFile in patchFiles)
        {
            string relativePath = patchFile.To ?? patchFile.From;
            if (string.IsNullOrEmpty(relativePath))
                continue;

            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relativePath = string.Join(Path.DirectorySeparatorChar, parts, 1, parts.Length - 1);

            string filePath = Path.Combine(tempDirPath, relativePath);
            if (!File.Exists(filePath))
            {
                conflicts.Add($"  {relativePath}: file not found in export dir");
                continue;
            }

            string original = File.ReadAllText(filePath);
            var result = PatchHelper.Patch(original, patchFile.Chunks);

            if (result == null)
            {
                conflicts.Add($"  {relativePath}: hunk failed to match");
            }
            else
            {
                File.WriteAllText(filePath, result);
            }
        }

        if (conflicts.Count > 0)
        {
            var msg = "Conflicts:\n" + string.Join("\n", conflicts);
            throw new ScriptException(msg);
        }
    }

    var busyBoxPath = Path.Join(GetToolsDir(), "busybox.exe");
    if (File.Exists(busyBoxPath))
    {
        var tempDir2Path = Path.Join(tempDirPath, "busybox");
        Directory.CreateDirectory(tempDir2Path);
        await ExportSpecificCodeToDir(Data, scriptNames, tempDir2Path, updateStatus ? "Exporting code to be patched" : null);

        await BusyBox("patch", tempDir2Path, new[] {"-i", patchPath}, updateStatus);

        var files1 = Directory.GetFiles(tempDirPath).Select(Path.GetFileName).OrderBy(f => f);
        var files2 = Directory.GetFiles(tempDir2Path).Select(Path.GetFileName).OrderBy(f => f);

        if (!files1.SequenceEqual(files2))
        {
            throw new ScriptException("Diff patch didn't create the same files as busybox");
        }

        var nonMatches = new List<string>();
        foreach (var file in files1)
        {
            if (!File.ReadAllBytes(Path.Combine(tempDirPath, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(tempDir2Path, file))))
            {
                nonMatches.Add(file);
            }
        }

        if (nonMatches.Count > 0)
        {
            var patchFail = Path.Join(GetBuildDir(), "patchfail");
            var patchFail1 = Path.Join(patchFail, "diffpatch");
            var patchFail2 = Path.Join(patchFail, "busybox");
            Directory.CreateDirectory(patchFail);
            Directory.CreateDirectory(patchFail1);
            Directory.CreateDirectory(patchFail2);

            var msg = "Diff patch files failed to match busybox:\n\n";
            foreach (var file in nonMatches)
            {
                File.Copy(Path.Join(tempDirPath, file), Path.Join(patchFail1, file), overwrite: true);
                File.Copy(Path.Join(tempDir2Path, file), Path.Join(patchFail2, file), overwrite: true);
                msg += $"{file}\n";
            }

            ScriptMessage(msg);
            tempDirPath = tempDir2Path;
        }
    }

    var patchedFiles = Directory.GetFiles(tempDirPath);
    if(patchedFiles.Length != scriptNames.Count) {
        throw new ScriptException($"patched files count mismatch (actual = {patchedFiles.Length}; expected = {scriptNames.Count})");
    }

    await ImportCodeDir(tempDirPath, updateStatus);
}

string RemoveNewFileDiffs(string patch, string startRemoving = "--- /dev/null", string stopRemoving = "--- ")
{
    var sb = new StringBuilder();
    int i = 0;
    int length = patch.Length;
    bool outputLine = true;

    while (i < length)
    {
        int start = i;
        while (i < length && patch[i++] != '\n') ;
        string line = patch.Substring(start, i - start);

        if (line.StartsWith(startRemoving))
            outputLine = false;
        else if (line.StartsWith(stopRemoving))
            outputLine = true;

        if (outputLine)
            sb.Append(line);
    }

    return sb.ToString();
}

string GenerateFileList(string diff)
{
    var sb = new StringBuilder();

    foreach (string line in diff.Split("\n"))
    {
        const string match = "--- original/";
        if (line.StartsWith(match))
        {
            string file = line.Substring(match.Length);
            sb.Append(file);
            sb.Append("\n");
        }
    }

    return sb.ToString();
}

async Task<string> GenerateCodePatch(UndertaleData patched, UndertaleData original, IList<string> scriptNames, bool updateStatus = false) {
    using(var tempDir = new TempDirectory(GetBuildDir())) {
        if(updateStatus) {
            SetProgressBar(null, "diff: "+tempDir.Path, 0, 0);
        }
        await ExportSpecificCodeToDir(original, scriptNames, Path.Join(tempDir.Path, "original"), updateStatus ? "Exporting original code": null);
        await ExportSpecificCodeToDir(patched, scriptNames, Path.Join(tempDir.Path, "patched"), updateStatus ? "Exporting patched code": null);
        return RemoveNewFileDiffs(await BusyBox("diff", tempDir.Path, "-a -b -B -d -N -w -r original patched".Split(' '), updateStatus, 1));
    }
}

struct GamePatch {
    public string Name { get; set; }
    public string ScriptFile { get; set; }
    public bool Public { get; set; }
    public string[] Deps { get; set; }
}

JsonSerializerOptions __g_gamepatch_jsonDeserializeOptions = new(JsonSerializerDefaults.Web);

IEnumerable<GamePatch> ScanGamePatches(string gamePatchesDir) {
    foreach(var gamePatchDir in Directory.EnumerateDirectories(gamePatchesDir)) {
        var gamePatchJsonPath = Path.Join(gamePatchDir, "GamePatch.json");
        if(!File.Exists(gamePatchJsonPath)) {
            continue;
        }

        using FileStream fileStream = File.OpenRead(gamePatchJsonPath);
        var gamePatch = JsonSerializer.Deserialize<GamePatch>(fileStream, __g_gamepatch_jsonDeserializeOptions);
    
        if(gamePatch.ScriptFile == "" || gamePatch.ScriptFile == null) {
            gamePatch.ScriptFile = "GamePatch.csx";
        }

        gamePatch.ScriptFile = PathResolve(gamePatchDir, gamePatch.ScriptFile);
        if(!File.Exists(gamePatchJsonPath)) {
            throw new Exception($"Could not find script file {gamePatch.ScriptFile}");
        }

        if(gamePatch.Name == "" || gamePatch.Name == null) {
            gamePatch.Name = Path.GetFileName(gamePatchDir);
        }

        if(gamePatch.Deps == null) {
            gamePatch.Deps = new string[]{};
        }

        yield return gamePatch;
    }
}
