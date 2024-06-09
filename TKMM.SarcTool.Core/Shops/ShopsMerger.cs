using System.Diagnostics;
using BymlLibrary;
using Revrs;
using SarcLibrary;

namespace TKMM.SarcTool.Core;

internal class ShopsMerger {

    private readonly ArchiveHelper archiveHelper;
    private readonly Queue<ShopMergerEntry> shops = new Queue<ShopMergerEntry>();
    private readonly HashSet<string> allShops;
    private readonly Stack<Byml> overflowEntries = new Stack<Byml>();

    private bool verbose = false;
    
    public Func<string, ShopMergerEntry>? GetEntryForShop { get; set; }

    public ShopsMerger(ArchiveHelper archiveHelper, HashSet<string> allShops, bool verbose = false) {
        this.archiveHelper = archiveHelper;
        this.allShops = allShops;
        this.verbose = verbose;
    }

    public void Add(string actor, string archivePath) {
        shops.Enqueue(new ShopMergerEntry(actor, archivePath));
    }

    public void MergeShops() {
        
        while (shops.Count > 0) {
            var shop = shops.Dequeue();

            if (allShops.Contains(shop.Actor))
                allShops.Remove(shop.Actor);

            if (verbose)
                Trace.TraceInformation("Processing shop for {0}", shop.Actor);
            
            var sarcBin = archiveHelper.GetFileContents(shop.ArchivePath, true, out var dictionaryId).ToArray();
            var sarc = Sarc.FromBinary(sarcBin);
            var key = $"Component/ShopParam/{shop.Actor}.game__component__ShopParam.bgyml";

            if (!sarc.ContainsKey(key)) {
                if (verbose)
                    Trace.TraceWarning("{0} does not contain shop param bgyml- skipping", shop.ArchivePath);
                
                continue;
            }

            var shopsByml = Byml.FromBinary(sarc[key]);
            if (shopsByml.Type != BymlNodeType.Map) {
                Trace.TraceWarning("Shop for {0} is not a map - skipping", shop.Actor);
                continue;
            }

            var goodsList = shopsByml.GetMap()["GoodsList"].GetArray();

            if (goodsList.Count > 111) {
                var goodsToOverflow = goodsList[111..];
                foreach (var item in goodsToOverflow) {
                    overflowEntries.Push(item);
                    goodsList.Remove(item);
                }

                Trace.TraceInformation("{0} shop overflowed {1}", shop.Actor, goodsToOverflow.Count);
                
                sarc[key] = shopsByml.ToBinary(Endianness.Little);
                archiveHelper.WriteFileContents(shop.ArchivePath, sarc, true, dictionaryId);
            }
            
            var wroteCount = 0;
            while (goodsList.Count < 111 && overflowEntries.Count > 0) {
                var nextItem = overflowEntries.Pop();
                goodsList.Add(nextItem);
                wroteCount++;
            }

            if (wroteCount > 0)
                Trace.TraceInformation("{0} shop added {1} overflow items", shop.Actor, wroteCount);

            if (overflowEntries.Count > 0 && shops.Count == 0 && allShops.Count == 0) {
                Trace.TraceWarning("Shop items overflow exceeds shops - discarding {0} shop entries",
                                   overflowEntries.Count);
            } else if (overflowEntries.Count > 0 && shops.Count == 0) {
                if (GetEntryForShop == null)
                    throw new Exception("No shop resolver specified");
                
                // Request a shop from the dump
                var nextShop = GetEntryForShop.Invoke(allShops.First());
                shops.Enqueue(nextShop);
            } 

            if (wroteCount > 0) {
                sarc[key] = shopsByml.ToBinary(Endianness.Little);
                archiveHelper.WriteFileContents(shop.ArchivePath, sarc, true, dictionaryId);
            }
        }
        
    }

    public class ShopMergerEntry(string actor, string archivePath) {
        public string Actor { get; init; } = actor;
        public string ArchivePath { get; init; } = archivePath;
    }

}