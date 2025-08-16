#load "../../patcher/lib/_Utils.csx"
#load "../../patcher/lib/_Patch.csx"
#load "../../patcher/lib/_Extension.csx"
#load "../../patcher/lib/_Graphics.csx"

using System.Threading.Tasks;

void PatchExtension()
{
    var extDllName = "CoffeeTools.dll";
    var extensionName = "CoffeeTools";
    var extensionVersion = "1.3.2";

    var extension = Data.Extensions.ByName(extensionName);
    if (extension == null)
    {
        extension = new UndertaleExtension();
        Data.Extensions.Add(extension);
    }

    extension.FolderName = Data.Strings.MakeString("");
    extension.Name = Data.Strings.MakeString(extensionName);
    extension.ClassName = Data.Strings.MakeString("");
    extension.Version = Data.Strings.MakeString(extensionVersion);
    extension.Files.Clear();
    extension.Options.Clear();

    uint lastExtFuncId = 0;
    foreach (var ext in Data.Extensions)
    {
        foreach (var file in ext.Files)
        {
            foreach (var func in file.Functions)
            {
                if (func.ID > lastExtFuncId)
                {
                    lastExtFuncId = func.ID;
                }
            }
        }
    }

    {
        var file = DefineExtensionFile(Data, extension, extDllName);
        file.Kind = UndertaleExtensionKind.Generic;
        file.Functions.Clear();

        var funcIdOffset = lastExtFuncId;

        void DefineExtensionFunction(string name)
        {
            file.Functions.DefineExtensionFunction(Data.Functions, Data.Strings, ++funcIdOffset, 11, name, UndertaleExtensionVarType.Double, name);
        }

        DefineExtensionFunction("ct_init");
        DefineExtensionFunction("ct_is_paused");
        DefineExtensionFunction("ct_set_paused");
        DefineExtensionFunction("ct_in_runloop");
        DefineExtensionFunction("ct_output_debug_string");
        DefineExtensionFunction("ct_game_load");
        DefineExtensionFunction("ct_is_ref");
        DefineExtensionFunction("ct_ref_to_int64");
        DefineExtensionFunction("ct_int64_to_ref");
        DefineExtensionFunction("ct_keyboard_check");
        DefineExtensionFunction("ct_keyboard_check_pressed");
        DefineExtensionFunction("ct_keyboard_check_released");

        file.InitScript = Data.Strings.MakeString("");
        file.CleanupScript = Data.Strings.MakeString("");
    }
}

async Task ImportCode()
{
    var scriptDir = Path.GetDirectoryName(GetCurrentScript());
    var codeDir = Path.Join(scriptDir, "code");
    await ImportCodeDir(codeDir, true);
}

void ImportSprites()
{
    var scriptDir = Path.GetDirectoryName(GetCurrentScript());
    ImportGraphics(scriptDir, true);
}
