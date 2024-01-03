using System.Collections.Immutable;

namespace TKMM.SarcTool.Common;

public interface IHandlerManager {
    public ISarcFileHandler? GetHandlerInstance(string extension);
    public ImmutableHashSet<string> GetSupportedExtensions();
}