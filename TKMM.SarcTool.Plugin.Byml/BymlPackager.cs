using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using Revrs;
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

        return result.ToBinary(Endianness.Little);
    }

    private Byml PackageArray(BymlArray baseNode, BymlArray mergeNode) {

        var arrayContentsType = baseNode.Any() ? baseNode.First().Type :
            mergeNode.Any() ? mergeNode.First().Type :
            BymlNodeType.Null;

        if (arrayContentsType == BymlNodeType.Null)
            return baseNode;

    
        // All other node types are treated as a 1-for-1 index comparison
        var nodesToRemove = new List<Byml>();
        var nodesToAdd = new List<Byml>();
        
        // Add & Edit
        for (int i = 0; i < mergeNode.Count; i++) {
            if (i >= baseNode.Count) {
                // Adding
                nodesToAdd.Add(new BymlMap() {
                    ["~ADD~"] = mergeNode[i]
                });
            } else {
                // Edits
                var identical = GetHash(mergeNode[i].ToBinary(Endianness.Little)) == GetHash(baseNode[i].ToBinary(
                    Endianness.Little));

                Byml modNode;
                if (!identical && arrayContentsType == BymlNodeType.Map) {
                    modNode = MergeMap(baseNode[i].GetMap(), mergeNode[i].GetMap());
                } else if (!identical && arrayContentsType == BymlNodeType.HashMap32) {
                    modNode = MergeHashTable(baseNode[i].GetHashMap32(), mergeNode[i].GetHashMap32());
                } else if (!identical && arrayContentsType == BymlNodeType.HashMap64) {
                    modNode = MergeHashTable(baseNode[i].GetHashMap64(), mergeNode[i].GetHashMap64());
                } else {
                    modNode = mergeNode[i];
                }

                if (!identical) {
                    baseNode[i] = new BymlMap() {
                        ["~MOD~"] = modNode,
                        ["~INDEX~"] = i
                    };
                } else {
                    nodesToRemove.Add(baseNode[i]);
                }
            }
        }
        
        // Deletions
        if (baseNode.Count > mergeNode.Count) {
            for (int i = mergeNode.Count; i < baseNode.Count; i++) {
                nodesToAdd.Add(new BymlMap() {
                    ["~DEL~"] = baseNode[i],
                    ["~INDEX~"] = i
                });
            }
        }

        foreach (var node in nodesToAdd)
            baseNode.Add(node);
        foreach (var node in nodesToRemove)
            baseNode.Remove(node);
    

        return baseNode;
    }

    private BymlHashMap32 PackageHashTable(BymlHashMap32 baseNode, BymlHashMap32 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var baseHash = GetHash(baseNodeItem.ToBinary(Endianness.Little));
            var mergeHash = GetHash(item.Value.ToBinary(Endianness.Little));

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

            var baseHash = GetHash(baseNodeItem.ToBinary(Endianness.Little));
            var mergeHash = GetHash(item.Value.ToBinary(Endianness.Little));

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

            var baseHash = GetHash(baseNodeItem.ToBinary(Endianness.Little));
            var mergeHash = GetHash(item.Value.ToBinary(Endianness.Little));

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

        var nodesToRemove = new List<string>();
        foreach (var item in baseNode) {
            
            if (!mergeNode.TryGetValue(item.Key, out _)) {
                // Handle a deleted item
                nodesToRemove.Add(item.Key);
            }
        }

        // Remove and re-add with the "~DEL~" prefix so we know to remove the key
        foreach (var node in nodesToRemove) {
            var item = baseNode[node];
            baseNode.Remove(node);
            baseNode.Add("~DEL~" + node, item);
        }

        return baseNode;
    }
    
    
}