using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation;

public static class NetworkUpgradeList { public static Dictionary<string,Action> UnlockActions=new Dictionary<string,Action>(); }
public static class ItemFeatureHelper { public static GameItem SetShabby(GameItem item){item.shabby=true;return item;} }
public static class PreBuiltItemHelper {
 public static GameItem CreateEvaporator(int charge)=>new GameItem{identifier="evaporator",charge=charge};
 public static GameItem FullWaterPurificationTabletBox()=>new GameItem{identifier="purification_tablets"};
}
public static class RNG { public static bool all; public static bool Roll(int odds)=>all; }
public static class RetiredChemistGiftTests {
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Retired chemist: "+message);}
 static GameInventory[] Inventories()=>new[]{EmporiumEntry.Instance.backInvinvElement,EmporiumEntry.Instance.invElement,EmporiumEntry.Instance.showcaseElement,EmporiumEntry.Instance.frontInvinvElement,EmporiumEntry.Instance.afterhourInventory};
 static GameItem[] All()=>Inventories().SelectMany(inv=>GraphUtils.FindChildrenType<GameItem>(inv,passthrough:i=>true)).Distinct().ToArray();
 static int Machines()=>All().Count(i=>i.identifier==ModEntry.ItemId);
 static void Setup(){RetiredChemistGift.Uninstall();SaveManager.IsDecoding=false;GraphUtils.assemblyFails=false;PlayerStore.Instance=new PlayerStore{chemist=false};EmporiumEntry.Instance=new EmporiumEntry();NetworkUpgradeList.UnlockActions=new Dictionary<string,Action>{{"RETIRED_CHEMIST",NativeGift}};DirectoryMaster.created=0;}
 static void NativeGift(){
  // NATIVE_GIFT
 }
 static void WeeklyShop(){
  // NATIVE_WEEKLY
 }
 public static void Run(){
  Setup();NativeGift();var vanilla=All().GroupBy(i=>i.identifier).ToDictionary(g=>g.Key,g=>g.Count());
  Check(ChemistStarter.Equipment.All(id=>vanilla[id]==1),"native reward fixture changed");
  Setup();var old=ChemistStarter.Equipment.Select(id=>new GameItem{identifier=id,x=7,y=8}).ToArray();foreach(var i in old)GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,i);
  RetiredChemistGift.Install();var hook=NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"];RetiredChemistGift.Install();Check(hook==NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"],"duplicate install");
  Check(Machines()==0&&DirectoryMaster.created==0,"enabling mod gave a retroactive reward");
  hook();Check(Machines()==1,"unlock lacks one assembled machine");
  Check(All().Single(i=>i.identifier==ModEntry.ItemId).assembled,"reward machine missing equipment");
  Check(old.All(i=>!i.destroyed&&i.parentInventory==EmporiumEntry.Instance.invElement&&i.x==7&&i.y==8),"existing devices were removed or moved");
  Check(ChemistStarter.Equipment.All(id=>All().Count(i=>i.identifier==id)==1),"new native gifts were retained alongside machine");
  foreach(var kv in vanilla.Where(kv=>!ChemistStarter.Equipment.Contains(kv.Key)))Check(All().Count(i=>i.identifier==kv.Key)==kv.Value,"other native gift changed: "+kv.Key);
  Check(All().Single(i=>i.identifier=="chem_scanner").shabby,"scanner native condition changed");
  Check(PlayerStore.Instance.future.SequenceEqual(new[]{"retired_chemist:1"}),"first visit scheduling changed");
  foreach(bool all in new[]{false,true}){RNG.all=all;PlayerStore.Instance.offers.Clear();WeeklyShop();Check(ChemistStarter.Equipment.All(id=>PlayerStore.Instance.offers.Count(x=>x==id)==1),"weekly native equipment missing");Check(!PlayerStore.Instance.offers.Contains(ModEntry.ItemId),"weekly visit sold automatic machine");}
  RetiredChemistGift.Uninstall();Check(NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]==(Action)NativeGift,"disable did not restore native reward");

  // Gift cells are freed before placement, even when all reward shelves fill.
  Setup();int nativeCount=vanilla.Values.Sum();EmporiumEntry.Instance.backInvinvElement.capacity=nativeCount;
  foreach(var inv in Inventories().Skip(1))inv.capacity=0;
  RetiredChemistGift.Install();NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]();
  Check(Machines()==1&&All().Length==nativeCount-3,"full shelf prevented replacing the four gift devices");

  foreach(bool assemblyFailure in new[]{false,true}){
   Setup();GraphUtils.assemblyFails=assemblyFailure;if(!assemblyFailure)foreach(var inv in Inventories())inv.rejectMachine=true;
   RetiredChemistGift.Install();NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]();
   Check(Machines()==0&&All().Length==vanilla.Values.Sum(),"failed replacement lost a native gift");
   Check(ChemistStarter.Equipment.All(id=>All().Count(i=>i.identifier==id)==1),"failed replacement did not restore four originals");
  }
  Setup();NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]=()=>{NativeGift();All().First(i=>i.identifier=="blender").Destroy();};
  RetiredChemistGift.Install();NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]();Check(Machines()==0&&All().Any(i=>i.identifier=="heating_plate"),"incomplete gift caused partial deletion");

  // Another mod may wrap this callback. Disable/re-enable must neither clobber
  // its wrapper nor duplicate the native action or the replacement.
  Setup();RetiredChemistGift.Install();var captured=NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"];int otherCalls=0;
  Action other=()=>{otherCalls++;captured();};NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]=other;
  RetiredChemistGift.Uninstall();Check(NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]==other,"another mod callback overwritten");
  other();Check(Machines()==0&&otherCalls==1,"disabled captured wrapper still modified reward");
  EmporiumEntry.Instance=new EmporiumEntry();PlayerStore.Instance=new PlayerStore();RetiredChemistGift.Install();NetworkUpgradeList.UnlockActions["RETIRED_CHEMIST"]();
  Check(Machines()==1&&PlayerStore.Instance.future.Count==1&&otherCalls==2,"re-enable nested wrappers duplicated reward");
  RetiredChemistGift.Uninstall();
  Console.WriteLine("Retired chemist reward: "+checks+" checks passed; installed native gift/weekly sale delegates, existing-item protection, other gifts, visits, full-shelf replacement, rollback and hook lifecycle. Rendering mocked.");
 }
}
