/*
[GUI only]
Selects and applies patches on currently opened game data

By default, only patches set as public will be shown
hold SHIFT to see all of them
*/

#load "./lib/_Utils.csx"
#load "./lib/_MainWindow.csx"
#load "./lib/_Code.csx"
#load "./lib/_Patch.csx"
#load "./lib/_GameObject.csx"
#load "./lib/_UFO50.csx"

using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Input;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

EnsureDataLoaded();
StartProgressBarUpdater();

var ufo50Version = GetUFO50Version(Data);
var expectedVersion = Environment.GetEnvironmentVariable("UFO50_EXPECTED_VERSION");
if(expectedVersion != null && expectedVersion != ufo50Version.ToString()) {
    throw new ScriptException($"UFO50 version mismatch (actual = {ufo50Version}; expected = {expectedVersion})");
}

var gamePatches = new Dictionary<string, GamePatch>();
foreach(var gamePatch in ScanGamePatches(GetPatchesDir())) {
    if(!gamePatches.TryAdd(gamePatch.Name, gamePatch)) {
        throw new ScriptException($"Game patch already defined (Name = '{gamePatch.Name}')");
    }
}

string[] selectablePatchNames = gamePatches.Where(kv => Keyboard.IsKeyDown(Key.LeftShift) || kv.Value.Public)
    .Select(kv => kv.Key).ToArray();

string[] selectedPatchNames = null;

string envPatchStr = Environment.GetEnvironmentVariable("UFO50_PATCHES");
if(envPatchStr == "") {
    envPatchStr = null;
}

if(envPatchStr != null) {
    selectedPatchNames = envPatchStr == "*" ? selectablePatchNames : envPatchStr.Split(',');
} else {
    selectedPatchNames = ChoosePatchForm.Run(ufo50Version, selectablePatchNames);
}

if(selectedPatchNames == null || selectedPatchNames.Length == 0) {
    throw new ScriptException("Cancelled");
}

var patchNames = new HashSet<string>();
foreach(var patchName in selectedPatchNames) {
    if(gamePatches[patchName].Deps != null) {
        foreach(var dep in gamePatches[patchName].Deps) {
            if(!gamePatches.ContainsKey(dep)) {
                throw new ScriptException($"Cannot resolve game patch dep '{dep}' (key = '{patchName}')");
            }
            patchNames.Add(dep);
        }
    }
    patchNames.Add(patchName);
}

var objMods = DefineGameObject(Data, "obj_gm_mods");
objMods.Visible = true;
objMods.Persistent = true;
DefineRoomGameObject(Data, 0, objMods);

var importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);

