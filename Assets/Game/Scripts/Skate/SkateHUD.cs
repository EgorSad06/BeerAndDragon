using System.Text;
using UnityEngine;
using UnityEngine.UI;

// HUD скейта в стиле THPS: счёт, спец-шкала, текущее комбо по центру, шкала баланса,
// подсказки, "+1200!" при приземлении, PERFECT/SKETCHY и "БЭЙЛ!" при падении.
// Создаётся сам из SkaterController, ничего настраивать не надо.
// Спец-шкала берёт пиксельные спрайты из GameHUD (если назначены).
public class SkateHUD : MonoBehaviour
{
    public SkaterController skater;
    public float helpDuration = 10f;

    private Font font;
    private GameObject rideRoot;
    private Text prompt, score, combo, comboValue, live, popup, help, rating, specialLabel;
    private RectTransform balanceRoot, balanceNeedle;
    private RectTransform specialRoot;
    private Image specialFillImage;
    private Color specialBaseColor = Color.white;
    private float popupTime = -10f;
    private float ratingTime = -10f;
    private int lastScore = -1;
    private int lastComboCount = -1, lastComboBase = -1;
    private readonly StringBuilder sb = new StringBuilder();

    void Start()
    {
        if (skater == null) skater = FindFirstObjectByType<SkaterController>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();

        if (skater != null)
        {
            skater.ComboLanded += OnComboLanded;
            skater.BailedEvent += OnBailed;
            skater.LandingRated += OnLandingRated;
            skater.SpecialReady += OnSpecialReady;
        }
    }

    void OnDestroy()
    {
        if (skater == null) return;
        skater.ComboLanded -= OnComboLanded;
        skater.BailedEvent -= OnBailed;
        skater.LandingRated -= OnLandingRated;
        skater.SpecialReady -= OnSpecialReady;
    }

    private void Build()
    {
        GameObject go = new GameObject("SkateCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)go.transform;

        prompt = NewText(root, 30, new Color(1f, 0.95f, 0.7f), new Vector2(0.5f, 0.3f), new Vector2(0f, 0f));

        rideRoot = new GameObject("Ride", typeof(RectTransform));
        rideRoot.transform.SetParent(root, false);
        RectTransform rr = (RectTransform)rideRoot.transform;
        rr.anchorMin = Vector2.zero;
        rr.anchorMax = Vector2.one;
        rr.offsetMin = rr.offsetMax = Vector2.zero;

        score = NewText(rr, 34, new Color(1f, 0.85f, 0.3f), new Vector2(1f, 1f), new Vector2(-220f, -130f));
        combo = NewText(rr, 28, Color.white, new Vector2(0.5f, 0.32f), Vector2.zero);
        comboValue = NewText(rr, 40, new Color(1f, 0.85f, 0.3f), new Vector2(0.5f, 0.32f), new Vector2(0f, -44f));
        live = NewText(rr, 30, new Color(0.6f, 0.95f, 1f), new Vector2(0.5f, 0.32f), new Vector2(0f, 46f));
        help = NewText(rr, 20, new Color(1f, 1f, 1f, 0.8f), new Vector2(0.5f, 0f), new Vector2(0f, 170f));
        help.text = "W -- толчок   S -- тормоз   A/D -- поворот / спин в воздухе (зажми на олли -- раскрутка)   Space -- олли (держи = выше)\n" +
                    "ЛКМ + направление -- флип   ПКМ + направление -- грэб   E у перил -- грайнд   Ctrl -- мэньюал   E -- слезть\n" +
                    "Шкала СПЕЦ полная: ПКМ + ЛКМ + направление -- спец-трюк";

        popup = NewText(root, 64, new Color(1f, 0.85f, 0.3f), new Vector2(0.5f, 0.55f), Vector2.zero);
        popup.fontStyle = FontStyle.Bold;

        rating = NewText(rr, 44, Color.white, new Vector2(0.5f, 0.64f), Vector2.zero);
        rating.fontStyle = FontStyle.Bold;

        BuildSpecial(rr);

        // Шкала баланса
        GameObject b = new GameObject("Balance", typeof(RectTransform), typeof(Image));
        b.transform.SetParent(rr, false);
        balanceRoot = (RectTransform)b.transform;
        balanceRoot.anchorMin = balanceRoot.anchorMax = new Vector2(0.5f, 0.42f);
        balanceRoot.sizeDelta = new Vector2(360f, 14f);
        b.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        b.GetComponent<Image>().raycastTarget = false;

        foreach (float x in new[] { -1f, 1f })
        {
            GameObject danger = new GameObject("Danger", typeof(RectTransform), typeof(Image));
            danger.transform.SetParent(balanceRoot, false);
            RectTransform drt = (RectTransform)danger.transform;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f + x * 0.42f, 0.5f);
            drt.sizeDelta = new Vector2(360f * 0.16f, 14f);
            Image di = danger.GetComponent<Image>();
            di.color = new Color(0.8f, 0.1f, 0.1f, 0.8f);
            di.raycastTarget = false;
        }

        GameObject needle = new GameObject("Needle", typeof(RectTransform), typeof(Image));
        needle.transform.SetParent(balanceRoot, false);
        balanceNeedle = (RectTransform)needle.transform;
        balanceNeedle.anchorMin = balanceNeedle.anchorMax = new Vector2(0.5f, 0.5f);
        balanceNeedle.sizeDelta = new Vector2(8f, 28f);
        needle.GetComponent<Image>().color = Color.white;
        needle.GetComponent<Image>().raycastTarget = false;
    }

