using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

async Task ImportCodeFiles(string[] scriptFiles, string[] globalFiles, bool updateStatus = false) {
    if(updateStatus) {
        SetProgressBar(null, "Importing code", 0, scriptFiles.Length);
    }

    string globals = "";
    foreach (string globalFile in globalFiles) {
        globals += "\n" + File.ReadAllText(globalFile);
    }

    globals = SubstituteScopedMacros(globals);

    BeginImportCode();
    var importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);
    // await Task.Run(() => {
        foreach (string scriptFile in scriptFiles) {
            if(!Path.GetExtension(scriptFile).Equals(".gml")) {
                throw new ScriptException($"Not a GML file: ${scriptFile}");
            }

            importGroup.QueueReplace(Path.GetFileNameWithoutExtension(scriptFile), SubstituteScopedMacros(File.ReadAllText(scriptFile)) + globals);

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

async Task ImportCodeDir(string dir, bool updateStatus = false, string globalDir = null) {
    string[] scriptFiles = Directory.GetFiles(dir, "*.gml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
    string[] globalFiles = globalDir != null ? Directory.GetFiles(globalDir, "*.gml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();

    await ImportCodeFiles(scriptFiles, globalFiles, updateStatus);
}

struct MacroDefinition
{
    public string Name;
    public string Body;
    public int BodyIndex;
}

string SubstituteScopedMacros(string source)
{
    var macros = new List<MacroDefinition>();
    var replacements = new Dictionary<string, string>();

    int i = 0;
    int length = source.Length;

    while (i < length)
    {
        int start = i;
        while (i < length && source[i++] != '\n') ;
        int end = i;

        string line = source.Substring(start, end - start);

        int p = 0;
        while (p < line.Length && char.IsWhiteSpace(line[p])) p++;

        if (!line.Substring(p).StartsWith("#macro")) continue;
        p += 6;

        int spaceStart = p;
        while (p < line.Length && char.IsWhiteSpace(line[p])) p++;
        if (p == spaceStart) continue;

        int idStart = p;
        while (p < line.Length && !char.IsWhiteSpace(line[p])) p++;
        if (p == idStart) continue;

        string fullId = line.Substring(idStart, p - idStart);

        while (p < line.Length && char.IsWhiteSpace(line[p])) p++;

        int bodyIndex = start + p;
        string body = line.Substring(p).TrimEnd('\r', '\n');

        var parts = fullId.Split(':', 2);
        if (parts.Length == 2)
        {
            string scope = parts[0];
            string name = parts[1];

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(scope)))
            {
                replacements.Add(name, body);
            }
        }
        else
        {
            macros.Add(new MacroDefinition
            {
                Name = fullId,
                Body = body,
                BodyIndex = bodyIndex
            });
        }
    }

    var sb = new StringBuilder(source);

    for (int m = macros.Count - 1; m >= 0; m--)
    {
        var macro = macros[m];

        if (!replacements.TryGetValue(macro.Name, out string replacement))
            continue;

        int start = macro.BodyIndex;
        int len = macro.Body.Length;

        sb.Remove(start, len);
        sb.Insert(start, replacement);
    }

    return sb.ToString();
}