importGroup.QueueAppend(objMods.EventHandlerFor(EventType.Create, Data), @"
    global.gm_mods_id = id;
    if(!variable_instance_exists(id, ""patches""))
        patches = ds_map_create();
    if(!variable_instance_exists(id, ""timesPatched""))
        timesPatched = 0;
    if(!variable_instance_exists(id, ""updateWindowTitle""))
        updateWindowTitle = true;
    timesPatched++;
");

importGroup.QueueAppend(objMods.EventHandlerFor(EventType.CleanUp, Data), @"
    if(variable_instance_exists(id, ""patches"") && patches != -1) {
        ds_map_destroy(patches);
        patches = -1;
    }
    updateWindowTitle = false;
");

importGroup.QueueAppend(objMods.EventHandlerFor(EventType.Draw, EventSubtypeDraw.DrawGUI, Data), @"
    if(updateWindowTitle) {
        updateWindowTitle = false;
        var newWindowTitle = ""%game% v%betaVersion% | %modlist%"";
        var modlist = ""MODS:"";
        for (var k = ds_map_find_first(patches); !is_undefined(k); k = ds_map_find_next(patches, k)) {
            if(ds_map_find_value(patches, k)) {
                modlist += "" "";
                modlist += string(k);
            }
        }
        if(variable_instance_exists(id, ""windowTitleFormat"")) {
            newWindowTitle = windowTitleFormat;
        }
        newWindowTitle = string_replace(newWindowTitle, ""%game%"", game_display_name);
        newWindowTitle = string_replace(newWindowTitle, ""%betaVersion%"", string(global.betaVersion));
        newWindowTitle = string_replace(newWindowTitle, ""%modlist%"", modlist);
        window_set_caption(newWindowTitle);
    }
");

int i = 0;
foreach(var patchName in patchNames) {
    SetProgressBar($"Applying patch {patchName} [{i}/{patchNames.Count}]", "Executing script", 0, 1);
    var gamePatch = gamePatches[patchName];

    await MainWindowRunScript(gamePatch.ScriptFile);
    if(!ScriptExecutionSuccess) {
        break;
    }

    importGroup.QueueAppend(objMods.EventHandlerFor(EventType.Create, Data), @$"
        ds_map_set(patches, {JsonSerializer.Serialize(patchName)}, {gamePatch.Public.ToString().ToLower()})
    ");
}

SetProgressBar($"Applying global patch", "Executing script", 0, 1);
importGroup.Import();

if (envPatchStr == null && ScriptExecutionSuccess) {
    ScriptMessage("Applied patches: "+String.Join(", ", patchNames.ToArray()));
}


await StopProgressBarUpdater();
HideProgressBar();

// -------------------------------------

public class ChoosePatchForm : Form {
    const int MARGIN = 16;
    private Version gameVersion;
    private string[] patchNames;
    private CheckBox[] checkboxes;
    private Button button;
    private int maxControlWidth;
    private int y;
    private bool ok;

    private ChoosePatchForm(Version gameVersion, string[] patchNames) {
        this.gameVersion = gameVersion;
        this.patchNames = patchNames;
        this.maxControlWidth = 0;
        this.y = 0;
        this.ok = false;
        this.InitializeComponents();
    }

    private void AddControl(Control ctrl) {
        ctrl.Location = new System.Drawing.Point(MARGIN, this.y);
        this.Controls.Add(ctrl);
        this.y += ctrl.Size.Height;
        this.maxControlWidth = Math.Max(this.maxControlWidth, ctrl.Size.Width);
    }

    private void AddLabel(string text) {
        var versionLabel = new Label();
        versionLabel.Text = text;
        versionLabel.AutoSize = true;
        this.AddControl(versionLabel);
    }

    private void UpdateButtonStatus(object sender, EventArgs e) {
        foreach(var checkbox in this.checkboxes) {
            if(checkbox.Checked) {
                this.button.Enabled = true;
                return;
            }
        }
        this.button.Enabled = false;
    }

    private void InitializeComponents() {
        this.MdiParent = (Form)(Control.FromHandle(Process.GetCurrentProcess().MainWindowHandle));
        this.Text = "Patcher";
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        this.checkboxes = new CheckBox[this.patchNames.Length];
        this.y += MARGIN;

        this.AddLabel($"Game ver: {this.gameVersion}");
        this.y += MARGIN;

        for(int i = 0; i < this.patchNames.Length; i++) {
            var checkbox = this.checkboxes[i] = new CheckBox();
            checkbox.Text = this.patchNames[i];
            checkbox.AutoSize = true;
            checkbox.CheckedChanged += UpdateButtonStatus;
            this.AddControl(checkbox);
        }
        
        this.y += MARGIN;

        this.button = new Button();
        this.button.Text = "Patch";
        this.button.Enabled = false;
        this.button.Size = new System.Drawing.Size(Math.Max(this.maxControlWidth, this.button.Size.Width), this.button.Size.Height);
        this.button.Click += (object sender, EventArgs e) => {
            this.ok = true;
            this.Close();
        };
        this.AddControl(this.button);
        this.ClientSize = new System.Drawing.Size(2*MARGIN+this.maxControlWidth, this.y+MARGIN);
    }

    static public string[] Run(Version ufo50Version, string[] patchNames) {
        var form = new ChoosePatchForm(ufo50Version, patchNames);
        form.ShowDialog();

        if(!form.ok) {
            return null;
        }
    
        return Enumerable.Range(0, patchNames.Length)
            .Where(x => form.checkboxes[x].Checked)
            .Select(x => patchNames[x])
            .ToArray();
    }
}
