// OH NO
using System.Reflection;
using System.Threading.Tasks;

var __g_mainWindow = System.Windows.Application.Current.MainWindow as UndertaleModTool.MainWindow;

object _MainWindowInvoke(string methodName, params object[] args) {
    var method = typeof(UndertaleModTool.MainWindow).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
    return method.Invoke(__g_mainWindow, args);
}
async Task MainWindowLoadFile(string filename, bool preventClose = false, bool onlyGeneralInfo = false) {
    await (Task)_MainWindowInvoke("LoadFile", filename, preventClose, onlyGeneralInfo);
}

async Task MainWindowSaveFile(string filename, bool suppressDebug = false) {
    await (Task)_MainWindowInvoke("SaveFile", filename, suppressDebug);
}

async Task MainWindowRunScript(string filename) {
    await __g_mainWindow.RunScript(filename);
}
