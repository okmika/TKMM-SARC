namespace TKMM.SarcTool.Common;

public interface ISarcFileHandler {
    ReadOnlyMemory<byte> Merge(string fileName, IList<MergeFile> files);
    ReadOnlyMemory<byte> Package(string fileName, IList<MergeFile> files);

    void Initialize(string modPath, string outputPath) {
        
    }

    void Finish(string modPath, string outputPath) {
        
    } 
}

public class MergeFile {
    public int Priority { get; init; }
    public ReadOnlyMemory<byte> Contents { get; init; }

    public MergeFile(int priority, ReadOnlyMemory<byte> contents) {
        Priority = priority;
        Contents = contents;
    }
}