    // Спец-шкала под счётом: пиксельная рамка + полоска (или простая полоска без спрайтов)
    private void BuildSpecial(RectTransform parent)
    {
        GameHUD hud = FindFirstObjectByType<GameHUD>();
        bool pixel = hud != null && hud.smallFrame != null && hud.smallEmpty != null && hud.specialFill != null;
        float k = hud != null ? hud.pixelScale : 7f;
        Vector2 size = pixel ? new Vector2(42f, 11f) * k : new Vector2(300f, 22f);

        GameObject go = new GameObject("Special", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        specialRoot = (RectTransform)go.transform;
        specialRoot.anchorMin = specialRoot.anchorMax = new Vector2(1f, 1f);
        specialRoot.pivot = new Vector2(1f, 1f);
        specialRoot.anchoredPosition = new Vector2(-40f, -175f);
        specialRoot.sizeDelta = size;

        if (pixel)
        {
            NewImage("Frame", specialRoot, hud.smallFrame, Vector2.zero, size);
            Vector2 pos = new Vector2(4f, 3f) * k, barSize = new Vector2(34f, 5f) * k;
            NewImage("Empty", specialRoot, hud.smallEmpty, pos, barSize);
            specialFillImage = NewImage("Fill", specialRoot, hud.specialFill, pos, barSize);
        }
        else
        {
            NewImage("Back", specialRoot, null, Vector2.zero, size).color = new Color(0f, 0f, 0f, 0.6f);
            specialFillImage = NewImage("Fill", specialRoot, null, Vector2.zero, size);
            specialFillImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            specialBaseColor = new Color(1f, 0.8f, 0.2f);
        }
        specialFillImage.type = Image.Type.Filled;
        specialFillImage.fillMethod = Image.FillMethod.Horizontal;
        specialFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        specialFillImage.color = specialBaseColor;

        specialLabel = NewText(specialRoot, 24, new Color(1f, 0.85f, 0.3f), new Vector2(0f, 0.5f), new Vector2(-55f, 0f));
        specialLabel.rectTransform.sizeDelta = new Vector2(120f, 40f);
        specialLabel.text = "СПЕЦ";
    }

    void Update()
    {
        if (skater == null) return;

        bool riding = skater.IsRiding;
        if (rideRoot.activeSelf != riding) rideRoot.SetActive(riding);

        prompt.text = !riding && skater.NearbyBoard != null && !PlayerInputLock.Locked ? "[E]  Встать на скейт" : "";

        // Попап живёт 1.5 сек
        float age = Time.time - popupTime;
        Color pc = popup.color;
        pc.a = age < 1.5f ? Mathf.Clamp01((1.5f - age) / 0.4f) : 0f;
        popup.color = pc;
        popup.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.25f - age) * 1.5f);

