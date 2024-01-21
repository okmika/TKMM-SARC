using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugin.BymlPlugin;

public partial class BymlHandler {
    public ReadOnlyMemory<byte> Package(string fileName, IList<MergeFile> files) {
        var orderedFiles = files.OrderBy(l => l.Priority).ToList();

        var baseFile = Byml.FromBinary(orderedFiles[0].Contents.ToArray());
        var mergeFile = Byml.FromBinary(orderedFiles[1].Contents.ToArray());

        return Package(baseFile, mergeFile);
    }

    private byte[] Package(Byml baseFile, Byml mergeFile) {
        Byml result;
        if (baseFile.Type is BymlNodeType.Array && mergeFile.Type is BymlNodeType.Array) {
            result = PackageArray(baseFile.GetArray(), mergeFile.GetArray());
        } else if (baseFile.Type is BymlNodeType.HashMap32 && mergeFile.Type is BymlNodeType.HashMap32) {
            result = PackageHashTable(baseFile.GetHashMap32(), mergeFile.GetHashMap32());
        } else if (baseFile.Type is BymlNodeType.HashMap64 && mergeFile.Type is BymlNodeType.HashMap64) {
            result = PackageHashTable(baseFile.GetHashMap64(), mergeFile.GetHashMap64());
        } else if (baseFile.Type is BymlNodeType.Map && mergeFile.Type is BymlNodeType.Map) {
            result = PackageMap(baseFile.GetMap(), mergeFile.GetMap());
        } else if (baseFile.Type == mergeFile.Type) {
            result = mergeFile;
        } else {
            result = baseFile;
        }

        return result.ToBinary();
    }

    private Byml PackageArray(BymlArray baseNode, BymlArray mergeNode) {
        var baseItemHashes = baseNode.Select(l => GetHash(l.ToBinary())).ToList();
        var nodeItemHashes = mergeNode.Select(l => GetHash(l.ToBinary())).ToHashSet();
                
        var nodesToRemove = new List<Byml>();
        var nodesToAdd = new List<Byml>();

        foreach (var node in mergeNode) {
            var nodeHash = GetHash(node.ToBinary());
            if (baseItemHashes.Contains(nodeHash))
                nodesToRemove.Add(baseNode[baseItemHashes.IndexOf(nodeHash)]);
            else if (!baseItemHashes.Contains(nodeHash))
                nodesToAdd.Add(node);
        }

        for (var i = 0; i < baseNode.Count; i++) {
            var nodeHash = GetHash(baseNode[i].ToBinary());
            if (!nodeItemHashes.Contains(nodeHash)) {
                nodesToRemove.Add(i);

                if (baseNode[i].Type == BymlNodeType.String)
                    nodesToAdd.Add("~DEL~" + baseNode[i].GetString());
            }
        }

        foreach (var index in nodesToRemove)
            baseNode.Remove(index);

        foreach (var node in nodesToAdd)
            baseNode.Add(node);

        return baseNode;
    }

    private BymlHashMap32 PackageHashTable(BymlHashMap32 baseNode, BymlHashMap32 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var baseHash = GetHash(baseNodeItem.ToBinary());
            var mergeHash = GetHash(item.Value.ToBinary());

            if (baseHash == mergeHash) {
                baseNode.Remove(item.Key);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = PackageArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = PackageMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }

        return baseNode;
    }

    private BymlHashMap64 PackageHashTable(BymlHashMap64 baseNode, BymlHashMap64 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var baseHash = GetHash(baseNodeItem.ToBinary());
            var mergeHash = GetHash(item.Value.ToBinary());

            if (baseHash == mergeHash) {
                baseNode.Remove(item.Key);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = PackageArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = PackageMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }

        return baseNode;
    }

    private BymlMap PackageMap(BymlMap baseNode, BymlMap mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var baseHash = GetHash(baseNodeItem.ToBinary());
            var mergeHash = GetHash(item.Value.ToBinary());

            if (baseHash == mergeHash) {
                baseNode.Remove(item.Key);
                continue;
            }

            if (item.Value.Type is BymlNodeType.Array && baseNodeItem.Type is BymlNodeType.Array)
                baseNode[item.Key] = PackageArray(baseNodeItem.GetArray(), item.Value.GetArray());
            else if (item.Value.Type is BymlNodeType.HashMap32 && baseNodeItem.Type is BymlNodeType.HashMap32)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap32(), item.Value.GetHashMap32());
            else if (item.Value.Type is BymlNodeType.HashMap64 && baseNodeItem.Type is BymlNodeType.HashMap64)
                baseNode[item.Key] = PackageHashTable(baseNodeItem.GetHashMap64(), item.Value.GetHashMap64());
            else if (item.Value.Type is BymlNodeType.Map && baseNodeItem.Type is BymlNodeType.Map)
                baseNode[item.Key] = PackageMap(baseNodeItem.GetMap(), item.Value.GetMap());
            else if (item.Value.Type == baseNodeItem.Type)
                baseNode[item.Key] = item.Value;

        }

        return baseNode;
    }
    
    
}