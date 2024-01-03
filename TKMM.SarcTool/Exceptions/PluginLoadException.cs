namespace TKMM.SarcTool;

public class PluginLoadException : Exception {
    public PluginLoadException(string message, Exception? baseException = null) : base(message, baseException) {
        
    }
}