#load "lib/_UFO50.csx"
#load "lib/_Patch.csx"
#load "lib/_Utils.csx"

using System.Diagnostics;
using System.Linq;

{
var gamePatch = ScanGamePatches(GetPatchesDir()).ToList()[0];
var modName = Path.GetFileName(Directory.GetDirectories(GetPatchesDir())[0]);
var modPrettyName = gamePatch.Name;
var modVersion = GetModVersion();
if (modVersion.EndsWith(".0"))
{
    modVersion = modVersion.Substring(0, modVersion.Length - 2);
}
var gameVersion = GetGameVersion();
if (gameVersion.EndsWith(".0"))
{
    gameVersion = gameVersion.Substring(0, gameVersion.Length - 2);
}

var rootDir = GetRootDir();
var dllOutputDir = Path.Join(rootDir, "UFO 50");
var extDllName = Path.GetFileName(Directory.GetFiles(dllOutputDir, "*.dll")[0]);
var dllVersionInfo = FileVersionInfo.GetVersionInfo(extDllName);
var isDebug = dllVersionInfo.IsDebug;

var convertDirName = $"{modPrettyName} v{modVersion}" + (isDebug ? "D" : "");
convertDirName = convertDirName.Replace(" ", "_").Replace(".", "-");
var convertRootDir = Path.Join(GetBuildDir(), convertDirName);
var convertDir = Path.Join(convertRootDir, modPrettyName);

if (Directory.Exists(convertRootDir))
{
    File.SetAttributes(convertRootDir, FileAttributes.Normal);
    Directory.Delete(convertRootDir, true);
}
Directory.CreateDirectory(convertRootDir);
Directory.CreateDirectory(convertDir);

var readme = Path.Join(rootDir, "README.md");
if (!File.Exists(readme))
{
    readme = Path.Join(rootDir, "README");
}

string ReplaceVariables(string text)
{
    return text
        .Replace("$ModVersion", modVersion)
        .Replace("$GameVersion", gameVersion)
        .Replace("$DebugPostfix", isDebug ? "-DEBUG" : "");
}

var readmeText = File.ReadAllText(readme);
var replacedReadmeText = readmeText;
foreach (var pair in gamePatch.ReadmeReplacements)
{
    replacedReadmeText = Regex.Replace(replacedReadmeText, pair.Pattern, pair.Replacement);
}
replacedReadmeText = ReplaceVariables(replacedReadmeText);
if (readmeText != replacedReadmeText)
{
    File.WriteAllText(readme, replacedReadmeText);
}
File.Copy(readme, Path.Join(convertRootDir, "README.txt"), overwrite: true);

void CopyFile(string srcDir, string dstDir, string file)
{
    File.Copy(Path.Join(srcDir, file), Path.Join(dstDir, file), overwrite: true);
}

CopyFile(rootDir, convertDir, "icon.png");

var infoText = $"{gamePatch.Author}\n{ReplaceVariables(gamePatch.Description)}";
File.WriteAllText(Path.Join(convertDir, "info.txt"), infoText);

var codeDir = Path.Join(convertDir, "code");
var buildDir = GetBuildDir();
Directory.CreateDirectory(codeDir);
CopyFile(buildDir, codeDir, "files.txt");

if (gamePatch.ConflictingMods.Length > 0)
{
    var conflictingModsFile = Path.Join(codeDir, "conflicting_mods.txt");
    File.WriteAllText(conflictingModsFile, string.Join("\n", gamePatch.ConflictingMods));
}

var dllDir = Path.Join(convertDir, "dll");
Directory.CreateDirectory(dllDir);
CopyFile(dllOutputDir, dllDir, extDllName);

var ufo50Version = GetUFO50Version(Data);
var modPatchesDir = Path.Join(GetPatchesDir(), modName);
var diffFile = ufo50Version + ".code.diff";
if (File.Exists(Path.Join(buildDir, diffFile)))
{
    CopyFile(buildDir, modPatchesDir, ufo50Version + ".code.diff");
}

var mainDir = Path.Join(convertDir, modName);
Directory.CreateDirectory(mainDir);
CopyFile(rootDir, mainDir, "version.h");

void CopyDir(string srcDir, string dstDir, string name, string[] excludeDirs = null, bool deleteExisting = false)
{
    srcDir = Path.Join(srcDir, name);
    dstDir = Path.Join(dstDir, name);
    var dir = new DirectoryInfo(srcDir);

    if (!dir.Exists)
    {
        throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
    }

    if (deleteExisting && Directory.Exists(dstDir))
    {
        File.SetAttributes(dstDir, FileAttributes.Normal);
        Directory.Delete(dstDir, true);
    }

    Directory.CreateDirectory(dstDir);

    foreach (FileInfo file in dir.GetFiles())
    {
        string targetFilePath = Path.Join(dstDir, file.Name);
        file.CopyTo(targetFilePath, true);
    }

    foreach (DirectoryInfo subDir in dir.GetDirectories())
    {
        if (excludeDirs == null || !Array.Exists(excludeDirs, x => x.Equals(subDir.Name, StringComparison.OrdinalIgnoreCase)))
        {
            string newDestDir = Path.Join(dstDir, subDir.Name);
            CopyDir(srcDir, dstDir, subDir.Name);
        }
    }
}

CopyDir(rootDir, mainDir, "patches");
CopyDir(rootDir, mainDir, "patcher", new[] { "build", "versions" });

var csxDir = Path.Join(convertDir, "csx");
var csxSubDir = Path.Join(csxDir, "post");
Directory.CreateDirectory(csxDir);
Directory.CreateDirectory(csxSubDir);

var csxFile = Path.Join(csxSubDir, $"1_Patch{modName}.csx");
var csxSettingsFile = Path.Join(csxSubDir, $"1_Patch{modName}.csx-settings");
File.WriteAllText(csxFile, $@"#load ""1_Patch{modName}.csx-settings""" + "\n" + $@"#load ""../../{modName}/patches/{modName}/GamePatch.csx""");
File.WriteAllText(csxSettingsFile, $@"Environment.SetEnvironmentVariable(""DEBUG"", ""{(isDebug ? "1" : "")}"");");

foreach (var d in new[] { "pre", "post", "after" })
{
    var srcCsxDir = Path.Join(modPatchesDir, d);
    if (Directory.Exists(srcCsxDir))
    {
        foreach (var srcCsxFile in Directory.GetFiles(srcCsxDir, "*.csx"))
        {
            var f = Path.GetFileNameWithoutExtension(srcCsxFile);
            var dstCsxSubDir = Path.Join(csxDir, d);
            var dstCsxFile = Path.Join(dstCsxSubDir, $"{f}{modName}.csx");

            Directory.CreateDirectory(dstCsxSubDir);
            File.Copy(srcCsxFile, dstCsxFile, true);
        }
    }
}

var libDir = Path.Join(mainDir, "patcher", "lib");
File.Delete(Path.Join(libDir, "busybox.exe"));

var modLoaderInstallDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UFO-50-Mod-Loader", "my mods");
if (Directory.Exists(modLoaderInstallDir))
{
    CopyDir(convertRootDir, modLoaderInstallDir, modPrettyName, deleteExisting: true);
}
}