        // PERFECT / SKETCHY живёт 1 сек
        float ra = Time.time - ratingTime;
        Color rc = rating.color;
        rc.a = ra < 1f ? Mathf.Clamp01((1f - ra) / 0.3f) : 0f;
        rating.color = rc;
        rating.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.2f - ra) * 2f);

        if (!riding) return;

        if (skater.TotalScore != lastScore)
        {
            lastScore = skater.TotalScore;
            score.text = "СЧЁТ  " + lastScore.ToString("N0");
        }

        if (skater.ComboTricks.Count != lastComboCount || skater.ComboBase != lastComboBase)
        {
            lastComboCount = skater.ComboTricks.Count;
            lastComboBase = skater.ComboBase;
            if (lastComboCount == 0)
            {
                combo.text = "";
                comboValue.text = "";
            }
            else
            {
                sb.Clear();
                int from = Mathf.Max(0, lastComboCount - 4);
                if (from > 0) sb.Append("... + ");
                for (int i = from; i < lastComboCount; i++)
                {
                    if (i > from) sb.Append(" + ");
                    sb.Append(skater.ComboTricks[i]);
                }
                combo.text = sb.ToString();
                comboValue.text = $"{skater.ComboBase:N0}  x  {skater.ComboMultiplier}";
            }
        }

        live.text = skater.LiveTrick ?? "";

        bool showBalance = skater.BalanceActive;
        if (balanceRoot.gameObject.activeSelf != showBalance) balanceRoot.gameObject.SetActive(showBalance);
        if (showBalance)
            balanceNeedle.anchoredPosition = new Vector2(Mathf.Clamp(skater.Balance, -1f, 1f) * 175f, 0f);

        UpdateSpecial();
        help.gameObject.SetActive(skater.RideTime < helpDuration);
    }

    // Полная шкала мигает и пульсирует, как в THPS
    private void UpdateSpecial()
    {
        if (specialFillImage == null) return;
        specialFillImage.fillAmount = skater.Special;
        if (skater.SpecialActive)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f);
            specialFillImage.color = Color.Lerp(specialBaseColor, new Color(1f, 0.35f, 0.2f), pulse);
            specialLabel.color = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.2f), pulse);
            specialRoot.localScale = Vector3.one * (1f + 0.04f * pulse);
        }
        else
        {
            specialFillImage.color = specialBaseColor;
            specialLabel.color = new Color(1f, 0.85f, 0.3f);
            specialRoot.localScale = Vector3.one;
        }
    }

    private void OnComboLanded(int value)
    {
        popup.text = "+" + value.ToString("N0") + "!";
        popup.color = new Color(1f, 0.85f, 0.3f);
        popupTime = Time.time;
    }

    private void OnBailed(string reason)
    {
        popup.text = "БЭЙЛ!\n<size=30>" + reason + "</size>";
        popup.color = new Color(1f, 0.25f, 0.2f);
        popupTime = Time.time;
        lastComboCount = -1;
    }

    private void OnLandingRated(string value)
    {
        bool perfect = value == "PERFECT";
        rating.text = perfect ? "PERFECT!" : "SKETCHY...";
        rating.color = perfect ? new Color(0.45f, 1f, 0.35f) : new Color(1f, 0.6f, 0.2f);
        ratingTime = Time.time;
    }

    private void OnSpecialReady()
    {
        rating.text = "СПЕЦ ГОТОВ!";
        rating.color = new Color(1f, 0.85f, 0.2f);
        ratingTime = Time.time;
    }

    private Image NewImage(string name, Transform parent, Sprite sprite, Vector2 bottomLeft, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = bottomLeft;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(Transform parent, int size, Color color, Vector2 anchor, Vector2 pos)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1400f, 60f);
        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = true;
        go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        return t;
    }
}
