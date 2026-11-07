using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation;

// New-game handoff contract; native inventory rendering is not instantiated.
public class GameItem {
 public string identifier; public GameInventory parentInventory; public bool assembled,destroyed;
 public bool shabby; public int charge; public int x,y;
 public List<GameInventory> nested=new List<GameInventory>();
 public bool IsTag(string tag)=>assembled;
 public void Destroy(){destroyed=true;parentInventory?.childItems.Remove(this);parentInventory=null;}
}
public class GameInventory {
 public List<GameItem> childItems=new List<GameItem>(); public int capacity=100; public bool rejectMachine;
 public bool Expel(GameItem item){if(!childItems.Remove(item))return false;item.parentInventory=null;return true;}
 public bool UncheckedAccept(GameItem item)=>GraphUtils.TryAcceptAll(this,item)==0;
}
public static class SaveManager { public static bool IsDecoding; }
public class PlayerStore {
 public static PlayerStore Instance=new PlayerStore(); public bool chemist=true; public bool IsChemistStart()=>chemist;
 public List<string> future=new List<string>(), offers=new List<string>();
 public void QueueFuturClient(string id,int day){future.Add(id+":"+day);}
 public void AddDirectSellingItemToTable(string id){offers.Add(id);}
 public void AddDirectSellingItemToTable(GameItem item){offers.Add(item.identifier);}
}
public class EmporiumEntry { public static EmporiumEntry Instance=new EmporiumEntry(); public GameInventory invElement=new GameInventory(),backInvinvElement=new GameInventory(),showcaseElement=new GameInventory(),frontInvinvElement=new GameInventory(),afterhourInventory=new GameInventory(); }
public static class DirectoryMaster { public static int created; public static GameItem Item(string id){created++;return new GameItem{identifier=id};} }
public static class ModHelper { public static GameItem SetItemOwnerShip(GameItem item,bool owned)=>item; }
public static class GraphUtils {
 public static bool assemblyFails;
 public static int TryAcceptAll(GameInventory inv,GameItem item){if(item.parentInventory==inv)return 0;if(inv.childItems.Count>=inv.capacity||(inv.rejectMachine&&item.identifier==ModEntry.ItemId))return 1;item.parentInventory?.childItems.Remove(item);item.parentInventory=inv;inv.childItems.Add(item);if(item.identifier==ModEntry.ItemId)item.assembled=!assemblyFails;return 0;}
 public static List<T> FindChildrenType<T>(GameInventory inv,Func<T,bool> filter=null,Func<T,bool> passthrough=null){var found=new List<T>();foreach(var item in inv.childItems){if(item is T value&&(filter==null||filter(value)))found.Add(value);if(!(item is T)||passthrough!=null&&passthrough((T)(object)item))foreach(var nested in item.nested)found.AddRange(FindChildrenType(nested,filter,passthrough));}return found;}
 public static void EmporiumTryAdd(GameInventory inv,GameItem item){foreach(var target in new[]{inv,EmporiumEntry.Instance.backInvinvElement,EmporiumEntry.Instance.invElement,EmporiumEntry.Instance.showcaseElement,EmporiumEntry.Instance.frontInvinvElement})if(TryAcceptAll(target,item)==0)return;item.Destroy();}
}
namespace DreamDustAutomation { public static class ModEntry { public const string ItemId="dream_dust_automation.synthesizer"; public static readonly Logger Log=new Logger(); } public class Logger{public void Warning(string value){}} }
public static class ChemistStarterTests {
 static int checks;static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
 static GameItem[] Setup(){SaveManager.IsDecoding=false;PlayerStore.Instance=new PlayerStore();EmporiumEntry.Instance=new EmporiumEntry();DirectoryMaster.created=0;GraphUtils.assemblyFails=false;var items=ChemistStarter.Equipment.Select(id=>new GameItem{identifier=id}).Concat(new[]{new GameItem{identifier="chemistry_guide"},new GameItem{identifier="water_pitcher"}}).ToArray();foreach(var item in items)GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,item);return items;}
 static GameItem[] All()=>new[]{EmporiumEntry.Instance.invElement,EmporiumEntry.Instance.backInvinvElement,EmporiumEntry.Instance.showcaseElement,EmporiumEntry.Instance.frontInvinvElement}.SelectMany(i=>i.childItems).ToArray();
 public static void Main(){
  var originals=Setup();ChemistStarter.Give("chemist");
  Check(All().Count(i=>i.identifier==ModEntry.ItemId)==1,"New chemist lacks exactly one machine");
  Check(originals.Take(4).All(i=>i.destroyed),"Original four devices were not replaced");
  Check(originals.Skip(4).All(i=>!i.destroyed&&All().Contains(i)),"Other starting supplies changed");
  ChemistStarter.Give("chemist");Check(DirectoryMaster.created==1,"Duplicate event gave another machine");
  originals=Setup();SaveManager.IsDecoding=true;ChemistStarter.Give("chemist");Check(DirectoryMaster.created==0&&originals.All(i=>!i.destroyed),"Save load deleted existing devices");
  originals=Setup();PlayerStore.Instance.chemist=false;ChemistStarter.Give("farmer");Check(DirectoryMaster.created==0&&originals.All(i=>!i.destroyed),"Another start changed");
  originals=Setup();originals[0].Destroy();ChemistStarter.Give("custom");Check(DirectoryMaster.created==0&&originals.Skip(1).All(i=>!i.destroyed),"Modified scenario partially lost equipment");
  originals=Setup();foreach(var inv in new[]{EmporiumEntry.Instance.invElement,EmporiumEntry.Instance.backInvinvElement,EmporiumEntry.Instance.showcaseElement,EmporiumEntry.Instance.frontInvinvElement})inv.capacity=inv.childItems.Count;
  ChemistStarter.Give("chemist");Check(originals.All(i=>!i.destroyed)&&All().Length==6,"Full inventory destroyed starter kit");
  originals=Setup();GraphUtils.assemblyFails=true;ChemistStarter.Give("chemist");Check(originals.All(i=>!i.destroyed)&&All().Length==6,"Assembly failure destroyed original kit");
  originals=Setup();EmporiumEntry.Instance.invElement.capacity=6;ChemistStarter.Give("chemist");Check(All().Count(i=>i.identifier==ModEntry.ItemId)==1&&originals.Take(4).All(i=>i.destroyed),"Full main inventory prevented fallback placement");
  Check(EmporiumEntry.Instance.invElement.childItems.Any(i=>i.identifier==ModEntry.ItemId),"Machine was not returned to newly freed main inventory");
  RetiredChemistGiftTests.Run();
  Console.WriteLine("Chemist starter: "+checks+" checks passed; new-game only, four-device replacement, other supplies, duplicate/failure/full-inventory safety (headless contract).");
 }
}
