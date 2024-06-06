using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using Revrs;

namespace TKMM.SarcTool.Core;

internal partial class BymlHandler {
    public ReadOnlyMemory<byte> Package(string fileName, IList<MergeFile> files, out bool isEmptyChangelog) {
        var orderedFiles = files.OrderBy(l => l.Priority).ToList();

        var baseFile = Byml.FromBinary(orderedFiles[0].Contents.ToArray());
        var mergeFile = Byml.FromBinary(orderedFiles[1].Contents.ToArray());

        return Package(baseFile, mergeFile, out isEmptyChangelog);
    }

    private byte[] Package(Byml baseFile, Byml mergeFile, out bool isEmptyChangelog) {
        Byml result;
        if (baseFile.Type is BymlNodeType.Array && mergeFile.Type is BymlNodeType.Array) {
            result = PackageArray(baseFile.GetArray(), mergeFile.GetArray());
            isEmptyChangelog = !result.GetArray().Any();
        } else if (baseFile.Type is BymlNodeType.HashMap32 && mergeFile.Type is BymlNodeType.HashMap32) {
            result = PackageHashTable(baseFile.GetHashMap32(), mergeFile.GetHashMap32());
            isEmptyChangelog = !result.GetHashMap32().Keys.Any();
        } else if (baseFile.Type is BymlNodeType.HashMap64 && mergeFile.Type is BymlNodeType.HashMap64) {
            result = PackageHashTable(baseFile.GetHashMap64(), mergeFile.GetHashMap64());
            isEmptyChangelog = !result.GetHashMap64().Keys.Any();
        } else if (baseFile.Type is BymlNodeType.Map && mergeFile.Type is BymlNodeType.Map) {
            result = PackageMap(baseFile.GetMap(), mergeFile.GetMap());
            isEmptyChangelog = !result.GetMap().Keys.Any();
        } else if (baseFile.Type == mergeFile.Type) {
            result = mergeFile;
            isEmptyChangelog = false;
        } else {
            result = baseFile;
            isEmptyChangelog = true;
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
                var identical = Byml.ValueEqualityComparer.Default.Equals(mergeNode[i], baseNode[i]);

                Byml modNode;
                if (!identical && arrayContentsType == BymlNodeType.Map) {
                    modNode = PackageMap(baseNode[i].GetMap(), mergeNode[i].GetMap());
                } else if (!identical && arrayContentsType == BymlNodeType.HashMap32) {
                    modNode = PackageHashTable(baseNode[i].GetHashMap32(), mergeNode[i].GetHashMap32());
                } else if (!identical && arrayContentsType == BymlNodeType.HashMap64) {
                    modNode = PackageHashTable(baseNode[i].GetHashMap64(), mergeNode[i].GetHashMap64());
                } else if (!identical && arrayContentsType == BymlNodeType.Array) {
                    modNode = PackageArray(baseNode[i].GetArray(), mergeNode[i].GetArray());
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

            var identical = Byml.ValueEqualityComparer.Default.Equals(baseNodeItem, item.Value);

            if (identical) {
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
            else if (item.Value.Type == baseNodeItem.Type) {
                if (!Byml.ValueEqualityComparer.Default.Equals(baseNode[item.Key], item.Value))
                    baseNode[item.Key] = item.Value;
                else
                    baseNode.Remove(item.Key);
            }

        }

        var deletions = new List<uint>();
        foreach (var item in baseNode) {

            if (!mergeNode.TryGetValue(item.Key, out _)) {
                deletions.Add(item.Key);
            }
        }

        // Handle a deleted item by setting its value to "~DEL~"
        foreach (var deletion in deletions)
            baseNode[deletion] = "~DEL~";

        return baseNode;
    }

    private BymlHashMap64 PackageHashTable(BymlHashMap64 baseNode, BymlHashMap64 mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var identical = Byml.ValueEqualityComparer.Default.Equals(baseNodeItem, item.Value);

            if (identical) {
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
            else if (item.Value.Type == baseNodeItem.Type) {
                if (!Byml.ValueEqualityComparer.Default.Equals(baseNode[item.Key], item.Value))
                    baseNode[item.Key] = item.Value;
                else
                    baseNode.Remove(item.Key);
            }

        }

        var deletions = new List<ulong>();
        foreach (var item in baseNode) {

            if (!mergeNode.TryGetValue(item.Key, out _)) {
                deletions.Add(item.Key);
            }
        }

        // Handle a deleted item by setting its value to "~DEL~"
        foreach (var deletion in deletions)
            baseNode[deletion] = "~DEL~";

        return baseNode;
    }

    private BymlMap PackageMap(BymlMap baseNode, BymlMap mergeNode) {

        foreach (var item in mergeNode) {
            if (!baseNode.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Add(item.Key, item.Value);
                continue;
            }

            var identical = Byml.ValueEqualityComparer.Default.Equals(baseNodeItem, item.Value);

            if (identical) {
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
            else if (item.Value.Type == baseNodeItem.Type) {
                if (!Byml.ValueEqualityComparer.Default.Equals(baseNode[item.Key], item.Value))
                    baseNode[item.Key] = item.Value;
                else
                    baseNode.Remove(item.Key);
            }

        }

        var deletions = new List<string>();
        foreach (var item in baseNode) {
            
            if (!mergeNode.TryGetValue(item.Key, out _)) {
                deletions.Add(item.Key);
            }
        }

        // Handle a deleted item by setting its value to "~DEL~"
        foreach(var deletion in deletions)
            baseNode[deletion] = "~DEL~";

        return baseNode;
    }
    
    
}