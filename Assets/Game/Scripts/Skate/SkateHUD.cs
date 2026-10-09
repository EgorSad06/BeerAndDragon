using System.Text;
using UnityEngine;
using UnityEngine.UI;

// HUD скейта в стиле THPS: счёт, текущее комбо по центру, шкала баланса,
// подсказки, "+1200!" при приземлении и "БЭЙЛ!" при падении.
// Создаётся сам из SkaterController, ничего настраивать не надо.
public class SkateHUD : MonoBehaviour
{
    public SkaterController skater;
    public float helpDuration = 10f;

    private Font font;
    private GameObject rideRoot;
    private Text prompt, score, combo, comboValue, live, popup, help;
    private RectTransform balanceRoot, balanceNeedle;
    private float popupTime = -10f;
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
        }
    }

    void OnDestroy()
    {
        if (skater == null) return;
        skater.ComboLanded -= OnComboLanded;
        skater.BailedEvent -= OnBailed;
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
        help = NewText(rr, 20, new Color(1f, 1f, 1f, 0.8f), new Vector2(0.5f, 0f), new Vector2(0f, 150f));
        help.text = "W -- толчок   S -- тормоз   A/D -- поворот / спин в воздухе   Space -- олли (держи = выше)\n" +
                    "ЛКМ + направление -- флип   ПКМ + направление -- грэб   E у перил -- грайнд   Ctrl -- мэньюал   E -- слезть";

        popup = NewText(root, 64, new Color(1f, 0.85f, 0.3f), new Vector2(0.5f, 0.55f), Vector2.zero);
        popup.fontStyle = FontStyle.Bold;

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

        help.gameObject.SetActive(skater.RideTime < helpDuration);
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
