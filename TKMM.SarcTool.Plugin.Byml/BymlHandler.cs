using System.IO.Hashing;
using System.Text;
using Nintendo.Byml;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugin.Byml;

public class BymlHandler : ISarcFileHandler {
    public ReadOnlyMemory<byte> Merge(string fileName, IList<MergeFile> files) {

        var orderedFiles = files.OrderBy(l => l.Priority).ToList();

        var baseFile = BymlFile.FromBinary(orderedFiles[0].Contents.ToArray());
        var mergeFile = BymlFile.FromBinary(orderedFiles[1].Contents.ToArray());

        if (baseFile.RootNode.Type is NodeType.Hash or NodeType.Array or NodeType.StringArray) {
            baseFile.RootNode = Merge(baseFile.RootNode, mergeFile.RootNode);
            return baseFile.ToBinary();
        } else {
            // Any other type, there's nothing to merge and we provide the
            // higher priority value
            return orderedFiles[1].Contents;
        }
    }

    private BymlNode Merge(BymlNode baseNode, BymlNode mergeNode) {
        if (baseNode.Type is NodeType.StringArray or NodeType.Array && baseNode.Type == mergeNode.Type) {
            return MergeArray(baseNode, mergeNode);
        } else if (baseNode.Type == NodeType.Hash && baseNode.Type == mergeNode.Type) {
            return MergeHashTable(baseNode, mergeNode);
        } else if (baseNode.Type == mergeNode.Type) {
            baseNode.Binary = mergeNode.Binary;
            return baseNode;
        }

        return baseNode;
    }

    private BymlNode MergeArray(BymlNode baseNode, BymlNode mergeNode) {
        if (baseNode.Type == NodeType.StringArray) {
            // Combine string arrays
            foreach (var item in mergeNode.StringArray) {
                if (!baseNode.StringArray.Contains(item))
                    baseNode.StringArray.Add(item);
            }
        } else if (baseNode.Type == NodeType.Array) {
            // Combine object arrays
            // TODO: There will need to be a way for us to be more granular with how we merge object arrays
            var baseItems = baseNode.Array.Select(l => GetHash(Encoding.Unicode.GetBytes(l.SerializeNode()))).ToHashSet();
            
            foreach (var item in mergeNode.Array) {
                var itemHash = GetHash(Encoding.Unicode.GetBytes(item.SerializeNode()));
                if (!baseItems.Contains(itemHash))
                    baseNode.Array.Add(item);
            }
        }

        return baseNode;
    }

    private BymlNode MergeHashTable(BymlNode baseNode, BymlNode mergeNode) {
        foreach (var item in mergeNode.Hash) {
            if (!baseNode.Hash.TryGetValue(item.Key, out var baseNodeItem)) {
                baseNode.Hash.Add(item.Key, item.Value);
                continue;
            }

            if (baseNodeItem.Type is NodeType.Array or NodeType.StringArray && item.Value.Type == baseNodeItem.Type) {
                baseNode.Hash[item.Key] = MergeArray(baseNodeItem, item.Value);
            } else if (baseNodeItem.Type == NodeType.Hash && item.Value.Type == baseNodeItem.Type) {
                baseNode.Hash[item.Key] = MergeHashTable(baseNodeItem, item.Value);
            } else if (baseNodeItem.Type == item.Value.Type) {
                baseNode.Hash[item.Key] = item.Value;
            }
        }

        return baseNode;
    }

    private ulong GetHash(byte[] data) {
        return XxHash64.HashToUInt64(data);
    }

    
}