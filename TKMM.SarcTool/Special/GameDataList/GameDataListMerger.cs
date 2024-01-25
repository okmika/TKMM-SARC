using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Spectre.Console;

namespace TKMM.SarcTool.Special;

internal class GameDataListMerger {

    public bool Compare(Memory<byte> firstBytes, Memory<byte> secondBytes) {
        var first = Byml.FromBinary(firstBytes.Span);
        var second = Byml.FromBinary(secondBytes.Span);

        var firstBase = first.GetMap()["Data"].GetMap();
        var secondBase = second.GetMap()["Data"].GetMap();

        try {
            var changes = CreateChangelog(firstBase, secondBase);


            if (changes.Count != 0) {
                AnsiConsole.MarkupLineInterpolated($"[bold]Compare:[/] [red]{changes.Count} change(s) detected![/]");
                return false;
            }

            AnsiConsole.MarkupLineInterpolated($"[bold]Compare:[/] [green]No changes detected![/]");
            return true;
        } catch (NotSupportedException) {
            // This is only thrown when entries are deleted -- that should be an exception during packaging with
            // a vanilla GDL, but in case of comparing it's just a sign that there's a difference.
            AnsiConsole.MarkupLineInterpolated($"[bold]Compare:[/] [red]Changes detected![/]");
            return false;
        }
    }
    
    public Memory<byte> Merge(Memory<byte> fileBytes, Memory<byte> changelogBytes) {
        var output = Byml.FromBinary(fileBytes.Span);
        var tables = output.GetMap()["Data"]
                           .GetMap();

        var changelogStream = new MemoryStream(changelogBytes.ToArray());
        var changelogReader = new GameDataListReader(changelogStream);

        var dicts = new Dictionary<string, Dictionary<ulong, BymlMap>>();
        
        foreach (var change in changelogReader) {
            if (!dicts.TryGetValue(change.Table, out var tableDict)) {
                tableDict = tables[change.Table].GetArray()
                                                .ToDictionary(
                                                    l => change.Table == "Bool64bitKey"
                                                        ? l.GetMap()["Hash"].GetUInt64()
                                                        : l.GetMap()["Hash"].GetUInt32(),
                                                    l => l.GetMap());

                dicts.Add(change.Table, tableDict);
            }
            
            var table = tables[change.Table].GetArray();
            WriteChange(change, change.Table, table, tableDict);
        }

        return output.ToBinary();
    }

    public Memory<byte> Package(Memory<byte> vanillaBytes, Memory<byte> modifiedBytes) {

        var vanilla = Byml.FromBinary(vanillaBytes.Span);
        var modified = Byml.FromBinary(modifiedBytes.Span);

        if (vanilla.Type != BymlNodeType.Map || modified.Type != BymlNodeType.Map)
            throw new Exception("Invalid GDL file");

        var vanillaBase = vanilla.GetMap()["Data"].GetMap();
        var modifiedBase = modified.GetMap()["Data"].GetMap();

        var changes = CreateChangelog(vanillaBase, modifiedBase);
        
        if (changes.Count == 0)
            return Memory<byte>.Empty;

        var serializedChanges = SerializeChangelog(changes);
        return serializedChanges;

    }

    private void WriteChange(GameDataListChange change, string tableName, BymlArray table, Dictionary<ulong, BymlMap> tableDict) {
        if (change.Change == GameDataListChangeType.Unknown)
            throw new InvalidOperationException($"Invalid changelog entry: 'Unknown' operation for hash {(change.Hash32 != 0 ? change.Hash32 : change.Hash64)} in table {tableName}");
        else if (change.Change == GameDataListChangeType.None)
            return;
        
        if (!tableDict.TryGetValue(change.Hash32 != 0 ? change.Hash32 : change.Hash64, out var existing)) {
            // Add a new record if we're set up for that
            if (change.Change == GameDataListChangeType.Edit)
                throw new InvalidOperationException($"Changelog inconsistency: 'Edit' operation on non-existent hash {(change.Hash32 != 0 ? change.Hash32 : change.Hash64)} in table {tableName}");

            var newItem = new BymlMap();
            WriteItemForTable(change, newItem, tableName);

            table.Add(newItem);
        } else {
            if (change.Change == GameDataListChangeType.Add)
                throw new InvalidOperationException($"Changelog inconsistency: 'Add' operation on existing hash {(change.Hash32 != 0 ? change.Hash32 : change.Hash64)} in table {tableName}");
            
            WriteItemForTable(change, existing, tableName);
        }
    }

