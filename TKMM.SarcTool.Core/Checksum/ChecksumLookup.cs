using System.Runtime.InteropServices;

namespace TKMM.SarcTool.Core;

internal class ChecksumLookup {
    
    private readonly Dictionary<ulong, ulong> checksums;
    
    public ChecksumLookup(string checksumBin) {
        var data = MemoryMarshal.Cast<byte, ulong>(File.ReadAllBytes(checksumBin));
        checksums = new Dictionary<ulong, ulong>();

        LoadChecksums(data);
    }

    public ulong? GetChecksum(ulong hash) {
        if (!checksums.TryGetValue(hash, out var checksum))
            return null;

        return checksum;
    }

    private void LoadChecksums(Span<ulong> checksumData) {
        var size = checksumData.Length / 2;
        var firstHalf = checksumData[..size];
        var secondHalf = checksumData[size..];

        for (int i = 0; i < size; i++) {
            checksums.Add(firstHalf[i], secondHalf[i]);
        }
    }
    
}