using BymlLibrary;
using Revrs;
using SarcLibrary;
using Spectre.Console;
using TKMM.SarcTool.Services;

namespace TKMM.SarcTool.Special;

internal class ShopsMerger {

    private readonly MergeService mergeService;
    private readonly Queue<ShopMergerEntry> shops = new Queue<ShopMergerEntry>();
    private readonly HashSet<string> allShops;
    private readonly Stack<Byml> overflowEntries = new Stack<Byml>();
    private readonly bool verbose;
    
    public Func<string, ShopMergerEntry>? GetEntryForShop { get; set; }

    public ShopsMerger(MergeService mergeService, HashSet<string> allShops, bool verbose) {
        this.mergeService = mergeService;
        this.verbose = verbose;
        this.allShops = allShops;
    }

    public void Add(string actor, string archivePath) {
        shops.Enqueue(new ShopMergerEntry(actor, archivePath));
    }

    public void MergeShops(StatusContext context) {
        
        while (shops.Count > 0) {
            var shop = shops.Dequeue();

            if (allShops.Contains(shop.Actor))
                allShops.Remove(shop.Actor);
            
            context.Status($"Processing shop for {shop.Actor}...");
            var sarcBin = mergeService.GetFileContents(shop.ArchivePath, true, true).ToArray();
            var sarc = Sarc.FromBinary(sarcBin);
            var key = $"Component/ShopParam/{shop.Actor}.game__component__ShopParam.bgyml";

            if (!sarc.ContainsKey(key)) {
                AnsiConsole.MarkupLineInterpolated($"! [yellow]{shop.ArchivePath} does not contain shop param bgyml. Skipping.[/]");
                continue;
            }

            var shopsByml = Byml.FromBinary(sarc[key]);
            if (shopsByml.Type != BymlNodeType.Map) {
                AnsiConsole.MarkupLineInterpolated($"! [yellow]Shop for {shop.Actor} is not a map. Skipping.[/]");
                continue;
            }

            var goodsList = shopsByml.GetMap()["GoodsList"].GetArray();

            if (goodsList.Count > 111) {
                var goodsToOverflow = goodsList[111..];
                foreach (var item in goodsToOverflow) {
                    overflowEntries.Push(item);
                    goodsList.Remove(item);
                }

                if (verbose)
                    AnsiConsole.MarkupLineInterpolated($"- {shop.Actor} overflowed {goodsToOverflow.Count}");

                sarc[key] = shopsByml.ToBinary(Endianness.Little);
                mergeService.WriteFileContents(shop.ArchivePath, sarc, true, true);
            }
            
            var wroteCount = 0;
            while (goodsList.Count < 111 && overflowEntries.Count > 0) {
                var nextItem = overflowEntries.Pop();
                goodsList.Add(nextItem);
                wroteCount++;
            }

            if (wroteCount > 0 && verbose)
                AnsiConsole.MarkupLineInterpolated($"- {shop.Actor} added {wroteCount} overflow items");

            if (overflowEntries.Count > 0 && shops.Count == 0 && allShops.Count == 0) {
                AnsiConsole.MarkupLineInterpolated($"X [red]Shop items overflow exceeds shops. Discarding {overflowEntries.Count} shop entries.[/]");
            } else if (overflowEntries.Count > 0 && shops.Count == 0) {
                if (GetEntryForShop == null)
                    throw new Exception("No shop resolver specified");
                
                // Request a shop from the dump
                var nextShop = GetEntryForShop.Invoke(allShops.First());
                shops.Enqueue(nextShop);
            } 

            if (wroteCount > 0) {
                sarc[key] = shopsByml.ToBinary(Endianness.Little);
                mergeService.WriteFileContents(shop.ArchivePath, sarc, true, true);
            }
        }
        
    }

    public class ShopMergerEntry(string actor, string archivePath) {
        public string Actor { get; init; } = actor;
        public string ArchivePath { get; init; } = archivePath;
    }

}