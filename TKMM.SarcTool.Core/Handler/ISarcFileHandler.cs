namespace TKMM.SarcTool.Core;

internal interface ISarcFileHandler {
    ReadOnlyMemory<byte> Merge(string fileName, IList<MergeFile> files);
    ReadOnlyMemory<byte> Package(string fileName, IList<MergeFile> files, out bool isEmptyChangelog);
    
    string[] Extensions { get; }
}

internal class MergeFile {
    public int Priority { get; init; }
    public ReadOnlyMemory<byte> Contents { get; init; }

    public MergeFile(int priority, ReadOnlyMemory<byte> contents) {
        Priority = priority;
        Contents = contents;
    }
}
