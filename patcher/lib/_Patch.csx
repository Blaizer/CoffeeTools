#load "_Utils.csx"
#load "_Code.csx"

using System;
using System.Text.Json;
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
    await ExportSpecificCodeToDir(Data, scriptNames, tempDir.Path, updateStatus ? "Exporting code to be patched" : null);

    try {
        await BusyBox("patch", tempDir.Path, new[] {"-R", "--dry-run", "-i", patchPath}, updateStatus);
    } catch {
        await BusyBox("patch", tempDir.Path, new[] {"-i", patchPath}, updateStatus);
    }

    var patchedFiles = Directory.GetFiles(tempDir.Path);
    if(patchedFiles.Length != scriptNames.Count) {
        throw new ScriptException($"patched files count mismatch (actual = {patchedFiles.Length}; expected = {scriptNames.Count})");
    }

    await ImportCodeDir(tempDir.Path, updateStatus);
}

async Task<string> GenerateCodePatch(UndertaleData patched, UndertaleData original, IList<string> scriptNames, bool updateStatus = false) {
    using(var tempDir = new TempDirectory(GetBuildDir())) {
        if(updateStatus) {
            SetProgressBar(null, "diff: "+tempDir.Path, 0, 0);
        }
        await ExportSpecificCodeToDir(original, scriptNames, Path.Join(tempDir.Path, "original"), updateStatus ? "Exporting original code": null);
        await ExportSpecificCodeToDir(patched, scriptNames, Path.Join(tempDir.Path, "patched"), updateStatus ? "Exporting patched code": null);
        return await BusyBox("diff", tempDir.Path, "-a -b -B -d -N -w -r original patched".Split(' '), updateStatus, 1);
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
