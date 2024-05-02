using TotkCommon.Components;

namespace TKMM.SarcTool.Core;

internal class ChecksumLookup {
    
    private readonly TotkChecksums checksums;
    
    public ChecksumLookup(string checksumBin) {
        this.checksums = TotkChecksums.FromFile(checksumBin);
    }

    // public ulong? GetChecksum(ulong hash) {
    //     if (!checksums.TryGetValue(hash, out var checksum))
    //         return null;
    // 
    //     return checksum;
    // }

    public bool IsVanillaFile(ReadOnlySpan<char> canonical, Span<byte> data, int version, out bool isEntryFound)
    {
        return this.checksums.IsFileVanilla(canonical, data, version, out isEntryFound);
    }
}