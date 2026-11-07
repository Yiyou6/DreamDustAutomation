using System;
using System.Collections.Generic;
using System.Reflection;
using DreamDustAutomation.UI;
using UnityEngine;

namespace UnityEngine {
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 zero=>new Vector2(); public static implicit operator Vector3(Vector2 v)=>new Vector3(v.x,v.y,0); }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 one=>new Vector3(1,1,1); public static Vector3 forward=>new Vector3(0,0,1);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(float a,Vector3 b)=>new Vector3(a*b.x,a*b.y,a*b.z);
        public static bool operator ==(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)<1e-5 && Math.Abs(a.y-b.y)<1e-5 && Math.Abs(a.z-b.z)<1e-5;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b); public override bool Equals(object b)=>b is Vector3 v && this==v; public override int GetHashCode()=>x.GetHashCode();
    }
    public class MonoBehaviour { }
    public class GameObject {
        private readonly Dictionary<Type,object> components=new Dictionary<Type,object>();
        public int ComponentCount=>components.Count;
        public T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var c)?c as T:null;
        public T AddComponent<T>() where T:new(){var c=new T();components.Add(typeof(T),c);return c;}
        public void ParentChanged(){foreach(var c in components.Values)c.GetType().GetMethod("OnTransformParentChanged",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(c,null);}
    }
    public class Rect { public Vector2 center; }
    public class RectTransform {
        public RectTransform parent;
        public GameObject gameObject;
        public Vector2 sizeDelta,anchoredPosition,anchorMin,anchorMax,pivot=new Vector2(0,1);
        public Vector3 localScale=Vector3.one,localEulerAngles;
        public Rect rect=>new Rect{center=new Vector2((.5f-pivot.x)*sizeDelta.x,(.5f-pivot.y)*sizeDelta.y)};
        public Vector3 TransformPoint(Vector3 p){ double a=localEulerAngles.z*Math.PI/180; double x=p.x*localScale.x,y=p.y*localScale.y;
            var v=new Vector3((float)(x*Math.Cos(a)-y*Math.Sin(a))+anchoredPosition.x,(float)(x*Math.Sin(a)+y*Math.Cos(a))+anchoredPosition.y,p.z);
            return parent==null?v:parent.TransformPoint(v); }
        public Vector3 InverseTransformPoint(Vector3 p){ if(parent!=null)p=parent.InverseTransformPoint(p); p=p-(Vector3)anchoredPosition;
            double a=-localEulerAngles.z*Math.PI/180; return new Vector3((float)(p.x*Math.Cos(a)-p.y*Math.Sin(a))/localScale.x,(float)(p.x*Math.Sin(a)+p.y*Math.Cos(a))/localScale.y,p.z); }
        public Vector3 position { get=>TransformPoint(new Vector3()); set{var p=parent==null?value:parent.InverseTransformPoint(value);anchoredPosition=new Vector2(p.x,p.y);} }
        public Vector3 localPosition { get=>(Vector3)anchoredPosition; set{anchoredPosition=new Vector2(value.x,value.y);} }
        public Vector3 lossyScale { get {var p=parent==null?Vector3.one:parent.lossyScale;return new Vector3(p.x*localScale.x,p.y*localScale.y,p.z*localScale.z);} }
        public void SetParent(RectTransform next,bool worldPositionStays=true){
            var p=position;var s=lossyScale;parent=next;
            if(worldPositionStays){position=p;var scale=parent==null?Vector3.one:parent.lossyScale;localScale=new Vector3(s.x/scale.x,s.y/scale.y,s.z/scale.z);}
            gameObject?.ParentChanged();
        }
    }
}
public class TreeNodeRender {
    public RectTransform rectTransform=new RectTransform(); public GameObject gameObject=new GameObject(); public TreeNodeRender parent;
    public TreeNodeRender(){rectTransform.gameObject=gameObject;}
    public TreeNodeRender SetParent(TreeNodeRender next)=>SetParent(next,false);
    // NATIVE_TreeSetParent
    // NATIVE_ResetParent
}
public class RenderHandler { public static RenderHandler current=new RenderHandler(); public TreeNodeRender overlayRoot=new TreeNodeRender(),tempRoot=new TreeNodeRender(); }
public class UISettings { public static UISettings current=new UISettings(); public int gridSize=16; }
public class Shape { public int orientation,minX,minY; public bool flipped; }
public class GameItem {
    public Shape modifiedShape=new Shape(); public GameInventory parentInventory;
    public Action<GameItem,GameInventory,SlotMarker> onSlotAddItemFunc,onSlotRemoveItemFunc;
    public void AddParent(GameInventory inv){parentInventory=inv;} public void RemoveParent(GameInventory inv){parentInventory=null;}
    public void ValidateShapeState(){} public virtual void Validate(){}
}
public class GameItemElement:GameItem,PixelElement {
    public Shape tempShape;
    public TreeNodeRender handler{get;}=new TreeNodeRender(); public TreeNodeRender inner{get;}=new TreeNodeRender();
    public TreeNodeRender middle=new TreeNodeRender(),rotator=new TreeNodeRender(),fakeMiddle=new TreeNodeRender(),fakeRotator=new TreeNodeRender();
    public int ValidateCalls;
    public GameItemElement(){middle.SetParent(handler);rotator.SetParent(middle);fakeMiddle.SetParent(handler);fakeRotator.SetParent(fakeMiddle);}
    // Rendering-only validation double; native geometry/reparent bodies below.
    public override void Validate(){ValidateCalls++;handler.rectTransform.anchoredPosition=Vector2.zero;fakeMiddle.SetParent(handler,true);UpdateRectTransform();}
    // NATIVE_UpdateRectTransform
    // NATIVE_GetCenteredPosition
    // NATIVE_SetCenteredPosition
}
public class ItemMouseDragHandler { public static ItemMouseDragHandler current; public bool IsDraggingItem; public GameItem GetCurrentItem()=>null; }
public static class DragVisualTests {
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static bool Near(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)<.001 && Math.Abs(a.y-b.y)<.001;
    static Vector3 Center(GameItemElement i)=>i.fakeRotator.rectTransform.TransformPoint(i.fakeRotator.rectTransform.rect.center);
    public static void Main(){
        foreach(float scale in new[]{.4f,.65f,.85f,1f,1.5f}) foreach(int rotation in new[]{0,1,2,3}) foreach(bool flip in new[]{false,true}) {
            var i=new GameItemElement();i.tempShape=i.modifiedShape;
            i.modifiedShape.orientation=rotation;i.modifiedShape.flipped=flip;i.modifiedShape.minX=7;i.modifiedShape.minY=4;
            i.fakeMiddle.rectTransform.sizeDelta=new Vector2(32,48);i.fakeRotator.rectTransform.sizeDelta=new Vector2(32,48);
            i.fakeRotator.rectTransform.parent=i.fakeMiddle.rectTransform;
            i.fakeMiddle.rectTransform.parent=RenderHandler.current.overlayRoot.rectTransform;
            i.fakeMiddle.rectTransform.localScale=new Vector3(scale,scale,1);
            i.UpdateRectTransform(); // Same operation used by sorting.
            i.SetCenteredPosition(300,200);
            if(scale!=1)Check(!Near(Center(i),new Vector3(300,200,0)),"Fixture did not reproduce native scaled-ghost offset");
            var center=Center(i);var fix=new MachineDragVisual{item=i};fix.Normalize();
            Check(Near(center,Center(i)),"Normalization moved the visible ghost center");
            i.SetCenteredPosition(300,200);
            Check(Near(Center(i),new Vector3(300,200,0)),"Native centering still offsets the sprite after sorting/rotation");
            Check(i.modifiedShape.minX==7 && i.modifiedShape.minY==4 && i.modifiedShape.orientation==rotation && i.modifiedShape.flipped==flip,"Inventory geometry changed");
            fix.Normalize();Check(Near(Center(i),new Vector3(300,200,0)),"Repeat normalization drift");
            // Return to a differently scaled item handler, then drag again.
            i.fakeMiddle.rectTransform.parent=i.handler.rectTransform;i.fakeMiddle.rectTransform.localScale=new Vector3(1/scale,1/scale,1);
            fix.Normalize();Check(i.fakeMiddle.rectTransform.localScale==Vector3.one,"Ghost scale leaked back into inventory");
            i.UpdateRectTransform();i.fakeMiddle.rectTransform.parent=RenderHandler.current.overlayRoot.rectTransform;
            i.fakeMiddle.rectTransform.localScale=new Vector3(scale,scale,1);fix.Normalize();i.SetCenteredPosition(400,230);
            Check(Near(Center(i),new Vector3(400,230,0)),"Second drag offset");
        }
        Console.WriteLine(checks+" native drag geometry checks passed (rendering doubles; no live Unity UI)");
        InventoryVisualTests.Run();
    }
}
