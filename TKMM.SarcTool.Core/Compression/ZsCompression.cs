using SarcLibrary;
using TotkCommon;
using ZstdSharp;

namespace TKMM.SarcTool.Core;

internal class ZsCompression {
    private readonly Zstd compressor;
    
    private object syncRoot = new object();

    public ZsCompression(string packFilePath) {
        compressor = new Zstd();
        compressor.LoadDictionaries(packFilePath);
    }

    public Span<byte> Decompress(ReadOnlySpan<byte> compressed, out int dictionaryId) {
        lock (syncRoot) {
            return compressor.Decompress(compressed, out dictionaryId);
        }
    }

    public Span<byte> Compress(ReadOnlySpan<byte> data, int dictionaryId) {
        lock (syncRoot) {
            return compressor.Compress(data, dictionaryId);
        }
    }
}
