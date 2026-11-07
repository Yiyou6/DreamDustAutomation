using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DreamDustAutomation.UI;

// Rendering doubles only; native container measurement/placement bodies are
// injected from the locally inspected game assembly by Test-Layout.ps1.
namespace UnityEngine {
 public struct Vector2 { public float x,y; public static Vector2 zero=>new Vector2(); public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 operator+(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y); public static Vector2 operator-(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y); }
 public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
 public struct Rect { public Vector2 min,max; }
 public enum RenderMode { ScreenSpaceOverlay }
 public class Canvas { public Canvas rootCanvas=>this; public Rect pixelRect; public RenderMode renderMode; public object worldCamera; }
 public class RectTransform { public object parent; public Vector2 anchoredPosition,sizeDelta; public Vector3 localScale=new Vector3(1,1,1),localPosition; public Canvas canvas; public float pixelScale=1; public T GetComponentInParent<T>() where T:class=>canvas as T; }
 public static class RectTransformUtility { public static bool ScreenPointToLocalPointInRectangle(RectTransform p,Vector2 s,object c,out Vector2 point){point=new Vector2(s.x/p.pixelScale,s.y/p.pixelScale);return true;} }
 public static class Mathf { public static int Max(params int[] v)=>v.Max(); public static float Min(params float[] v)=>v.Min(); public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v)); }
}
public interface PixelElement { int widthPixels{get;} int heightPixels{get;} TreeNodeRender handler{get;} RectTransform rectTransform{get;} void Validate(); void ResizePixels(int w,int h); }
public interface GraphNodeStorage { }
public interface MachineryStorageOwner { }
public interface MachineryContainer { }
public interface MachineryWidget { bool fillWidth{get;} }
public class Image { public bool enabled; }
public class TreeNodeRender { public RectTransform rectTransform=new RectTransform(); public Image image=new Image(); public bool hidden; public int textWidth; public static TreeNodeRender Instantiate(PixelElement e=null,string name=null)=>new TreeNodeRender(); public TreeNodeRender SetParent(TreeNodeRender n){rectTransform.parent=n.rectTransform;return this;} }
internal static class PixelExtensions { internal static void SetRenderingParent(this PixelElement c,MachineRegion p){c.handler.SetParent(p.inner);} }
public static class MachineryStyle { public static void Place(TreeNodeRender n,int x,int y,int w,int h){n.rectTransform.anchoredPosition=new Vector2(x,-y);n.rectTransform.sizeDelta=new Vector2(w,h);} public static int TextWidth(TreeNodeRender n)=>n.textWidth; public static int Even(int n)=>(n+1)/2*2; }
public static class MachineryContainerUtils {
 public static bool IsHidden(PixelElement e)=>e.handler.hidden;
 public static void Refresh(PixelElement e){e?.Validate();}
 public static void TryResize(PixelElement e,int w,int h){if(e is MachineryContainer||e is MachineryWidget)e.ResizePixels(w,h);}
 public static void SetOwner(PixelElement e,MachineryStorageOwner owner){}
 public static void Collect(PixelElement e,List<GraphNodeStorage> list){if(e is GraphNodeStorage s)list.Add(s);else if(e is MachineRegion r)r.CollectStorage(list);}
}
public abstract class Element:PixelElement {
 public int widthPixels{get;protected set;} public int heightPixels{get;protected set;} public TreeNodeRender handler{get;}=new TreeNodeRender();
 public RectTransform rectTransform=>handler.rectTransform;
 public abstract void Validate(); public abstract void ResizePixels(int w,int h);
}
public class MachineryText:Element,MachineryWidget {
 public static int Measures;
 private int requestedWidth; public int naturalWidth,naturalHeight; public bool fillWidth=>true;
 public MachineryText(int w,int h){naturalWidth=w;naturalHeight=h;Validate();}
 public override void Validate(){Measures++;widthPixels=Math.Max(naturalWidth,requestedWidth);heightPixels=naturalHeight;}
 public override void ResizePixels(int w,int h){requestedWidth=w;Validate();}
}
public class MachineryProgressBar:MachineryText { private int requestedWidth; public MachineryProgressBar():base(70,8){} }
public class Inventory:Element,GraphNodeStorage {
 public Inventory(){widthPixels=144;heightPixels=96;}
 public override void Validate(){} public override void ResizePixels(int w,int h){throw new Exception("Inventory was resized");}
}
public class MachineryPanel:PixelElement,MachineryContainer {
 public RectTransform rectTransform=>handler.rectTransform;
 public int widthPixels{get;private set;} public int heightPixels{get;private set;} public TreeNodeRender handler{get;}=new TreeNodeRender();
 private int requestedWidth,requestedHeight,minWidth; private bool validateLock;
 private TreeNodeRender plate=new TreeNodeRender(),labelHost=new TreeNodeRender(),inner=new TreeNodeRender();
 public PixelElement content; public MachineryText label;
 public MachineryPanel(PixelElement c,bool titled=true){content=c;label=titled?new MachineryText(30,10):null;}
 // NATIVE_MachineryPanel
}
public class MachineryStack:PixelElement,MachineryContainer {
 public RectTransform rectTransform=>handler.rectTransform;
 public enum Align { Start,Center,End }
 public int widthPixels{get;private set;} public int heightPixels{get;private set;} public TreeNodeRender handler{get;}=new TreeNodeRender();
 public bool vertical; public int gap=4; public Align align=Align.Center,mainAlign=Align.Center;
 private int requestedWidth,requestedHeight; private bool validateLock; private TreeNodeRender inner=new TreeNodeRender();
 private List<PixelElement> children=new List<PixelElement>(); public IReadOnlyList<PixelElement> elements=>children;
 private PixelElement StretchTarget()=>null;
 public MachineryStack(bool vertical,params PixelElement[] items){this.vertical=vertical;children.AddRange(items);}
 // NATIVE_MachineryStack
}
public class MachineryTabs:PixelElement,MachineryContainer {
 public RectTransform rectTransform=>handler.rectTransform;
 private class Tab { public string caption="Tab"; public PixelElement page; public int width; public TreeNodeRender plate=new TreeNodeRender(),text=new TreeNodeRender(); }
 private List<Tab> tabs=new List<Tab>(); private int requestedWidth,requestedHeight; private bool validateLock;
 private TreeNodeRender strip=new TreeNodeRender(),underline=new TreeNodeRender(),inner=new TreeNodeRender();
 public int widthPixels{get;private set;} public int heightPixels{get;private set;} public TreeNodeRender handler{get;}=new TreeNodeRender();
 public int count=>tabs.Count; public PixelElement GetPage(int i)=>tabs[i].page;
 public MachineryTabs(params PixelElement[] pages){foreach(var page in pages)tabs.Add(new Tab{page=page});}
 // NATIVE_MachineryTabs
}
public class MachineryIntake:Element,MachineryContainer { private int requestedWidth,requestedHeight;
 public PixelElement content; public override void Validate(){} public override void ResizePixels(int w,int h){}
}
public class PixelWindow { public bool IsVisible()=>true; public int widthPixels,heightPixels; public RectTransform rectTransform=new RectTransform(); }
public static class LayoutSizingTests {
 static int checks;
 static void Check(bool condition,string message){checks++;if(!condition){Console.WriteLine("FAILED: "+message);throw new Exception(message);}}
 static string Measure(MachineryPanel[] panels,bool reset){using(MachineLayoutPass.Begin()){return MeasureRaw(panels,reset);}}
 static string MeasureRaw(MachineryPanel[] panels,bool reset){if(reset)foreach(var p in panels)MachineLayoutSizing.Reset(p);foreach(var p in panels)p.Validate();int h=panels.Max(p=>p.heightPixels);foreach(var p in panels)p.ResizePixels(p.widthPixels,h);return string.Join("/",panels.Select(p=>p.widthPixels+"x"+p.heightPixels));}
 public static void Main(){
  var text=new MachineryText(80,20);var advice=new MachineryText(70,20);advice.handler.hidden=true;var inventory=new Inventory();
  var controls=new MachineryStack(true,text,new MachineryPanel(new MachineryStack(true,advice,new MachineryStack(false,new MachineryText(20,20),new MachineryText(30,20)))));
  var panels=new[]{new MachineryPanel(new MachineryTabs(new MachineryStack(true,new MachineryText(80,20),inventory),new MachineryText(90,100))),new MachineryPanel(new MachineryTabs(controls,new MachineryTabs(new MachineryText(90,120),new MachineryText(150,160))))};
  string baseline=Measure(panels,true);
  for(int i=0;i<50;i++)Check(Measure(panels,true)==baseline,"Layout grows on repeated unchanged refresh");
  text.naturalWidth=600;advice.handler.hidden=false;advice.naturalHeight=700;Measure(panels,true);
  text.naturalWidth=80;advice.handler.hidden=true;advice.naturalHeight=20;
  Check(Measure(panels,false)!=baseline,"Positive control: native stale allocations were not reproduced");
  Check(Measure(panels,true)==baseline,"Shorter/hidden text must shrink to natural dimensions");
  for(int i=0;i<200;i++){text.naturalWidth=80+i%40;advice.handler.hidden=i%2==0;Measure(panels,true);text.naturalWidth=80;advice.handler.hidden=true;Check(Measure(panels,true)==baseline,"Repeated option changes accumulate size");}
  // Use the actual fixed-region implementation, with the native stack/panel/tab
  // bodies above. Information cards are below the work-area tabs; button slots
  // remain allocated even when their contents are hidden or grow after planning.
  var summary=new MachineryText(180,80);var residue=new MachineryText(160,60);var mode=new MachineryText(220,20);var start=new MachineryText(120,16);
  var result=new MachineryPanel(new MachineRegion(summary,MachineGeometry.InfoCardWidth-8,MachineGeometry.InfoCardHeight-22));
  var residual=new MachineryPanel(new MachineRegion(residue,MachineGeometry.InfoCardWidth-8,MachineGeometry.InfoCardHeight-22));
  var work=new MachineryTabs(new MachineRegion(inventory,MachineGeometry.WorkspaceWidth,MachineGeometry.StorageHeight),new MachineRegion(new MachineryText(280,200),MachineGeometry.WorkspaceWidth,MachineGeometry.StorageHeight));
  var infoCards=new MachineryStack(false,result,residual);infoCards.mainAlign=MachineryStack.Align.Start;
  var left=new MachineryStack(true,work,infoCards);left.mainAlign=MachineryStack.Align.Start;
  var modeSlot=new MachineRegion(mode,MachineGeometry.ControlWidth,20);var startSlot=new MachineRegion(start,MachineGeometry.ControlWidth,16);
  var right=new MachineryStack(true,modeSlot,startSlot);right.mainAlign=MachineryStack.Align.Start;
  var fixedPanels=new[]{new MachineryPanel(left),new MachineryPanel(new MachineryTabs(new MachineRegion(right,MachineGeometry.ControlWidth,MachineGeometry.ControlHeight)))};
  string fixedSize=Measure(fixedPanels,true);float top=result.handler.rectTransform.anchoredPosition.y;
  MachineryText.Measures=0;
  string uncachedSize=MeasureRaw(fixedPanels,true); int repeatedMeasures=MachineryText.Measures;
  MachineryText.Measures=0;
  Check(Measure(fixedPanels,true)==uncachedSize,"Pass caching changed native layout dimensions");
  int cachedMeasures=MachineryText.Measures;
  Check(cachedMeasures*2 < repeatedMeasures,"Fixed-region cache did not remove repeated subtree text measurements");
  Console.WriteLine("Native layout text measurements per pass: "+repeatedMeasures+" => "+cachedMeasures+"; identical dimensions (headless).");
  Check(fixedPanels.Max(p=>p.heightPixels)<=380,"Compact window must leave substantially less dead space than the 512px columns in 0.3.18");
  Check(fixedPanels.Sum(p=>p.widthPixels)<=520,"Compact columns expanded beyond their width budget");
  Check(fixedPanels[0].heightPixels==fixedPanels[1].heightPixels,"Both columns must agree without surplus padding");
  Check(fixedSize=="288x372/228x372","Information blocks changed the approved 0.3.19 window dimensions");
  Check(work.rectTransform.anchoredPosition.y==0,"Inventory tabs must be the first element of the workspace");
  // Header action must fit beside all existing tabs, including longer English
  // captions, without participating in page sizing or adding a saved tab.
  foreach(int[] captions in new[]{new[]{12,12,12,12},new[]{24,42,36,30}}){
   int tabsRight=4+captions.Sum(w=>MachineryStyle.Even(Math.Max(32,w+8))+2)-2;
   Check(tabsRight+4<=MachineGeometry.SortX,"Sort button overlaps a native storage tab");
   Check(MachineGeometry.SortX+MachineGeometry.SortWidth<=MachineGeometry.WorkspaceWidth-2,"Sort button escaped header");
  }
  Check(result.heightPixels==124&&residual.heightPixels==124,"Titled cards must have matching heights inside the old combined budget");
  Check(infoCards.rectTransform.anchoredPosition.y==-work.heightPixels-4,"Both cards must start immediately below the storage workspace");
  Check(result.rectTransform.anchoredPosition.x==0&&residual.rectTransform.anchoredPosition.x==result.widthPixels+4&&result.rectTransform.anchoredPosition.y==residual.rectTransform.anchoredPosition.y,"Cards must be side by side, with equal top offsets and a four-pixel gutter");
  float cardTop=infoCards.rectTransform.anchoredPosition.y;
  float residualTop=residual.rectTransform.anchoredPosition.y;float startTop=startSlot.rectTransform.anchoredPosition.y;
  for(int i=0;i<200;i++){
   summary.naturalWidth=100+i*5;summary.naturalHeight=20+i*3;residue.handler.hidden=i%2==0;
   residue.naturalHeight=10+i;mode.handler.hidden=i%3==0;mode.naturalWidth=50+i*4;
   Check(Measure(fixedPanels,true)==fixedSize,"Mode/preview/notice changed the fixed outer dimensions");
   Check(result.handler.rectTransform.anchoredPosition.y==top&&residual.rectTransform.anchoredPosition.y==residualTop&&infoCards.rectTransform.anchoredPosition.y==cardTop,"Work-area information moved relative to its upper edge");
   Check(startSlot.rectTransform.anchoredPosition.y==startTop,"Hidden notices moved an action button");
   Check(summary.widthPixels*summary.rectTransform.localScale.x<=MachineGeometry.InfoCardWidth-8+.001f&&summary.heightPixels*summary.rectTransform.localScale.y<=MachineGeometry.InfoCardHeight-22+.001f,"Long preview escaped its block padding");
   Check(residue.widthPixels*residue.rectTransform.localScale.x<=MachineGeometry.InfoCardWidth-8+.001f&&residue.heightPixels*residue.rectTransform.localScale.y<=MachineGeometry.InfoCardHeight-22+.001f,"Residue text escaped its block padding");
  }
  // Confirmation keeps a separate compact slot: an accidental double-click on
  // Start must not also confirm mixing. Hiding it never moves the stop action.
  var normalAction=new MachineryText(160,14);var confirmAction=new MachineryText(220,14);var stopAction=new MachineryText(160,14);
  var actionSlot=new MachineRegion(normalAction,MachineGeometry.ControlWidth,16);
  var quantityCaption=new MachineryText(150,8);var quantityTrack=new MachineryText(220,8);
  var quantity=new MachineryStack(true,quantityCaption,quantityTrack);quantity.gap=0;
  var confirmationSlot=new MachineRegion(new MachineryStack(true,quantity,confirmAction),MachineGeometry.ControlWidth,16);
  var holdAction=new MachineryText(90,14); stopAction.naturalWidth=90;
  var holdSlot=new MachineRegion(holdAction,(MachineGeometry.ControlWidth-4)/2,16);
  var stopSlot=new MachineRegion(new MachineryStack(false,holdSlot,new MachineRegion(stopAction,(MachineGeometry.ControlWidth-4)/2,16)),MachineGeometry.ControlWidth,16);
  var actionColumn=new MachineryStack(true,confirmationSlot,actionSlot,stopSlot);actionColumn.mainAlign=MachineryStack.Align.Start;
  float stopAnchor=0;
  for(int i=0;i<30;i++){
   confirmAction.handler.hidden=i%2!=0;
   quantity.handler.hidden=!confirmAction.handler.hidden;
   quantityCaption.naturalWidth=150+i;
   MachineLayoutSizing.Reset(actionColumn);actionColumn.Validate();
   if(i==0)stopAnchor=stopSlot.rectTransform.anchoredPosition.y;
   Check(actionColumn.heightPixels==56,"Confirmation visibility changed the reserved action area");
   Check(normalAction.rectTransform.localScale.x==1&&confirmAction.rectTransform.localScale.x==1,"Compact action must keep normal text size");
   Check(stopSlot.rectTransform.anchoredPosition.y==stopAnchor,"Confirmation displaced stop action");
   Check(quantity.widthPixels*quantity.rectTransform.localScale.x<=MachineGeometry.ControlWidth+.001f,"Quantity control escaped its slot");
  }
  // The new direct-mode picker occupies the existing hidden grade slot. Neither
  // switching mode nor a two-line disabled-module explanation may move Start.
  var gradeChoices=new MachineryText(190,35);
  var normalDryingChoices=new MachineryText(190,16);
  var dryingTitle=new MachineryText(120,8);var dryingButtons=new MachineryText(188,18);var dryingHint=new MachineryText(210,20);
  var dryingChoices=new MachineryStack(true,dryingTitle,dryingButtons,dryingHint);dryingChoices.gap=2;
  var choicesStack=new MachineryStack(true,gradeChoices,normalDryingChoices,dryingChoices);choicesStack.gap=3;
  var choiceSlot=new MachineRegion(choicesStack,MachineGeometry.ControlWidth,54);
  var calculateSlot=new MachineRegion(new MachineryText(150,14),MachineGeometry.ControlWidth,16);
  var choicesColumn=new MachineryStack(true,choiceSlot,calculateSlot);choicesColumn.gap=3;choicesColumn.mainAlign=MachineryStack.Align.Start;
  for(int i=0;i<40;i++){
   gradeChoices.handler.hidden=i%2==0;normalDryingChoices.handler.hidden=gradeChoices.handler.hidden;dryingChoices.handler.hidden=!gradeChoices.handler.hidden;
   dryingHint.naturalWidth=i%3==0?210:150;
   MachineLayoutSizing.Reset(choicesColumn);choicesColumn.Validate();
   Check(choicesColumn.heightPixels==73,"Drying selection grew the existing control region");
   Check(calculateSlot.rectTransform.anchoredPosition.y==-57,"Drying selection moved Calculate");
   Check(choiceSlot.Content.rectTransform.localScale.x==1,"Drying selection shrank its text");
  }
  var gathered=new List<GraphNodeStorage>();new MachineRegion(inventory,300,250).CollectStorage(gathered);
  Check(gathered.Count==1&&ReferenceEquals(gathered[0],inventory),"Fixed presentation must not add inventory identities");
  foreach(int[] size in new[]{new[]{1280,720},new[]{1920,1080},new[]{2560,1440},new[]{2560,1600},new[]{800,600}}){
   var window=new PixelWindow{widthPixels=900,heightPixels=1400};window.rectTransform.parent=new RectTransform();window.rectTransform.canvas=new Canvas{pixelRect=new Rect{min=new Vector2(0,0),max=new Vector2(size[0],size[1])}};window.rectTransform.localPosition=new Vector3(-90,3000,0);
   MachineLayoutSizing.Fit(window);var s=window.rectTransform.localScale.x;var p=window.rectTransform.localPosition;
   Check(p.x>=7.99f&&p.x+900*s<=size[0]-7.99f&&p.y<=size[1]-7.99f&&p.y-1400*s>=7.99f,"Window/title outside viewport");
   Check(900*s<=(size[0]-16)*.85f+.01f&&1400*s<=(size[1]-16)*.8f+.01f,"Window occupies too much of the usable viewport");
   MachineLayoutSizing.Fit(window);Check(Math.Abs(s-window.rectTransform.localScale.x)<0.00001f,"Fit compounds its own scale");
  }
  foreach(float canvasScale in new[]{1f,1.5f,2f,2.666667f,3f,4f})foreach(int[] size in new[]{new[]{1280,720},new[]{1920,1080},new[]{2560,1600}}){
   // Include letterboxed gameplay rather than equating display height with UI space.
   int band=size[1]==1600?80:0;
   var window=new PixelWindow{widthPixels=520,heightPixels=392};
   window.rectTransform.parent=new RectTransform{pixelScale=canvasScale};
   window.rectTransform.canvas=new Canvas{pixelRect=new Rect{min=new Vector2(0,band),max=new Vector2(size[0],size[1]-band)}};
   window.rectTransform.localPosition=new Vector3(-100,10000,0);
   MachineLayoutSizing.Fit(window);float s=window.rectTransform.localScale.x;var p=window.rectTransform.localPosition;
   Check(520*s*canvasScale<=(size[0]-16)*.85f+.01f&&392*s*canvasScale<=(size[1]-band*2-16)*.8f+.01f,"Canvas-scaled window exceeds 85% width/80% height");
   Check(p.x*canvasScale>=7.99f&&(p.x+520*s)*canvasScale<=size[0]-7.99f&&(p.y-392*s)*canvasScale>=band+7.99f&&p.y*canvasScale<=size[1]-band-7.99f,"Scaled/letterboxed title or footer is outside game viewport");
  }
  Console.WriteLine("Layout sizing: "+checks+" checks passed; compact columns "+fixedSize+", native containers, 200 mode/preview/residue changes, separate confirmation slot, stationary info/actions, storage identity and five viewport sizes with canvas scaling. Rendering mocked.");
 }
}
