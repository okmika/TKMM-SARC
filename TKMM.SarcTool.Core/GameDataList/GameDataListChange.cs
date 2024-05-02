using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace TKMM.SarcTool.Core;

internal class GameDataListReader : IDisposable, IEnumerable<GameDataListChange> {

    private readonly Stream inputStream;
    private readonly BinaryReader reader;
    private ushort maxRecords;
    private ushort recordsRead;

    public GameDataListReader(Stream inputStream) {
        this.inputStream = inputStream;
        this.reader = new BinaryReader(inputStream);

        ReadHeader();
    }

    private GameDataListChange ReadNext() {
        var item = new GameDataListChange();
        
        item.Change = (GameDataListChangeType)reader.ReadByte();
        item.Table = reader.ReadString();

        if (item.Table == "Bool64bitKey")
            item.Hash64 = reader.ReadUInt64();
        else
            item.Hash32 = (uint)reader.ReadUInt64();

        item.Index = reader.ReadInt32();
        item.ExtraByte = reader.ReadInt32();
        item.ArraySize = reader.ReadUInt32();
        item.OriginalSize = reader.ReadUInt32();
        item.SaveFileIndex = reader.ReadInt32();
        item.ResetTypeValue = reader.ReadInt32();
        item.Size = reader.ReadUInt32();

        item.Values = ReadValues(item.Table);
        item.DefaultValue = ReadValues(item.Table);

        var rawValueLength = reader.ReadInt32();

        if (rawValueLength == 0) {
            item.RawValues = new string[0];
        } else {
            item.RawValues = new string[rawValueLength];
            for (int i = 0; i < rawValueLength; i++) {
                item.RawValues[i] = reader.ReadString();
            }
        }

        recordsRead++;
        return item;
    }

    private GameDataListValue[] ReadValues(string table) {
        var valueLength = reader.ReadInt32();
        var output = new GameDataListValue[valueLength];

        if (valueLength == 0) {
            return output;
        } else {
            for (int i = 0; i < valueLength; i++) {
                output[i] = new GameDataListValue();
                output[i].Change = (GameDataListChangeType)reader.ReadByte();
                output[i].Type = (GameDataListValueType)reader.ReadByte();
                output[i].Index = reader.ReadUInt32();

                var valueType = output[i].Type;

                // Handle special cases for different table types
                if (table == "Struct") {
                    var length = reader.ReadInt32();
                    var outputObj = new uint[length];
                    for (int inner = 0; inner < length; inner++)
                        outputObj[inner] = reader.ReadUInt32();

                    output[i].Value = outputObj;
                } else if (table == "Vector2" || table == "Vector3" || table == "Vector2Array" || table == "Vector3Array") {
                    var length = reader.ReadInt32();
                    var outputObj = new float[length];
                    for (int inner = 0; inner < length; inner++)
                        outputObj[inner] = reader.ReadSingle();

                    output[i].Value = outputObj;
                } else if (table == "BoolExp") {
                    var length = reader.ReadInt32();
                    var outputObj = new ulong[length];
                    for (int inner = 0; inner < length; inner++)
                        outputObj[inner] = reader.ReadUInt64();

                    output[i].Value = outputObj;
                } else {
                    var length = reader.ReadInt32();

                    if (length > 0) {
                        var outputObj = new object[length];

                        for (int inner = 0; inner < length; inner++) {
                            outputObj[inner] = valueType switch {
                                GameDataListValueType.Boolean => reader.ReadBoolean(),
                                GameDataListValueType.Float => reader.ReadSingle(),
                                GameDataListValueType.Int32 => reader.ReadInt32(),
                                GameDataListValueType.Int64 => reader.ReadInt64(),
                                GameDataListValueType.String => reader.ReadString(),
                                GameDataListValueType.UInt32 => reader.ReadUInt32(),
                                GameDataListValueType.UInt64 => reader.ReadUInt64(),
                                _ => throw new Exception("Unknown GDL value type")
                            };
                        }

                        output[i].Value = valueType switch {
                            GameDataListValueType.Boolean => outputObj.Cast<bool>().ToArray(),
                            GameDataListValueType.Float => outputObj.Cast<float>().ToArray(),
                            GameDataListValueType.Int32 => outputObj.Cast<int>().ToArray(),
                            GameDataListValueType.Int64 => outputObj.Cast<long>().ToArray(),
                            GameDataListValueType.String => outputObj.Cast<string>().ToArray(),
                            GameDataListValueType.UInt32 => outputObj.Cast<uint>().ToArray(),
                            GameDataListValueType.UInt64 => outputObj.Cast<ulong>().ToArray(),
                            _ => throw new Exception("Unknown GDL value type")
                        };
                    } else {
                        output[i].Value = valueType switch {
                            GameDataListValueType.Boolean => reader.ReadBoolean(),
                            GameDataListValueType.Float => reader.ReadSingle(),
                            GameDataListValueType.Int32 => reader.ReadInt32(),
                            GameDataListValueType.Int64 => reader.ReadInt64(),
                            GameDataListValueType.String => reader.ReadString(),
                            GameDataListValueType.UInt32 => reader.ReadUInt32(),
                            GameDataListValueType.UInt64 => reader.ReadUInt64(),
                            _ => throw new Exception("Unknown GDL value type")
                        };
                    }
                }
            }
        }

        return output;
    }

