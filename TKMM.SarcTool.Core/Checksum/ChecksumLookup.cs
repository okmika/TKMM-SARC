using TotkCommon.Components;

namespace TKMM.SarcTool.Core;

internal class ChecksumLookup {
    
    private readonly TotkChecksums checksums;
    
    public ChecksumLookup(string checksumBin) {
        this.checksums = TotkChecksums.FromFile(checksumBin);
    }

    public bool IsVanillaFile(ReadOnlySpan<char> canonical, Span<byte> data, int version, out bool isEntryFound)
    {
        return this.checksums.IsFileVanilla(canonical, data, version, out isEntryFound);
    }
}