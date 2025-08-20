using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
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
        WorkingDirectory = workdir
    };

    startInfo.ArgumentList.Add(applet);
    Array.ForEach(args, arg => startInfo.ArgumentList.Add(arg));

    if(updateStatus) {
        SetProgressBar(null, $"$ busybox.exe {applet} {String.Join(' ', args)}", 0, 1);
    }

    using (Process p = Process.Start(startInfo)) {
        string lastFile = null;
        var errors = new List<(string file, string line)>();

        p.OutputDataReceived += (s, e) => {
            if (e.Data != null && e.Data.StartsWith("patching file ")) {
                lastFile = e.Data.Substring("patching file ".Length).Trim();
            }
        };

        p.ErrorDataReceived += (s, e) => {
            if (e.Data != null && e.Data.StartsWith("Hunk ")) {
                errors.Add((lastFile ?? "<unknown file>", e.Data));
            }
        };

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.Close();

        await Task.Run(() => p.WaitForExit());

        if (p.ExitCode != expectedExitCode) {
            var msg = $"busybox '{applet}' exited with code {p.ExitCode}\n\nConflicts:\n";
            foreach (var err in errors) {
                msg += $"  {err.file}: {err.line}\n";
            }

            throw new ScriptException(msg);
        }

        if(updateStatus) {
            IncrementProgressParallel();
        }
    }

    return null;
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