    private void ReadHeader() {
        var magic = reader.ReadChars(5);
        var version = reader.ReadByte();
        maxRecords = reader.ReadUInt16();

        if (new string(magic) != "GDLCL")
            throw new InvalidDataException("Invalid GDL change log");

        if (version != 1)
            throw new InvalidDataException($"GDL change log version {version} not supported");
    }
    
    public void Dispose() {
        reader.Close();
        inputStream.Close();
        reader.Dispose();
        inputStream.Dispose();
    }

    public IEnumerator<GameDataListChange> GetEnumerator() {
        while (recordsRead < maxRecords) {
            yield return ReadNext();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}

internal class GameDataListWriter : IDisposable {
    private readonly Stream outputStream;
    private readonly BinaryWriter writer;
    private ushort recordsWritten = 0;

    public GameDataListWriter(Stream outputStream) {
        this.outputStream = outputStream;
        this.writer = new BinaryWriter(outputStream);
        WriteHeader();
    }

    private void WriteHeader() {
        writer.Write("GDLCL".ToCharArray());
        writer.Write((byte)1);
        writer.Write((ushort)0);            // Byte 6 = this will be the number of changes
    }

    public void Write(GameDataListChange data) {
        writer.Write((byte)data.Change);
        writer.Write(data.Table);

        if (data.Hash32 != 0)
            writer.Write((ulong)data.Hash32);
        else
            writer.Write(data.Hash64);

        writer.Write(data.Index);
        writer.Write(data.ExtraByte);
        writer.Write(data.ArraySize);
        writer.Write(data.OriginalSize);
        writer.Write(data.SaveFileIndex);
        writer.Write(data.ResetTypeValue);
        writer.Write(data.Size);

        WriteValues(data.Values);
        WriteValues(data.DefaultValue);

        if (data.RawValues == null || data.RawValues.Length == 0)
            writer.Write(0);
        else {
            writer.Write(data.RawValues.Length);
            foreach (var item in data.RawValues)
                writer.Write(item);
        }

        recordsWritten++;
    }

    private void  WriteValues(GameDataListValue[] values) {
        if (values == null || values.Length == 0) {
            writer.Write(0);
        } else {
            writer.Write(values.Length);
            foreach (var item in values) {
                writer.Write((byte)item.Change);
                writer.Write((byte)item.Type);
                writer.Write(item.Index);
                item.WriteBytesInto(writer);
            }
        }
    }

    public void FinalizeAndClose() {
        // Write the total number of records written in byte 6
        writer.Seek(6, SeekOrigin.Begin);
        writer.Write(recordsWritten);
        
        // Flush & close
        writer.Flush();
        outputStream.Flush();
        
        // Reset the output to 0th position - do not close or dispose
        outputStream.Seek(0, SeekOrigin.Begin);
    }

    public void Dispose() {
        writer.Dispose();
    }

}

internal class GameDataListChange {
    public GameDataListChangeType Change;
    public string Table = "";
    public uint Hash32;
    public ulong Hash64;
    public int ExtraByte;
    public GameDataListValue[] Values = new GameDataListValue[0];
    public GameDataListValue[] DefaultValue = new GameDataListValue[0];
    public string[] RawValues = new string[0];
    public uint ArraySize;
    public uint OriginalSize;
    public uint Size;
    public int SaveFileIndex;
    public int ResetTypeValue;
    public int Index;

    public bool IsSameAs(GameDataListChange compare) {
        var isSame = Hash32 == compare.Hash32 &&
                     Index == compare.Index &&
                     Hash64 == compare.Hash64 &&
                     ExtraByte == compare.ExtraByte &&
                     ArraySize == compare.ArraySize &&
                     OriginalSize == compare.OriginalSize &&
                     Size == compare.Size &&
                     SaveFileIndex == compare.SaveFileIndex &&
                     ResetTypeValue == compare.ResetTypeValue &&
                     RawValues?.Length == compare.RawValues?.Length &&
                     Values?.Length == compare.Values?.Length &&
                     DefaultValue?.Length == compare.DefaultValue?.Length;

        if (!isSame)
            return false;

        if (Values?.Length > 0) {
            for (int i = 0; i < Values.Length; i++) {
                if (Values[i].Value is ulong[] uLongArray) {
                    if (!AreArraysIdentical(uLongArray, (compare.Values![i].Value as ulong[])!))
                        return false;
                } else if (Values[i].Value is uint[] uIntArray) {
                    if (!AreArraysIdentical(uIntArray, (compare.Values![i].Value as uint[])!))
                        return false;
                } else if (Values[i].Value is float[] floatArray) {
                    if (!AreArraysIdentical(floatArray, (compare.Values![i].Value as float[])!))
                        return false;
                } else {
                    if (!Values[i].Value.Equals(compare.Values![i].Value))
                        return false;
                }
            }
        }

        if (DefaultValue?.Length > 0) {
            for (int i = 0; i < DefaultValue.Length; i++) {
                if (DefaultValue[i].Value is ulong[] uLongArray) {
                    if (!AreArraysIdentical(uLongArray, (compare.DefaultValue![i].Value as ulong[])!))
                        return false;
                } else if (DefaultValue[i].Value is uint[] uIntArray) {
                    if (!AreArraysIdentical(uIntArray, (compare.DefaultValue![i].Value as uint[])!))
                        return false;
                } else if (DefaultValue[i].Value is float[] floatArray) {
                    if (!AreArraysIdentical(floatArray, (compare.DefaultValue![i].Value as float[])!))
                        return false;
                } else {
                    if (!DefaultValue[i].Value.Equals(compare.DefaultValue![i].Value))
                        return false;
                }
                
            }
                
        }

        if (RawValues?.Length > 0) {
            for (int i = 0; i < RawValues.Length; i++)
                if (RawValues[i] != compare.RawValues![i])
                    return false;
        }

        return true;
    }

    private bool AreArraysIdentical<T>(T[] left, T[] right) where T : struct {
        if (left.Length != right.Length)
            return false;
        
        for (int i = 0; i < left.Length; i++)
            if (!left[i].Equals(right[i]))
                return false;

        return true;
    }
}

#nullable disable
internal class GameDataListValue {
    public GameDataListChangeType Change;
    public GameDataListValueType Type;
    public uint Index;
    public object Value;

    public void WriteBytesInto(BinaryWriter writer) {
        if (Value is bool valueBool) {
            writer.Write(0);
            writer.Write(valueBool);
        } else if (Value is int valueInt) {
            writer.Write(0);
            writer.Write(valueInt);
        } else if (Value is long valueLong) {
            writer.Write(0);
            writer.Write(valueLong);
        } else if (Value is uint valueUInt) {
            writer.Write(0);
            writer.Write(valueUInt);
        } else if (Value is ulong valueULong) {
            writer.Write(0);
            writer.Write(valueULong);
        } else if (Value is byte valueByte) {
            writer.Write(0);
            writer.Write(valueByte);
        } else if (Value is string valueStr) {
            writer.Write(0);
            writer.Write(valueStr);
        } else if (Value is float valueFloat) {
            writer.Write(0);
            writer.Write(valueFloat);
        } else if (Value is ulong[] valueULongArray) {
            writer.Write(valueULongArray.Length);
            foreach (var item in valueULongArray)
                writer.Write(item);
        } else if (Value is uint[] valueUIntArray) {
            writer.Write(valueUIntArray.Length);
            foreach (var item in valueUIntArray)
                writer.Write(item);
        } else if (Value is float[] valueFloatArray) {
            writer.Write(valueFloatArray.Length);
            foreach (var item in valueFloatArray)
                writer.Write(item);
        } else {
            throw new Exception("Type not supported");
        }
    }
    
}

#nullable restore

internal enum GameDataListChangeType : byte {
    Unknown = 0,
    Add = 1,
    Edit = 2,
    Delete = 3,
    None = 4
}

internal enum GameDataListValueType : byte {
    Unknown,
    String,
    Boolean,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float
}