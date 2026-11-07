using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    internal sealed class PlannedInputHighlights : MonoBehaviour
    {
        private readonly Dictionary<GameItemElement, TreeNodeRender[]> frames = new Dictionary<GameItemElement, TreeNodeRender[]>();
        internal void Show(IEnumerable<GameItem> items)
        {
            if (!gameObject.activeInHierarchy) { Clear(); return; }
            var wanted = new HashSet<GameItemElement>(items.OfType<GameItemElement>().Where(item => !item.IsDestroyed()));
            foreach (var item in frames.Keys.Where(item => !wanted.Contains(item)).ToArray()) Remove(item);
            foreach (var item in wanted)
            {
                if (frames.ContainsKey(item)) continue;
                var edges = new TreeNodeRender[4];
                for (int i = 0; i < edges.Length; i++)
                {
                    // Follow the actual item rectangle through rotation/sort and
                    // native scroll masking. Never modify sprites or selection.
                    edges[i] = TreeNodeRender.Instantiate().SetParent(item.middle);
                    edges[i].gameObject.name = "DDA Planned input";
                    edges[i].image.sprite = null;
                    edges[i].image.raycastTarget = false;
                    edges[i].image.color = new Color32(79, 230, 119, 255);
                    edges[i].image.enabled = true;
                    edges[i].rectTransform.SetAsLastSibling();
                }
                float width = 2 * Mathf.Max(1, UISettings.current.gridSize / 16);
                Place(edges[0], new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1), new Vector2(0, width));
                Place(edges[1], new Vector2(0, 0), new Vector2(1, 0), new Vector2(.5f, 0), new Vector2(0, width));
                Place(edges[2], new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, .5f), new Vector2(width, -2 * width));
                Place(edges[3], new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, .5f), new Vector2(width, -2 * width));
                frames[item] = edges;
            }
        }
        private static void Place(TreeNodeRender node, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            node.rectTransform.anchorMin = min; node.rectTransform.anchorMax = max;
            node.rectTransform.pivot = pivot; node.rectTransform.sizeDelta = size;
            node.rectTransform.anchoredPosition = Vector2.zero;
        }
        private void Remove(GameItemElement item)
        {
            foreach (var node in frames[item])
                if (node != null && node.gameObject != null)
                {
                    node.image.enabled = false;
                    Object.Destroy(node.gameObject);
                }
            frames.Remove(item);
        }
        internal void Clear() { foreach (var item in frames.Keys.ToArray()) Remove(item); }
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
