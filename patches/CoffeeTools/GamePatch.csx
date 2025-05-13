#load "../../patcher/lib/_Utils.csx"
#load "../../patcher/lib/_Patch.csx"
#load "../../patcher/lib/_UFO50.csx"
#load "_Patch.csx"

using System.Threading.Tasks;

var ufo50Version = GetUFO50Version(Data);
var scriptDir = Path.GetDirectoryName(GetCurrentScript());

PatchExtension();

ImportSprites();

ImportCode();

await ApplyCompatibleCodePatch(ufo50Version, scriptDir, new[] {
    new PatchVersionRange("1.7.6.0", "1.7.6.0")
}, true);
