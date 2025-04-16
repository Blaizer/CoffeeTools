UndertaleExtensionFile DefineExtensionFile(UndertaleData utdata, UndertaleExtension extension, string filename, bool throwIfExists = false) {
    UndertaleExtensionFile result = null;

    foreach(var file in extension.Files) {
        if(file.Filename.Content == filename) {
            result = file;
            break;
        }
    }

    if(result == null) {
        result = new UndertaleExtensionFile() {
            Filename = utdata.Strings.MakeString(filename)
        };
        extension.Files.Add(result);
    } else if(throwIfExists) {
        throw new ScriptException($"Extension file with name '{filename}' already exists");
    }

    return result;
}

