using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Наведение на кнопку меню -- «левитация», как у карточек в Steam:
//  - карточка (card) приподнимается над своей тенью и чуть увеличивается, тень проступает снизу;
//  - пока курсор на ней -- медленно покачивается вверх-вниз;
//  - наклоняется в перспективе: край под курсором уходит вглубь (MenuCardTilt на всей графике карточки);
//  - надпись меняет цвет, при нажатии карточка проседает.
// Вешается на тень: она стоит на месте и ловит мышь, поэтому карточка не дёргается, когда уезжает из-под курсора.
public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform card;
    public Image shadow;
    public Text label;
    public Color normal, hover;

    [Header("Левитация")]
    public float lift = 14f;               // на сколько поднимается, px
    public float hoverScale = 1.06f;
    public float bob = 4f;                 // покачивание, px
    public float bobSpeed = 2.4f;
    public float maxTilt = 0.22f;          // наклон к курсору: глубина на пиксель от центра (0.22 ~ 12 градусов)
    public float shadowAlpha = 0.45f;
    public float speed = 8f;

    private bool over, down;
    private float k;
    private Vector2 tilt;
    private MenuCardTilt[] tilters;

    public void OnPointerEnter(PointerEventData e) => over = true;
    public void OnPointerExit(PointerEventData e) { over = false; down = false; }
    public void OnPointerDown(PointerEventData e) => down = true;
    public void OnPointerUp(PointerEventData e) => down = false;

    void Start()
    {
        if (card == null) return;
        Graphic[] graphics = card.GetComponentsInChildren<Graphic>(true);
        tilters = new MenuCardTilt[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
        {
            tilters[i] = graphics[i].gameObject.AddComponent<MenuCardTilt>();
            tilters[i].space = card;
        }
    }

    void OnDisable()
    {
        over = down = false;
        k = 0f;
        tilt = Vector2.zero;
        Apply();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        k = Mathf.MoveTowards(k, over ? 1f : 0f, dt * speed * 0.6f);

        // Курсор в координатах тени: -1..1 от центра
        Vector2 target = Vector2.zero;
        RectTransform rt = (RectTransform)transform;
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (over && RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, cam, out Vector2 local))
        {
            Vector2 half = rt.rect.size * 0.5f;
            target = new Vector2(Mathf.Clamp(local.x / Mathf.Max(1f, half.x), -1f, 1f), Mathf.Clamp(local.y / Mathf.Max(1f, half.y), -1f, 1f));
        }
        tilt = Vector2.Lerp(tilt, target, 1f - Mathf.Exp(-dt * speed));
        Apply();
    }

    private void Apply()
    {
        float e = k * k * (3f - 2f * k);
        if (card != null)
        {
            float s = Mathf.Lerp(1f, hoverScale, e) * (down ? 0.97f : 1f);
            float y = (lift + Mathf.Sin(Time.unscaledTime * bobSpeed) * bob) * e * (down ? 0.4f : 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.localScale = new Vector3(s, s, 1f);
        }
        if (shadow != null) shadow.color = new Color(0f, 0f, 0f, shadowAlpha * e);
        if (tilters != null)
        {
            Vector2 t = tilt * (maxTilt * e);
            foreach (MenuCardTilt m in tilters)
                if (m != null) m.Set(t);
        }
        if (label != null) label.color = Color.Lerp(normal, hover, e);
    }
}
