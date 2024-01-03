using SarcLibrary;
using ZstdSharp;

namespace TKMM.SarcTool.Compression;

public class ZsCompression {
    private const int CompressionLevel = 7;
    private Compressor commonCompressor = new(CompressionLevel);
    private Compressor bcettCompressor = new(CompressionLevel);
    private Compressor packCompressor = new(CompressionLevel);

    private Decompressor defaultDecompressor = new();
    private Decompressor commonDecompressor = new();
    private Decompressor bcettDecompressor = new();
    private Decompressor packDecompressor = new();

    public ZsCompression(string packFilePath) {
        var zsDic = File.ReadAllBytes(packFilePath);
        zsDic = defaultDecompressor.Unwrap(zsDic).ToArray();

        var sarc = Sarc.FromBinary(zsDic);

        commonDecompressor.LoadDictionary(sarc["zs.zsdic"]);
        bcettDecompressor.LoadDictionary(sarc["bcett.byml.zsdic"]);
        packDecompressor.LoadDictionary(sarc["pack.zsdic"]);

        commonCompressor.LoadDictionary(sarc["zs.zsdic"]);
        bcettCompressor.LoadDictionary(sarc["bcett.byml.zsdic"]);
        packCompressor.LoadDictionary(sarc["pack.zsdic"]);
    }

    public Span<byte> Decompress(ReadOnlySpan<byte> compressed, CompressionType type) {
        if (type == CompressionType.Common)
            return commonDecompressor.Unwrap(compressed);
        else if (type == CompressionType.Bcett)
            return bcettDecompressor.Unwrap(compressed);
        else if (type == CompressionType.Pack)
            return packDecompressor.Unwrap(compressed);

        throw new Exception("Invalid compression type");
    }

    public Span<byte> Compress(ReadOnlySpan<byte> data, CompressionType type) {
        if (type == CompressionType.Common)
            return commonCompressor.Wrap(data);
        else if (type == CompressionType.Bcett)
            return bcettCompressor.Wrap(data);
        else if (type == CompressionType.Pack)
            return packCompressor.Wrap(data);

        throw new Exception("Invalid compression type");
    }
}

public enum CompressionType {
    Common,
    Bcett,
    Pack
}