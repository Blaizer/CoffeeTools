#load "_Utils.csx"

using System;

Version GetUFO50Version(UndertaleData utdata) {
    var constants = GetConstants(utdata, new string[] {"@@VersionMajor", "@@VersionMinor", "@@VersionRevision", "@@VersionBuild"});
    return Version.Parse(String.Join('.', constants));
}

string GetCurrentUFO50Dir() {
    EnsureDataLoaded();
    var dir = Environment.GetEnvironmentVariable("UFO50_DIR");

    if(dir == null || dir == "") {
        dir = Path.GetDirectoryName(FilePath);
    }

    return dir;
}
