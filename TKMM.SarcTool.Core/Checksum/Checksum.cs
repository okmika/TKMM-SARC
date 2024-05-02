namespace TKMM.SarcTool.Core;

internal static class Checksum {
    private static readonly uint[] table = CreateTable();

    public static uint ComputeNintendoHash(ReadOnlySpan<char> chars) {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < chars.Length; ++i) {
            byte index = (byte)(crc & 0xff ^ chars[i]);
            crc = crc >> 8 ^ table[index];
        }

        return unchecked(~crc);
    }

    // public static ulong ComputeXxHash(string str) {
    //     var bytes = Encoding.Unicode.GetBytes(str);
    //     return ComputeXxHash(bytes);
    // }

    // public static ulong ComputeXxHash(ReadOnlySpan<byte> bytes) {
    //     return XxHash64.HashToUInt64(bytes);
    // }

    static uint[] CreateTable() {
        const uint poly = 0xEDB88320;
        var localTable = new uint[256];
        for (uint i = 0; i < localTable.Length; ++i) {
            uint temp = i;
            for (int j = 8; j > 0; --j) {
                if ((temp & 1) == 1) {
                    temp = temp >> 1 ^ poly;
                } else {
                    temp >>= 1;
                }
            }

            localTable[i] = temp;
        }

        return localTable;
    }
}