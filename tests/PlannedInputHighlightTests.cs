using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DreamDustAutomation.UI;
namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject o) { o.destroyed = true; } }
    public class GameObject { public bool activeInHierarchy = true, destroyed; public string name; }
    public class MonoBehaviour { public GameObject gameObject = new GameObject(); }
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 zero => new Vector2(0,0); }
    public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public class RectTransform { public Vector2 anchorMin,anchorMax,pivot,sizeDelta,anchoredPosition; public void SetAsLastSibling(){} }
    public static class Mathf { public static int Max(int a,int b)=>Math.Max(a,b); }
}
public class Image { public object sprite; public bool enabled,raycastTarget; public UnityEngine.Color32 color; }
public class TreeNodeRender
{
    public static List<TreeNodeRender> created = new List<TreeNodeRender>();
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
    public Image image = new Image(); public TreeNodeRender parent;
    public static TreeNodeRender Instantiate() { var n=new TreeNodeRender();created.Add(n);return n; }
    public TreeNodeRender SetParent(TreeNodeRender p){parent=p;return this;}
}
public class GameItem {}
public class GameItemElement : GameItem { public TreeNodeRender middle=new TreeNodeRender();public bool destroyed;public bool IsDestroyed()=>destroyed; }
public class UISettings { public static UISettings current=new UISettings();public int gridSize=16; }
internal static class PlannedInputHighlightTests
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main()
    {
        var view=new PlannedInputHighlights();var a=new GameItemElement();var b=new GameItemElement();
        view.Show(new GameItem[]{a,a,new GameItem()});
        Check(TreeNodeRender.created.Count==4,"duplicate source created duplicate frames");
        var first=TreeNodeRender.created.ToArray();
        Check(first.All(n=>n.parent==a.middle&&n.image.enabled&&!n.image.raycastTarget&&n.image.sprite==null),"overlay changes hit testing or parent");
        Check(first.All(n=>n.image.color.g>n.image.color.r&&n.image.color.a==255),"frame is not opaque green");
        Check(first[0].rectTransform.anchorMin.y==1&&first[0].rectTransform.anchorMax.x==1&&first[0].rectTransform.sizeDelta.y==2,"top edge geometry");
        Check(first[3].rectTransform.anchorMax.y==1&&first[3].rectTransform.sizeDelta.x==2,"side edge geometry");
        for(int i=0;i<100;i++){view.Show(new GameItem[]{a});Check(TreeNodeRender.created.Count==4&&first.All(n=>n.image.enabled&&!n.gameObject.destroyed),"unchanged preview rebuilt or flashed");}
        a.middle.rectTransform.sizeDelta=new UnityEngine.Vector2(80,16);
        view.Show(new GameItem[]{a,b});Check(TreeNodeRender.created.Count==8&&first.All(n=>!n.gameObject.destroyed),"adding source rebuilt existing frame");
        view.Show(new GameItem[]{b});Check(first.All(n=>n.gameObject.destroyed&&!n.image.enabled),"old quantity selection stayed highlighted");
        var second=TreeNodeRender.created.Skip(4).ToArray();
        typeof(PlannedInputHighlights).GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,null);
        Check(second.All(n=>n.gameObject.destroyed&&!n.image.enabled),"closed window kept frames");
        view.Show(new GameItem[]{a});Check(TreeNodeRender.created.Count==12,"reopen did not recreate selected frame");
        a.destroyed=true;view.Show(new GameItem[]{a});Check(TreeNodeRender.created.Skip(8).All(n=>n.gameObject.destroyed),"destroyed source kept frame");
        view.gameObject.activeInHierarchy=false;view.Show(new GameItem[]{b});Check(TreeNodeRender.created.Count==12,"hidden window created frames");
        view.Clear();view.Clear();Check(TreeNodeRender.created.All(n=>n.gameObject.destroyed),"clear is not idempotent");
        Console.WriteLine("Planned input highlights: "+checks+" checks passed (diff-only updates, frames, resizing, removals and window lifecycle; rendering mocked).");
    }
}