    private void WriteItemForTable(GameDataListChange change, BymlMap item, string table) {
        if (table != "Bool64bitKey")
            item["Hash"] = change.Hash32;
        else
            item["Hash"] = change.Hash64;

        var hash = change.Hash32 != 0 ? change.Hash32 : change.Hash64;

        item["ResetTypeValue"] = change.ResetTypeValue;
        item["SaveFileIndex"] = change.SaveFileIndex;

        if (change.ArraySize != 0)
            item["ArraySize"] = change.ArraySize;
        if (change.OriginalSize != 0)
            item["OriginalSize"] = change.OriginalSize;
        if (change.ExtraByte != 0)
            item["ExtraByte"] = change.ExtraByte;
        if (change.Size != 0)
            item["Size"] = change.Size;

        // Nothing else to do for this table type
        if (table == "Bool64bitKey")
            return;

        if (table == "Binary") {
            item["DefaultValue"] = (uint)(change.DefaultValue[0].Value);
        } else if (table == "Bool") {
            item["DefaultValue"] = (bool)(change.DefaultValue[0].Value);
        } else if (table == "BoolArray") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();
            
            WriteValueArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "BoolExp") {
            if (change.Change == GameDataListChangeType.Add)
                item["Values"] = new BymlArray();

            var itemArray = item["Values"].GetArray();
            var changeArray = change.Values;

            WriteSubArray(changeArray, table, itemArray, hash, "Values");
        } else if (table == "Enum" || table == "EnumArray") {
            if (change.Change == GameDataListChangeType.Add) {
                item["Values"] = new BymlArray();
            }

            item["DefaultValue"] = (uint)(change.DefaultValue[0].Value);
            item["RawValues"] = new BymlArray();

            foreach (var rawValue in change.RawValues)
                item["RawValues"].GetArray().Add(rawValue);

            var itemArray = item["Values"].GetArray();
            var changeArray = change.Values;

            WriteSubArray(changeArray, table, itemArray, hash, "Values");
        } else if (table == "Float") {
            item["DefaultValue"] = (float)change.DefaultValue[0].Value;
        } else if (table == "FloatArray") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();
            
            WriteValueArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "Int") {
            item["DefaultValue"] = (int)change.Values[0].Value;
        } else if (table == "IntArray") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();

            WriteValueArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "String16" || table == "String32" || table == "String64" || table == "WString16") {
            item["DefaultValue"] = (string)change.DefaultValue[0].Value;
        } else if (table == "String64Array" || table == "WString16Array") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();

            WriteValueArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "Struct") {
            WriteStructArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "UInt") {
            item["DefaultValue"] = (uint)change.DefaultValue[0].Value;
        }  else if (table == "UInt64") {
            item["DefaultValue"] = (ulong)change.DefaultValue[0].Value;
        } else if (table == "UInt64Array" || table == "UIntArray") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();

            WriteValueArray(change.DefaultValue, item["DefaultValue"]);
        } else if (table == "Vector2" || table == "Vector3") {
            item["DefaultValue"] = WriteVector(change.DefaultValue, table == "Vector3");
        } else if (table == "Vector2Array" || table == "Vector3Array") {
            if (change.Change == GameDataListChangeType.Add)
                item["DefaultValue"] = new BymlArray();

            WriteVectorArray(change.DefaultValue, item["DefaultValue"], table == "Vector3");
        } else {
            throw new NotSupportedException($"Cannot handle table type: {table}");
        }
        
    }

    private void WriteVectorArray(GameDataListValue[] change, Byml value, bool isVector3) {
        var valueArray = value.GetArray();
        for (int i = 0; i < change.Length; i++) {
            var vecValue = valueArray[i].Value as float[];

            if (vecValue == null)
                throw new Exception("Invalid vector value in changelog");

            var newVector = new BymlMap() {
                ["x"] = vecValue[0],
                ["y"] = vecValue[1]
            };

            if (isVector3)
                newVector["z"] = vecValue[2];

            if (i >= valueArray.Count && change[i].Change == GameDataListChangeType.Edit)
                throw new Exception("Edit operation failed on value - existing element not found");
            else if (change[i].Change == GameDataListChangeType.Add) {
                valueArray.Add(newVector);
            } else if (change[i].Change == GameDataListChangeType.Edit) {
                valueArray[i] = newVector;
            }
        }
    }

    private BymlMap WriteVector(GameDataListValue[] change, bool isVector3) {
        var vecValue = change[0].Value as float[];

        if (vecValue == null)
            throw new Exception("Invalid vector value in changelog");

        var newVector = new BymlMap() {
            ["x"] = vecValue[0],
            ["y"] = vecValue[1]
        };

        if (isVector3)
            newVector["z"] = vecValue[2];

        return newVector;
    }

    private void WriteStructArray(GameDataListValue[] change, Byml value) {
        var valueArray = value.GetArray();
        for (int i = 0; i < change.Length; i++) {
            var structValue = change[i].Value as uint[];
            
            if (structValue == null)
                throw new Exception("Invalid struct value in changelog");
            
            if (i >= valueArray.Count && change[i].Change == GameDataListChangeType.Edit)
                throw new Exception("Edit operation failed on value - existing element not found");
            else if (change[i].Change == GameDataListChangeType.Add) {
                valueArray.Add(new BymlMap() {
                    ["Hash"] = structValue[0],
                    ["Value"] = structValue[1]
                });
            } else if (change[i].Change == GameDataListChangeType.Edit) {
                valueArray[i] = new BymlMap() {
                    ["Hash"] = structValue[0],
                    ["Value"] = structValue[1]
                };
            }
        }
    }

    private void WriteValueArray(GameDataListValue[] change, Byml value) {
        var valueArray = value.GetArray();

        for (int i = 0; i < change.Length; i++) {
            if (i >= valueArray.Count && change[i].Change == GameDataListChangeType.Edit)
                throw new Exception("Edit operation failed on value - existing element not found");
            else if (change[i].Change == GameDataListChangeType.Add)
                valueArray.Add(MakeValue(change[i].Value));
            else if (change[i].Change == GameDataListChangeType.Edit)
                valueArray[i] = MakeValue(change[i].Value);
        }
    }

    private Byml MakeValue(object value) {
        if (value is float valueFloat)
            return new Byml(valueFloat);
        else if (value is uint valueUInt)
            return new Byml(valueUInt);
        else if (value is ulong valueULong)
            return new Byml(valueULong);
        else if (value is string valueString)
            return new Byml(valueString);
        else if (value is int valueInt)
            return new Byml(valueInt);
        else
            throw new Exception("Unknown value type");
    }

    private void WriteSubArray(GameDataListValue[] changeArray, string table, BymlArray itemArray, ulong hash, string changeArrayDescription) {
        for (int i = 0; i < changeArray.Length; i++) {
            if (changeArray[i].Change == GameDataListChangeType.Edit && i >= itemArray.Count)
                throw new Exception($"Edit operation on hash {hash} in table {table} failed: {changeArrayDescription} element {i} does not exist.");

            var changeValue = changeArray[i].Value as ulong[];

            if (changeValue == null)
                throw new Exception($"Change value is null: {hash} in {table}, {changeArrayDescription} element {i}");
                
            if (changeArray[i].Change == GameDataListChangeType.Add) {
                var newSubArrayItem = new BymlArray();
                for (int inner = 0; i < changeValue.Length; i++)
                    newSubArrayItem.Add(inner);

                itemArray.Add(newSubArrayItem);
            } else if (changeArray[i].Change == GameDataListChangeType.Edit) {
                var itemSubArray = itemArray[i].GetArray();
                for (int inner = 0; i < changeValue.Length; i++) {
                    if (inner >= itemSubArray.Count)
                        itemSubArray.Add(changeValue[inner]);
                    else
                        itemSubArray[inner] = changeValue[inner];
                }
            }

                
        }
    }

    private Memory<byte> SerializeChangelog(List<GameDataListChange> changes) {
        var output = new MemoryStream();
        var serializer = new GameDataListWriter(output);

        foreach (var change in changes)
            serializer.Write(change);

        serializer.FinalizeAndClose();

        var outputBytes = output.ToArray().AsMemory();
        output.Close();
        output.Dispose();

        return outputBytes;
    }

    private List<GameDataListChange> CreateChangelog(BymlMap vanilla, BymlMap modified) {
        var changes = new List<GameDataListChange>();
        
        foreach (var table in vanilla) {
            var vanillaTable = table.Value.GetArray();
            var modifiedTable = modified[table.Key].GetArray();

            if (modifiedTable.Count < vanillaTable.Count)
                throw new NotSupportedException($"GDL table {table.Key}: No support for deleting vanilla GDL entries");
            
            if (table.Key == "Bool64bitKey") {
                var vanillaMapping = vanillaTable.ToDictionary(l => l.GetMap()["Hash"].GetUInt64(), l => l.GetMap());
                var modifiedMapping = modifiedTable.ToDictionary(l => l.GetMap()["Hash"].GetUInt64(), l => l.GetMap());

                if (!vanillaMapping.Keys.All(l => modifiedMapping.ContainsKey(l)))
                    throw new NotSupportedException("Deleting vanilla entries is not supported");

                var index = 0;
                foreach (var item in modifiedTable) {
                    
                    var itemMap = item.GetMap();
                    try {
                        // Add any items that are new
                        if (!vanillaMapping.ContainsKey(itemMap["Hash"].GetUInt64())) {
                            var change = CreateChange(table.Key, itemMap);
                            change.Change = GameDataListChangeType.Add;

                            foreach (var value in change.Values)
                                value.Change = GameDataListChangeType.Add;
                            foreach (var value in change.DefaultValue)
                                value.Change = GameDataListChangeType.Add;

                            change.Index = index;
                            changes.Add(change);
                        } else {

                            var change = ReconcileChanges(item, vanillaMapping[itemMap["Hash"].GetUInt64()], table.Key);

                            if (change != null) {
                                change.Change = GameDataListChangeType.Edit;
                                change.Index = index;
                                changes.Add(change);
                            }

                        }

                        index++;
                    } catch {
                        AnsiConsole.MarkupInterpolated($"X [red]Failed to generate changelog: {table.Key} {itemMap["Hash"].GetUInt64()}[/]");
                        throw;
                    }
                }
            } else {
                var vanillaMapping = vanillaTable.ToDictionary(l => l.GetMap()["Hash"].GetUInt32(), l => l.GetMap());
                var modifiedMapping = modifiedTable.ToDictionary(l => l.GetMap()["Hash"].GetUInt32(), l => l.GetMap());

                if (!vanillaMapping.Keys.All(l => modifiedMapping.ContainsKey(l)))
                    throw new NotSupportedException("Deleting vanilla entries is not supported");

                var index = 0;
                foreach (var item in modifiedTable) {
                    var itemMap = item.GetMap();
                    try {
                        // Add any items that are new
                        if (!vanillaMapping.ContainsKey(itemMap["Hash"].GetUInt32())) {
                            var change = CreateChange(table.Key, itemMap);
                            change.Change = GameDataListChangeType.Add;

                            foreach (var value in change.Values)
                                value.Change = GameDataListChangeType.Add;
                            foreach (var value in change.DefaultValue)
                                value.Change = GameDataListChangeType.Add;

                            change.Index = index;
                            changes.Add(change);
                        } else {

                            var change = ReconcileChanges(item, vanillaMapping[itemMap["Hash"].GetUInt32()], table.Key);

                            if (change != null) {
                                change.Change = GameDataListChangeType.Edit;
                                change.Index = index;
                                changes.Add(change);
                            }

                        }

                        index++;
                    } catch {
                        AnsiConsole.MarkupInterpolated($"X [red]Failed to generate changelog: {table.Key} {itemMap["Hash"].GetUInt32()}[/]");
                        throw;
                    }
                }
            }
        }

        return changes;
    }

    private GameDataListChange? ReconcileChanges(Byml modified, Byml vanilla, string table) {
        var modifiedObj = CreateChange(table, modified.GetMap());
        var vanillaObj = CreateChange(table, vanilla.GetMap());

        if (modifiedObj.IsSameAs(vanillaObj))
            return null;

        modifiedObj.Change = GameDataListChangeType.Edit;

        if (modifiedObj.DefaultValue.Length < vanillaObj.DefaultValue.Length)
            throw new NotSupportedException($"Deletion of array elements is not supported in DefaultValue: " +
                                            $"{(modifiedObj.Hash32 != 0 ? modifiedObj.Hash32 : modifiedObj.Hash64)} " +
                                            $"in table {table}");
        
        if (modifiedObj.Values.Length < vanillaObj.Values.Length)
            throw new NotSupportedException($"Deletion of array elements is not supported in Values: " +
                                            $"{(modifiedObj.Hash32 != 0 ? modifiedObj.Hash32 : modifiedObj.Hash64)} " +
                                            $"in table {table}");

        ReconcileValueChanges(modifiedObj.DefaultValue, vanillaObj.DefaultValue);
        ReconcileValueChanges(modifiedObj.Values, vanillaObj.Values);

        return modifiedObj;
    }

    private void ReconcileValueChanges(GameDataListValue[] modified, GameDataListValue[] vanilla) {
        for (int i = 0; i < modified.Length; i++) {
            if (i >= vanilla.Length) {
                modified[i].Change = GameDataListChangeType.Add;
            } else {
                if (!modified[i].Value.Equals(vanilla[i].Value))
                    modified[i].Change = GameDataListChangeType.Edit;
                else
                    modified[i].Change = GameDataListChangeType.None;
            }
        }
    }

    private GameDataListChange CreateChange(string table, BymlMap item) {
        var change = new GameDataListChange();
        var isArray = IsArrayTable(table);
        change.Table = table;
        change.Type = GetTypeFromTable(table);

        if (table == "Bool64bitKey")
            change.Hash64 = item["Hash"].GetUInt64();
        else
            change.Hash32 = item["Hash"].GetUInt32();

        if (item.TryGetValue("ResetTypeValue", out var resetTypeValue))
            change.ResetTypeValue = resetTypeValue.GetInt();
        if (item.TryGetValue("SaveFileIndex", out var saveFileIndex))
            change.SaveFileIndex = saveFileIndex.GetInt();
        if (item.TryGetValue("ExtraByte", out var extraByte))
            change.ExtraByte = extraByte.GetInt();
        if (item.TryGetValue("ArraySize", out var arraySize))
            change.ArraySize = arraySize.GetUInt32();
        if (item.TryGetValue("OriginalSize", out var originalSize))
            change.OriginalSize = originalSize.GetUInt32();
        if (item.TryGetValue("Size", out var size))
            change.Size = size.GetUInt32();

        if (item.TryGetValue("DefaultValue", out var defaultValue)) {
            change.DefaultValue = GetValues(defaultValue, change.Type, table, isArray);
        }

        if (item.TryGetValue("Values", out var values)) {
            change.Values = GetValues(values, change.Type, table, true);
        }

        if (item.TryGetValue("RawValues", out var rawValues)) {
            change.RawValues = rawValues.GetArray().Select(l => l.GetString()).ToArray();
        }

        return change;

    }

    private GameDataListValue[] GetValues(Byml values, GameDataListValueType valueType, string table, bool isArray) {
        List<GameDataListValue> output = new List<GameDataListValue>();

        if (table == "Struct") {
            var index = 0u;
            foreach (var item in values.GetArray()) {
                var itemMap = item.GetMap();
                var newValue = new GameDataListValue();
                newValue.Value = new[] {itemMap["Hash"].GetUInt32(), itemMap["Value"].GetUInt32()};
                newValue.Index = index;

                output.Add(newValue);

                index++;
            }
        } else if (table == "Vector2") {
            var itemMap = values.GetMap();
            output.Add(new GameDataListValue() {
                Value = new[] {itemMap["x"].GetFloat(), itemMap["y"].GetFloat()}
            });
        } else if (table == "Vector2Array") {
            var index = 0u;
            foreach (var item in values.GetArray()) {
                var itemMap = item.GetMap();
                output.Add(new GameDataListValue() {
                    Value = new[] {itemMap["x"].GetFloat(), itemMap["y"].GetFloat()},
                    Index = index
                });

                index++;
            }
        } else if (table == "Vector3") {
            var itemMap = values.GetMap();
            output.Add(new GameDataListValue() {
                Value = new[] {itemMap["x"].GetFloat(), itemMap["y"].GetFloat(), itemMap["z"].GetFloat()}
            });
        } else if (table == "Vector3Array") {
            var index = 0u;
            foreach (var item in values.GetArray()) {
                var itemMap = item.GetMap();
                output.Add(new GameDataListValue() {
                    Value = new[] {itemMap["x"].GetFloat(), itemMap["y"].GetFloat(), itemMap["z"].GetFloat()},
                    Index = index
                });

                index++;
            }
        } else if (table == "BoolExp") {
            var index = 0u;
            foreach (var item in values.GetArray()) {
                var itemArray = item.GetArray();
                output.Add(new GameDataListValue() {
                    Value = itemArray.Select(l => l.GetUInt64()).ToArray(),
                    Index = index
                });

                index++;
            }
        } else if (table == "Enum" || table == "EnumArray") {
            if (values.Type == BymlNodeType.UInt32)
                output.Add(GetValue(values, GameDataListValueType.UInt32));
            else if (values.Type == BymlNodeType.UInt64)
                output.Add(GetValue(values, GameDataListValueType.UInt64));
            else if (values.Type == BymlNodeType.Array) {
                var items = values.GetArray().Select(l => GetValue(l, GameDataListValueType.UInt64));
                var index = 0u;
                foreach (var item in items) {
                    item.Index = index;
                    output.Add(item);
                    index++;
                }
            }
        } else if (isArray) {
            var index = 0u;
            foreach (var item in values.GetArray()) {
                var newValue = GetValue(item, valueType);
                newValue.Index = index;

                output.Add(newValue);
                index++;
            }
        } else {
            output.Add(GetValue(values, valueType));
        }

        return output.ToArray();
    }

    private GameDataListValue GetValue(Byml value, GameDataListValueType valueType) {
        var outputValue = new GameDataListValue();

        outputValue.Value = valueType switch {
            GameDataListValueType.Boolean => value.GetBool(),
            GameDataListValueType.Float => value.GetFloat(),
            GameDataListValueType.Int32 => value.GetInt(),
            GameDataListValueType.Int64 => value.GetInt64(),
            GameDataListValueType.String => value.GetString(),
            GameDataListValueType.UInt32 => value.GetUInt32(),
            GameDataListValueType.UInt64 => value.GetUInt64(),
            _ => throw new NotSupportedException($"Unsupported value type {valueType}")
        };

        return outputValue;
    }

    private bool IsArrayTable(string table) {
        if (table == "EnumArray")
            return false;
        else if (table == "BinaryArray")
            return false;
        
        return table.EndsWith("Array");
    }

    private GameDataListValueType GetTypeFromTable(string table) {
        return table switch {
            "Binary" => GameDataListValueType.UInt32,
            "BinaryArray" => GameDataListValueType.UInt32,
            "Bool" => GameDataListValueType.Boolean,
            "Bool64bitKey" => GameDataListValueType.UInt64,
            "BoolArray" => GameDataListValueType.Boolean,
            "BoolExp" => GameDataListValueType.UInt64,
            "Enum" => GameDataListValueType.UInt64,
            "EnumArray" => GameDataListValueType.Int64,
            "Float" => GameDataListValueType.Float,
            "FloatArray" => GameDataListValueType.Float,
            "Int" => GameDataListValueType.Int32,
            "IntArray" => GameDataListValueType.Int32,
            "String16" => GameDataListValueType.String,
            "String32" => GameDataListValueType.String,
            "String64" => GameDataListValueType.String,
            "String64Array" => GameDataListValueType.String,
            "Struct" => GameDataListValueType.UInt32,
            "UInt" => GameDataListValueType.UInt32,
            "UInt64" => GameDataListValueType.UInt64,
            "UInt64Array" => GameDataListValueType.UInt64,
            "UIntArray" => GameDataListValueType.Int64,
            "Vector2" => GameDataListValueType.Float,
            "Vector2Array" => GameDataListValueType.Float,
            "Vector3" => GameDataListValueType.Float,
            "Vector3Array" => GameDataListValueType.Float,
            "WString16" => GameDataListValueType.String,
            "WString16Array" => GameDataListValueType.String,
            _ => throw new NotSupportedException($"Table type {table} is not supported")
        };
    }

}

