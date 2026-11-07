// Uses the game's actual accept/expel, render-parent and sprite positioning
// methods. Only Unity transforms/scene services are replaced for headless tests.
public interface PixelElement {
    TreeNodeRender handler{get;} TreeNodeRender inner{get;}
    // NATIVE_SetRenderingParent
    // NATIVE_IsRenderingChildOf
    // NATIVE_RemoveRenderingParent
}
public class SlotMarker { }
public class OverlayHandler { public static OverlayHandler current=new OverlayHandler(); public void UpdateInventoryStateUI(){} }
public static class GraphUtils { public static T FindParentType<T>(object o) where T:class=>null; }
public abstract class GameInventory {
    public Action<GameItem,GameInventory,SlotMarker> onSlotAddItemFunc,onSlotAddItemEndFunc,onSlotRemoveItemFunc,onSlotRemoveItemEndFunc;
    public abstract IEnumerable<GameItem> childItems{get;}
    public SlotMarker GetIncompleteItemInventorySlot(GameItem item)=>new SlotMarker();
    public abstract bool UncheckedAccept(GameItem item); public abstract bool Expel(GameItem item);
}
public class GameGridScrollableInventory:GameInventory,PixelElement {
    private readonly List<GameItem> items=new List<GameItem>(); public bool alwaysStack;
    public TreeNodeRender handler{get;}=new TreeNodeRender(); public TreeNodeRender inner{get;}=new TreeNodeRender();
    public override IEnumerable<GameItem> childItems=>items;
    public GameGridScrollableInventory(){inner.SetParent(handler);}
    public GameItem FastStackUnchecked(GameItem item)=>null;
    // NATIVE_UncheckedAccept
    // NATIVE_Expel
}
public static class InventoryVisualTests {
    private static int checks;
    private static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    private static bool Near(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)<.002 && Math.Abs(a.y-b.y)<.002;
    private static Vector3 Center(GameItemElement i)=>i.rotator.rectTransform.TransformPoint(i.rotator.rectTransform.rect.center);
    private static Vector3 Expected(GameGridScrollableInventory inv,GameItemElement i){
        bool rotated=i.modifiedShape.orientation%2!=0;
        var size=i.rotator.rectTransform.sizeDelta;
        return inv.inner.rectTransform.TransformPoint(new Vector3(i.modifiedShape.minX*16+(rotated?size.y:size.x)/2,
            -i.modifiedShape.minY*16-(rotated?size.x:size.y)/2,0));
    }
    private static GameGridScrollableInventory Bin(float scale,int scroll,bool bind){
        var outer=new TreeNodeRender();outer.rectTransform.localScale=new Vector3(scale,scale,1);outer.rectTransform.anchoredPosition=new Vector2(83,-57);
        var inv=new GameGridScrollableInventory();inv.handler.SetParent(outer);inv.inner.rectTransform.anchoredPosition=new Vector2(0,scroll*16);
        if(bind)DreamDustAutomation.UI.MachineInventoryVisuals.Bind(inv);return inv;
    }
    private static GameItemElement Item(int width,int height,int x,int y,int rotation,bool flip){
        var item=new GameItemElement();item.modifiedShape=new Shape{minX=x,minY=y,orientation=rotation,flipped=flip};item.tempShape=item.modifiedShape;
        var size=new Vector2(width*16,height*16);
        item.middle.rectTransform.sizeDelta=item.rotator.rectTransform.sizeDelta=item.fakeMiddle.rectTransform.sizeDelta=item.fakeRotator.rectTransform.sizeDelta=size;
        return item;
    }
    public static void Run(){
        // Must reproduce the user's symptom through native Expel -> Accept,
        // before installing our callbacks. This was missing in the ghost test.
        var broken=Bin(.65f,16,false);var sample=Item(2,4,6,19,0,false);broken.UncheckedAccept(sample);
        Check(Near(Center(sample),Expected(broken,sample)),"Baseline insertion should align");
        broken.Expel(sample);broken.UncheckedAccept(sample);
        Check(sample.handler.rectTransform.localScale.x<1 && !Near(Center(sample),Expected(broken,sample)),"Did not reproduce shrunken, shifted stored sprite");
        Check(sample.modifiedShape.minX==6 && sample.modifiedShape.minY==19,"Native logical position unexpectedly changed");
        DreamDustAutomation.UI.MachineInventoryVisuals.Restore(broken);
        Check(Near(Center(sample),Expected(broken,sample)),"Open-time recovery did not repair stored sprite");

        foreach(float scale in new[]{.4f,.65f,.85f,1f,1.5f})
        foreach(int scroll in new[]{0,8,16,152})
        foreach(var size in new[]{new[]{1,2},new[]{2,4},new[]{2,3},new[]{3,3}})
        foreach(int rotation in new[]{0,1,2,3}) foreach(bool flip in new[]{false,true}) {
            var raw=Bin(scale,scroll,true);var waste=Bin(.78f*scale,scroll,true);var backpack=Bin(1,0,false);
            var item=Item(size[0],size[1],3,scroll+2,rotation,flip);backpack.UncheckedAccept(item);
            var shape=item.modifiedShape;
            // Incoming drag from vanilla, then repeated moves inside the scaled
            // inventory, followed by cross-bin and outgoing vanilla transfers.
            item.fakeMiddle.SetParent(RenderHandler.current.overlayRoot,true);
            backpack.Expel(item);raw.UncheckedAccept(item);
            Check(Near(Center(item),Expected(raw,item)),"Incoming sprite mismatch");
            for(int move=0;move<12;move++) {
                DreamDustAutomation.UI.MachineDragVisual.Watch(item);
                item.fakeMiddle.SetParent(RenderHandler.current.overlayRoot,true);
                item.SetCenteredPosition(300,200);
                Check(Near(item.fakeRotator.rectTransform.TransformPoint(item.fakeRotator.rectTransform.rect.center),new Vector3(300,200,0)),"Drag hit center mismatch");
                raw.Expel(item);item.modifiedShape.minX=(move%7);item.modifiedShape.minY=scroll+move%5;
                raw.UncheckedAccept(item);
                Check(Near(Center(item),Expected(raw,item)),"Stored sprite mismatch after repeated drag at scroll "+scroll);
                Check(item.handler.rectTransform.localScale==Vector3.one && item.fakeMiddle.rectTransform.localScale==Vector3.one,"Scale accumulated");
            }
            raw.Expel(item);waste.UncheckedAccept(item);
            Check(Near(Center(item),Expected(waste,item)),"Cross-bin sprite mismatch");
            waste.Expel(item);backpack.UncheckedAccept(item);
            Check(Near(Center(item),Expected(backpack,item)),"Outgoing vanilla sprite mismatch");
            Check(object.ReferenceEquals(shape,item.modifiedShape) && shape.orientation==rotation && shape.flipped==flip,"Shape/rotation mutated");
        }
        // Insertion does not touch older items or install a component per bottle.
        var output=Bin(.65f,152,true);var stock=new List<GameItemElement>();
        for(int n=0;n<1700;n++){var item=Item(1,2,n%16,n/16,0,false);stock.Add(item);output.UncheckedAccept(item);}
        foreach(var item in stock)Check(item.ValidateCalls==1 && item.fakeMiddle.gameObject.ComponentCount==0,"Insertion caused a full rescan/component per stored product");
        foreach(var item in stock)item.handler.rectTransform.localScale=new Vector3(.5f,.5f,1);
        DreamDustAutomation.UI.MachineInventoryVisuals.Restore(output);
        foreach(var item in stock)Check(item.ValidateCalls==1 && item.handler.rectTransform.localScale==Vector3.one,"Restore revalidated products instead of restoring scale only");
        Console.WriteLine(checks+" native inventory transfer geometry checks passed (rendering doubles; no live Unity UI)");
    }
}
