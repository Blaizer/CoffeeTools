#load "../../patcher/lib/_UFO50.csx"
#load "_Patch.csx"

var info = GetVersionInfo();
var extensionName = info["c_ExtensionName"];
var extensionVersion = info["c_ExtensionVersion"];
var extDllName = extensionName + ".dll";

var convertRootDir = Path.Join(GetBuildDir(), $"{extensionName} v{extensionVersion} GMLoader");
var convertDir = Path.Join(convertRootDir, extensionName);
var scriptDir = Path.GetDirectoryName(GetCurrentScript());
var rootDir = Path.Join(scriptDir, "../..");

if (Directory.Exists(convertRootDir))
{
    Directory.Delete(convertRootDir, true);
}
Directory.CreateDirectory(convertRootDir);
Directory.CreateDirectory(convertDir);

File.Copy(Path.Join(rootDir, "README"), Path.Join(convertRootDir, "ReadMe.txt"), overwrite: true);

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
var outputDir = Path.Join(rootDir, "UFO 50");
Directory.CreateDirectory(dllDir);
CopyFile(outputDir, dllDir, extDllName);

var ufo50Version = GetUFO50Version(Data);
CopyFile(buildDir, scriptDir, ufo50Version + ".code.diff");

var mainDir = Path.Join(convertDir, extensionName);
Directory.CreateDirectory(mainDir);
CopyFile(rootDir, mainDir, "VersionInfo.txt");

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

var csxFile = Path.Join(csxSubDir, $"1_Patch{extensionName}.csx");
File.WriteAllText(csxFile, $@"#load ""../../{extensionName}/patches/{extensionName}/GamePatch.csx""");
