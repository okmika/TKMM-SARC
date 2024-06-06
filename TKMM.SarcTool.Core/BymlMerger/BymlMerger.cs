using System.Diagnostics;
using System.IO.Hashing;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using Revrs;

namespace TKMM.SarcTool.Core;

internal partial class BymlHandler : ISarcFileHandler {

    public string[] Extensions => new[] {"byml", "byaml", "bgyml"};
    
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

        return result.ToBinary(Endianness.Little);
    }

    private BymlArray MergeArray(BymlArray baseNode, BymlArray mergeNode) {
        // Handle arrays of any other type (usually primitives)
        var toDelete = new List<Byml>();
        foreach (var item in mergeNode) {
            if (item.Type == BymlNodeType.Map) {
                var itemMap = item.GetMap();
                if (itemMap.TryGetValue("~ADD~", out var addValue)) {
                    baseNode.Add(addValue);
                } else if (itemMap.TryGetValue("~INSERT~", out var insertValue)) {
                    baseNode.Insert(0, insertValue);
                } else if (itemMap.TryGetValue("~MOD~", out var modValue)) {
                    var index = itemMap["~INDEX~"].GetInt();

                    // We're modifying an index value that doesn't exist - insert the modded value at the end
                    if (index >= baseNode.Count) {
                        baseNode.Add(modValue);
                        continue;
                    }

                    if (baseNode[index].Type is BymlNodeType.Array)
                        baseNode[index] = MergeArray(baseNode[index].GetArray(), modValue.GetArray());
                    else if (baseNode[index].Type is BymlNodeType.HashMap32)
                        baseNode[index] = MergeHashTable(baseNode[index].GetHashMap32(), modValue.GetHashMap32());
                    else if (baseNode[index].Type is BymlNodeType.HashMap64)
                        baseNode[index] = MergeHashTable(baseNode[index].GetHashMap64(), modValue.GetHashMap64());
                    else if (baseNode[index].Type is BymlNodeType.Map)
                        baseNode[index] = MergeMap(baseNode[index].GetMap(), modValue.GetMap());
                    else
                        baseNode[index] = modValue;
                    
                } else if (itemMap.TryGetValue("~DEL~", out var _)) {
                    var index = itemMap["~INDEX~"].GetInt();

                    // We're deleting an index value that doesn't exist - skip
                    if (index >= baseNode.Count)
                        continue;

                    toDelete.Add(baseNode[index]);
                }
            }
        }

        foreach (var node in toDelete)
            baseNode.Remove(node);
    
        
        

        
        return baseNode;
    }

    private BymlHashMap32 MergeHashTable(BymlHashMap32 baseNode, BymlHashMap32 mergeNode) {

        foreach (var item in mergeNode) {
            // Process key deletion
            if (item.Value.Type == BymlNodeType.String && item.Value.GetString() == "~DEL~") {
                baseNode.Remove(item.Key);
                continue;
            }
            
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
            // Process key deletion
            if (item.Value.Type == BymlNodeType.String && item.Value.GetString() == "~DEL~") {
                baseNode.Remove(item.Key);
                continue;
            }
            
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
            // Process key deletion
            if (item.Value.Type == BymlNodeType.String && item.Value.GetString() == "~DEL~") {
                baseNode.Remove(item.Key);
                continue;
            }
            
            // Add new items
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