/*
Exports all code to `UFO50_Code` subfolder next to the currently opened file using the following path rule:

./UFO50_Code/%gameId%-%gameName%/%scriptName%.gml
*/

#load "./lib/_Code.csx"
#load "./lib/_UFO50.csx"

using System.IO;
using System.Threading.Tasks;
using System.Linq;

EnsureDataLoaded();

var codeFolderName = "UFO50_Code";
var gameNamesByIds = "Global|MORTOL_II|ATTACTICS|CAMPANELLA|CAMOUFLAGE|DEVILITION|GOLFARIA|VAINGER|CAMPANELLA_3|FIST_HELL|PORGY|MOONCAT|GRIMSTONE|RAIL_HEIST|QUIBBLE_RACE|VELGRESS|WARPTANK|STAR_WASPIR|BLOCK_KOALA|DIVERS|BUG_HUNTER|WALDORF_S_JOURNEY|BUSHIDO_BALL|PINGOLF|RAKSHASA|COMBATANTS|KICK_CLUB|MAGIC_GARDEN|THE_BIG_BELL_RACE|MORTOL|OVERBOLD|ELFAZAR_S_HAT|ONION_DELIVERY|LORDS_OF_DISKONIA|NINPEK|VALBRACE|PARTY_HOUSE|PILOT_QUEST|CAMPANELLA_2|NIGHT_MANOR|BARBUTA|MINI_MAX|HYPER_CONTENDER|HOT_FOOT|ROCK_ON_ISLAND|CYBER_OWLS|CARAMEL_CARAMEL|SEASIDE_DRIVE|PLANET_ZOLDATH|PAINT_CHASE|AVIANOS".Split("|");
var codeDir = Path.Join(GetCurrentUFO50Dir(), codeFolderName);
if (Directory.Exists(codeDir)) {
    throw new ScriptException($"Folder '{codeFolderName}' already exists, remove before running this script");
}

var groupByGame = ScriptQuestion("Enable grouping of scripts by game ?");

Directory.CreateDirectory(codeDir);

SetProgressBar(null, "Export code", 0, Data.Code.Count);
StartProgressBarUpdater();

Regex gameRelatedScriptNameRegex = new Regex(@"^gml_[\w]+_[a-z]+(\d\d)_[\w_]+$");
await Task.Run(() => Parallel.ForEach(Data.Code, new ParallelOptions{MaxDegreeOfParallelism = 1}, async code => {
    IncrementProgressParallel();
    if(code.ParentEntry is not null) {
        return;
    }

    var gameDir = codeDir;

    if (groupByGame) {
        var gameIndex = 0;
        Match m = gameRelatedScriptNameRegex.Match(code.Name.Content);
        if(m.Success) {
            gameIndex = Int32.Parse(m.Groups[1].Value);
        }

        if(gameIndex >= gameNamesByIds.Length) {
            gameIndex = 0;
        }

        gameDir = Path.Join(codeDir,  $"{gameIndex:00}-{gameNamesByIds[gameIndex]}");
    }

    try {
        Directory.CreateDirectory(gameDir);
    } catch {}

    ExportCodeToFile(Data, code, gameDir);
}));

File.WriteAllText(Path.Combine(codeDir, "modified.txt"), DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

await StopProgressBarUpdater();
HideProgressBar();
ScriptMessage("Export Complete.\n\nLocation: " + codeDir);
