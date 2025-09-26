using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using UndertaleModLib.Util;

string GetCurrentScript([System.Runtime.CompilerServices.CallerFilePath] string fileName = null) {
    return fileName;
}

string PathResolve(params string[] components) {
    return Path.GetFullPath(Path.Combine(components));
}

string GetToolsDir() {
    return Path.GetDirectoryName(GetCurrentScript());
}

string GetPatchesDir() {
    return PathResolve(GetToolsDir(), "../../patches");
}

string GetBuildDir() {
    return PathResolve(GetToolsDir(), "../build");
}

string GetVersionsDir() {
    return PathResolve(GetToolsDir(), "../versions");
}

string GetVersionDir(string version) {
    return PathResolve(GetVersionsDir(), version);
}

string GetVersionDir(Version version) {
    return GetVersionDir(version.ToString());
}

string GetRootDir()
{
    return PathResolve(GetToolsDir(), "../..");
}

string GetMergeDir()
{
    return PathResolve(GetBuildDir(), "merge");
}

class TempDirectory : IDisposable {
    private DirectoryInfo di;
    public string Path => di.FullName;

    public TempDirectory(string parentDir, string prefix = "~tmp.") {
        di = new DirectoryInfo(System.IO.Path.Join(parentDir, prefix+System.IO.Path.GetRandomFileName()));
        if(di.Exists) {
            throw new ScriptException("failed to create tmp dir");
        }
        di.Create();
    }

    public async void Dispose() {
        if(di != null) {
            var diToDelete = di;
            Task.Run(async () => {
                try {
                    diToDelete.Delete(true);
                } catch(Exception e) {}
            });
            di = null;
        }
    }
}

async Task<UndertaleData> LoadExternalData(string path, bool updateStatus = false) {
    UndertaleData result = null;

    if(updateStatus) {
        SetProgressBar(null, $"Loading {path}", 0, 1);
    }

    await Task.Run(() => {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) {
            result = UndertaleIO.Read(stream, (string warning, bool _) => ScriptMessage("A warning occured while trying to load " + path + ":\n" + warning));
        }

        if(updateStatus) {
            IncrementProgressParallel();
        }
    });

    return result;
}

// BusyBox builds and source available here: https://frippery.org/busybox/index.html
async Task<string> BusyBox(string applet, string workdir, string[] args, bool updateStatus = false, int expectedExitCode = 0) {
    var startInfo = new ProcessStartInfo { 
        FileName = PathResolve(GetToolsDir(), "busybox.exe"),
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        WorkingDirectory = workdir,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    startInfo.ArgumentList.Add(applet);
    Array.ForEach(args, arg => startInfo.ArgumentList.Add(arg));

    if(updateStatus) {
        SetProgressBar(null, $"$ busybox.exe {applet} {String.Join(' ', args)}", 0, 1);
    }

    using(Process p = Process.Start(startInfo)) {
        string res = null;
        string err = null;

        var tasks = new List<Task>();
        tasks.Add(Task.Run(() => res = p.StandardOutput.ReadToEnd()));
        tasks.Add(Task.Run(() => err = p.StandardError.ReadToEnd()));
        tasks.Add(Task.Run(() => p.WaitForExit()));
        p.StandardInput.Close();
        await Task.WhenAll(tasks);

        if(p.ExitCode != expectedExitCode) {
            throw new ScriptException($"busybox '{applet}' exited with code {p.ExitCode}:\n\nargs:{String.Join(' ', args)}\n\n{err}");
        }

        if(updateStatus) {
            IncrementProgressParallel();
        }

        return res;
    }
}

string[] GetConstants(UndertaleData utdata, string[] constantNames) {
    var remaining = constantNames.Length;
    var constants = new string[remaining];

    foreach (UndertaleOptions.Constant constant in utdata.Options.Constants) {
        if (constant.Name == null || constant.Value == null) {
            continue;
        }
        int pos = Array.IndexOf(constantNames, constant.Name.Content);
        if (pos <= -1) {
            continue;
        }
        constants[pos] = constant.Value.Content;
        if(--remaining <= 0) {
            return constants;
        }
    }

    throw new ScriptException("Could not find constants");
}

Dictionary<string, string> GetVersionDefines()
{
    var versionFile = PathResolve(GetRootDir(), "version.h");
    var defines = new Dictionary<string, string>();
    foreach (var line in File.ReadLines(versionFile))
    {
        var parts = line.Trim().Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[0] == "#define")
        {
            defines[parts[1]] = parts[2].Trim('"');
        }
    }
    return defines;
}

string GetModVersion()
{
    var defines = GetVersionDefines();
    return $"{defines["MOD_VERSION_MAJOR"]}.{defines["MOD_VERSION_MINOR"]}.{defines["MOD_VERSION_BUILD"]}";
}

string GetGameVersion()
{
    var defines = GetVersionDefines();
    return $"{defines["GAME_VERSION_MAJOR"]}.{defines["GAME_VERSION_MINOR"]}.{defines["GAME_VERSION_BUILD"]}.{defines["GAME_VERSION_REVSN"]}";
}
