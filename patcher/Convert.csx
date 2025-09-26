#load "lib/_UFO50.csx"
#load "lib/_Utils.csx"

{
var modName = Path.GetFileName(Directory.GetDirectories(GetPatchesDir())[0]);
var modPrettyName = modName.Replace("-", " ");
var modVersion = GetModVersion();

var rootDir = GetRootDir();
var convertRootDir = Path.Join(GetBuildDir(), $"{modPrettyName} v{modVersion} GMLoader");
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
File.Copy(readme, Path.Join(convertRootDir, "ReadMe.txt"), overwrite: true);

void CopyFile(string srcDir, string dstDir, string file)
{
    File.Copy(Path.Join(srcDir, file), Path.Join(dstDir, file), overwrite: true);
}

CopyFile(rootDir, convertDir, "icon.png");
CopyFile(rootDir, convertDir, "info.txt");

var codeDir = Path.Join(convertDir, "code");
var buildDir = GetBuildDir();
Directory.CreateDirectory(codeDir);
CopyFile(buildDir, codeDir, "files.txt");

var dllDir = Path.Join(convertDir, "dll");
var dllOutputDir = Path.Join(rootDir, "UFO 50");
var extDllName = Path.GetFileName(Directory.GetFiles(dllOutputDir, "*.dll")[0]);
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

void CopyDir(string srcDir, string dstDir, string name, string[] excludeDirs = null)
{
    srcDir = Path.Join(srcDir, name);
    dstDir = Path.Join(dstDir, name);
    var dir = new DirectoryInfo(srcDir);

    if (!dir.Exists)
        throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

    Directory.CreateDirectory(dstDir);

    foreach (FileInfo file in dir.GetFiles())
    {
        string targetFilePath = Path.Combine(dstDir, file.Name);
        file.CopyTo(targetFilePath, true);
    }

    foreach (DirectoryInfo subDir in dir.GetDirectories())
    {
        if (excludeDirs == null || !Array.Exists(excludeDirs, x => x.Equals(subDir.Name, StringComparison.OrdinalIgnoreCase)))
        {
            string newDestDir = Path.Combine(dstDir, subDir.Name);
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
File.WriteAllText(csxFile, $@"#load ""../../{modName}/patches/{modName}/GamePatch.csx""");

var libDir = Path.Join(mainDir, "patcher", "lib");
File.Delete(Path.Join(libDir, "busybox.exe"));
}
