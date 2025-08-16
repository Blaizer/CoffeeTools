using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

string ExportAsm(UndertaleData utdata, UndertaleCode code) {
    return code.Disassemble(utdata.Variables, utdata.CodeLocals?.For(code));
}

// var __g_ExportCodeSettings = new DecompilerSettings() {
//     CleanupLocalVarDeclarations = false,
// };
var __g_ExportCodeContext = new System.Runtime.CompilerServices.ConditionalWeakTable<UndertaleData, GlobalDecompileContext>();
GlobalDecompileContext _ExportCodeContext(UndertaleData utdata) {
    lock(utdata) {
        GlobalDecompileContext context;
        if (!__g_ExportCodeContext.TryGetValue(utdata, out context)) {
            context = new GlobalDecompileContext(utdata);
            __g_ExportCodeContext.Add(utdata, context);
        }

        if (utdata.GlobalFunctions is null)
        {
            GlobalDecompileContext.BuildGlobalFunctionCache(utdata);
        }   

        return context;
    }
}

string ExportCode(UndertaleData utdata, UndertaleCode code) {
    return new Underanalyzer.Decompiler.DecompileContext(_ExportCodeContext(utdata), code).DecompileToString();
}

string ExportCodeToFile(UndertaleData utdata, UndertaleCode code, string dir) {
    if(code == null) {
        return null;
    }

    var scriptFile = Path.Join(dir, code.Name.Content+".gml");
    var gml = ExportCode(utdata, code);
    File.WriteAllText(scriptFile, gml);
    return scriptFile;
}

string ExportCodeToFile(UndertaleData utdata, string name, string dir) {
    return ExportCodeToFile(utdata, utdata.Code.ByName(name), dir);
}

async Task ExportSpecificCodeToDir(UndertaleData utdata, IList<string> names, string dir, string status = null) {
    if (status != null) {
        SetProgressBar(null, status, 0, names.Count);
    }
    Directory.CreateDirectory(dir);

    await Task.Run(() => Parallel.ForEach(names, new ParallelOptions{MaxDegreeOfParallelism = 8}, name => {
        ExportCodeToFile(utdata, name, dir);
        if(status != null) {
            IncrementProgressParallel();
        }
    }));
}

async Task<List<string>> CompareCode(UndertaleData patched, UndertaleData original, bool updateStatus = false) {
    var changed = new List<string>();
    var patchedCodes = patched.Code.Where(c => c.ParentEntry is null).ToList();
    if(updateStatus) {
        SetProgressBar("Comparing code", "Comparing code", 0, patchedCodes.Count);
    }

    foreach(var patchedCode in patchedCodes) {
        if(updateStatus) {
            IncrementProgressParallel();
        }

        var scriptName = patchedCode.Name.Content;
        var originalCode = original.Code.ByName(scriptName);
        if(originalCode == null) {
            changed.Add(scriptName);
            continue;
        }
    
        if(ExportAsm(patched, patchedCode) != ExportAsm(original, originalCode)) {
            changed.Add(scriptName);
            continue;
        }
    }

    return changed;
}

void BeginImportCode() {
    SyncBinding("Strings, Code, CodeLocals, Scripts, GlobalInitScripts, GameObjects, Functions, Variables", true);
}

void EndImportCode() {
    DisableAllSyncBindings();
}

async Task ImportCodeFiles(string[] scriptFiles, bool updateStatus = false) {
    if(updateStatus) {
        SetProgressBar(null, "Importing code", 0, scriptFiles.Length);
    }

    BeginImportCode();
    var importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);
    // await Task.Run(() => {
        foreach (string scriptFile in scriptFiles) {
            if(!Path.GetExtension(scriptFile).Equals(".gml")) {
                throw new ScriptException($"Not a GML file: ${scriptFile}");
            }

            importGroup.QueueReplace(Path.GetFileNameWithoutExtension(scriptFile), File.ReadAllText(scriptFile));

            if(updateStatus) {
                IncrementProgressParallel();
            }
        }

        if(updateStatus) {
            SetProgressBar(null, "Applying imported code", 0, 1);
        }

        importGroup.Import();

        if(updateStatus) {
            IncrementProgressParallel();
        }
    // });

    EndImportCode();
}

async Task ImportCodeDir(string dir, bool updateStatus = false) {
    string[] scriptFiles = Directory.GetFiles(dir, "*.gml");

    await ImportCodeFiles(scriptFiles, updateStatus);
}
