using System.IO.Hashing;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugin.BymlPlugin;

public partial class BymlHandler : ISarcFileHandler {
    
    public ReadOnlyMemory<byte> Merge(string fileName, IList<MergeFile> files) {

        var orderedFiles = files.OrderBy(l => l.Priority).ToList();

        var baseFile = Byml.FromBinary(orderedFiles[0].Contents.ToArray());
        var mergeFile = Byml.FromBinary(orderedFiles[1].Contents.ToArray());

        return Merge(baseFile, mergeFile);
    }
    
    
    private byte[] Merge(Byml baseFile, Byml mergeFile) {

        Byml result;
        if (baseFile.Type is BymlNodeType.Array && mergeFile.Type is BymlNodeType.Array) {
            result = MergeArray(baseFile.GetArray(), mergeFile.GetArray());
        } else if (baseFile.Type is BymlNodeType.HashMap32 && mergeFile.Type is BymlNodeType.HashMap32) {
            result = MergeHashTable(baseFile.GetHashMap32(), mergeFile.GetHashMap32());
        } else if (baseFile.Type is BymlNodeType.HashMap64 && mergeFile.Type is BymlNodeType.HashMap64) {
            result = MergeHashTable(baseFile.GetHashMap64(), mergeFile.GetHashMap64());
        } else if (baseFile.Type is BymlNodeType.Map && mergeFile.Type is BymlNodeType.Map) {
            result = MergeMap(baseFile.GetMap(), mergeFile.GetMap());
        } else if (baseFile.Type == mergeFile.Type) {
            result = mergeFile;
        } else {
            result = baseFile;
        }

        return result.ToBinary();
    }

    private BymlArray MergeArray(BymlArray baseNode, BymlArray mergeNode) {
        var itemHashes = baseNode.Select(l => GetHash(l.ToBinary())).ToHashSet();

        foreach (var item in mergeNode) {
            if (item.Type == BymlNodeType.String && item.GetString().StartsWith("~DEL~")) {
                var itemName = item.GetString().Substring(5);
                if (baseNode.Contains(itemName))
                    baseNode.Remove(itemName);
            } else {
                if (!itemHashes.Contains(GetHash(item.ToBinary())))
                    baseNode.Add(item);
            }
        }
        
        return baseNode;
    }

    private BymlHashMap32 MergeHashTable(BymlHashMap32 baseNode, BymlHashMap32 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = MergeArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = MergeMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }
        
        return baseNode;
    }

    private BymlHashMap64 MergeHashTable(BymlHashMap64 baseNode, BymlHashMap64 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = MergeArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = MergeMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }

        return baseNode;
    }

    private BymlMap MergeMap(BymlMap baseNode, BymlMap mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = MergeArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = MergeHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = MergeMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }

        return baseNode;
    }

    private ulong GetHash(byte[] data) {
        return XxHash64.HashToUInt64(data);
    }

    
}