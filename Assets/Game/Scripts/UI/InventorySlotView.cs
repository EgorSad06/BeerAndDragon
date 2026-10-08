using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Один слот инвентаря на экране. Создаётся GameHUD, вся логика -- там.
public class InventorySlotView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    private GameHUD hud;
    private int index;
    private Image highlight;
    private Image icon;
    private Text countText;
    private Text nameFallback;   // если у предмета нет иконки -- пишем имя

    public void Init(GameHUD owner, int slotIndex, Image iconImage, Text count, Image highlightFrame)
    {
        hud = owner;
        index = slotIndex;
        highlight = highlightFrame;
        icon = iconImage;
        countText = count;

        GameObject go = new GameObject("Name", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);
        nameFallback = go.AddComponent<Text>();
        nameFallback.font = count.font;
        nameFallback.fontSize = 14;
        nameFallback.alignment = TextAnchor.MiddleCenter;
        nameFallback.color = Color.white;
        nameFallback.raycastTarget = false;
    }

    // highlightColor с альфой 0 -- без подсветки
    public void Show(Sprite sprite, string count, Color highlightColor, string itemName)
    {
        highlight.color = highlightColor;
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        countText.text = count;
        nameFallback.text = sprite == null ? itemName : "";
    }

    public void OnPointerClick(PointerEventData e) => hud.SlotClicked(index, e.button);
    public void OnBeginDrag(PointerEventData e) => hud.SlotBeginDrag(index, e);
    public void OnDrag(PointerEventData e) => hud.SlotDrag(e);
    public void OnEndDrag(PointerEventData e) => hud.SlotEndDrag();
    public void OnDrop(PointerEventData e) => hud.SlotDrop(index);
    public void OnPointerEnter(PointerEventData e) => hud.SlotHover(index, true);
    public void OnPointerExit(PointerEventData e) => hud.SlotHover(index, false);
}
