using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Экран смерти. Строит GameHUD при старте, до смерти он спрятан. Когда игрок умирает:
//  - камера падает на землю (DeathCamera), весь остальной UI (патроны, портрет, хотбар...) гаснет;
//  - экран темнеет, по углам наползает серая могильная дымка (DeathFog: кадр из гифки, плывёт двумя слоями);
//  - из дымки сверху вниз проявляется карта таро «Смерть» (DeathCard);
//  - под ней меню: «Ещё раз» -- перезапуск сцены; «Настройки» и «В меню» -- пока заглушки.
// Картинки -- Resources/UI/Death (их готовит tools/ui/death_assets.py), там же шейдеры.
public class DeathScreen : MonoBehaviour
{
    [Header("Тайминг, секунды от смерти")]
    public float hudFadeTime = 0.6f;
    public float darkenTime = 2.6f;
    public float fogStart = 0.3f, fogTime = 3f;
    public float cardStart = 2.2f, cardTime = 3.2f;
    public float menuStart = 5.2f, menuFadeTime = 0.6f;

    [Header("Вид")]
    [Range(0f, 1f)] public float darkness = 0.96f;
    public float cardHeight = 640f;
    public Color textColor = new Color(0.86f, 0.83f, 0.75f);
    public Color hoverColor = new Color(0.9f, 0.22f, 0.16f);

    private const string Folder = "UI/Death/";

    private PlayerHealth health;
    private Canvas canvas;
    private Font font;
    private GameObject content;
    private Image dark;
    private RawImage card;
    private Material fogMat, cardMat;
    private RectTransform cardRect;
    private CanvasGroup menu;
    private Text note;
    private float noteTime = -10f;
    private float deathTime = -1f;
    private bool menuShown;
    private readonly List<CanvasGroup> hiddenUI = new List<CanvasGroup>();

    public bool IsShown => deathTime >= 0f;

    public static DeathScreen Create(Transform parent, PlayerHealth health)
    {
        GameObject go = new GameObject("DeathScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        DeathScreen ds = go.AddComponent<DeathScreen>();
        ds.health = health;
        ds.Build();
        if (health != null) health.Died += ds.Show;
        return ds;
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= Show;
        if (fogMat != null) Destroy(fogMat);
        if (cardMat != null) Destroy(cardMat);
    }

    // ---------------- Build ----------------

    private void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;                       // поверх HUD (100) и скейта
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        content = NewRect("Content", transform).gameObject;
        Stretch((RectTransform)content.transform);

        dark = NewGraphic<Image>("Dark", content.transform);
        dark.color = new Color(0f, 0f, 0f, 0f);
        Stretch(dark.rectTransform);

        Texture2D fogTex = Resources.Load<Texture2D>(Folder + "DeathFog");
        Shader fogShader = Resources.Load<Shader>(Folder + "DeathFog");
        RawImage fog = NewGraphic<RawImage>("Fog", content.transform);
        fog.texture = fogTex;
        if (fogShader != null) fog.material = fogMat = new Material(fogShader);
        Stretch(fog.rectTransform);

        Texture2D cardTex = Resources.Load<Texture2D>(Folder + "DeathCard");
        Shader cardShader = Resources.Load<Shader>(Folder + "DeathCard");
        card = NewGraphic<RawImage>("Card", content.transform);
        card.texture = cardTex;
        if (cardShader != null)
        {
            card.material = cardMat = new Material(cardShader);
            cardMat.SetTexture("_FogTex", fogTex);
        }
        float aspect = cardTex != null ? cardTex.width / (float)cardTex.height : 0.59f;
        cardRect = card.rectTransform;
        Place(cardRect, new Vector2(0f, 70f), new Vector2(cardHeight * aspect, cardHeight));

        // Меню под картой
        RectTransform m = NewRect("Menu", content.transform);
        Place(m, new Vector2(0f, 70f - cardHeight * 0.5f - 80f), new Vector2(1100f, 70f));
        menu = m.gameObject.AddComponent<CanvasGroup>();
        AddButton(m, "ЕЩЁ РАЗ", -360f, Retry);
        AddButton(m, "НАСТРОЙКИ", 0f, () => ShowNote("Настройки пока не готовы"));
        AddButton(m, "В МЕНЮ", 360f, () => ShowNote("Главного меню пока нет"));

        note = NewText("Note", content.transform, 24, new Color(textColor.r, textColor.g, textColor.b, 0f));
        Place(note.rectTransform, new Vector2(0f, 70f - cardHeight * 0.5f - 140f), new Vector2(800f, 30f));

        SetMenuVisible(0f);
        content.SetActive(false);
    }

    private void AddButton(RectTransform parent, string label, float x, UnityAction onClick)
    {
        Text t = NewText(label, parent, 42, Color.white);
        t.text = label;
        t.fontStyle = FontStyle.Bold;
        t.raycastTarget = true;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(320f, 70f);

        Button b = t.gameObject.AddComponent<Button>();
        b.targetGraphic = t;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock c = b.colors;
        c.normalColor = textColor;
        c.selectedColor = textColor;
        c.highlightedColor = hoverColor;
        c.pressedColor = hoverColor * 0.6f;
        c.disabledColor = textColor;
        c.fadeDuration = 0.08f;
        b.colors = c;
        b.onClick.AddListener(onClick);
    }

    // ---------------- Show ----------------

    public void Show()
    {
        if (IsShown) return;
        deathTime = Time.unscaledTime;
        Time.timeScale = 1f;                             // вдруг умерли посреди хит-стопа меча
        content.SetActive(true);

        // Гасим весь остальной UI
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c == canvas) continue;
            CanvasGroup g = c.GetComponent<CanvasGroup>();
            if (g == null) g = c.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            g.interactable = false;
            hiddenUI.Add(g);
        }

        CameraPOV look = FindFirstObjectByType<CameraPOV>();
        if (look != null) DeathCamera.Begin(look.transform, health != null ? health.transform : null);
    }

    void Update()
    {
        if (!IsShown) return;
        float t = Time.unscaledTime - deathTime;

        float h = 1f - Mathf.Clamp01(t / hudFadeTime);
        foreach (CanvasGroup g in hiddenUI)
            if (g != null) g.alpha = h;

        dark.color = new Color(0f, 0f, 0f, darkness * Smooth(t / darkenTime));

        if (fogMat != null)
        {
            fogMat.SetFloat("_Amount", Smooth((t - fogStart) / fogTime));
            fogMat.SetFloat("_T", Time.unscaledTime);
            fogMat.SetFloat("_Aspect", Screen.height > 0 ? Screen.width / (float)Screen.height : 1.78f);
        }

        float reveal = Mathf.Clamp01((t - cardStart) / cardTime);
        if (cardMat != null)
        {
            cardMat.SetFloat("_Reveal", reveal);
            cardMat.SetFloat("_T", Time.unscaledTime);
        }
        else card.color = new Color(1f, 1f, 1f, reveal);
        // опускается из дымки, потом еле заметно парит
        float settle = 1f - Smooth(reveal);
        cardRect.anchoredPosition = new Vector2(0f, 70f + settle * 40f + Mathf.Sin(t * 1.2f) * 5f * (1f - settle));

        float mk = Mathf.Clamp01((t - menuStart) / menuFadeTime);
        SetMenuVisible(mk);
        if (mk > 0f && !menuShown)
        {
            menuShown = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        float na = Mathf.Clamp01(2.5f - (Time.unscaledTime - noteTime));
        note.color = new Color(textColor.r, textColor.g, textColor.b, na);
    }

    private void SetMenuVisible(float a)
    {
        menu.alpha = a;
        menu.interactable = menu.blocksRaycasts = a >= 1f;
    }

    private void ShowNote(string text)
    {
        note.text = text;
        noteTime = Time.unscaledTime;
    }

    private void Retry()
    {
        Time.timeScale = 1f;
        PlayerInputLock.Locked = false;
        PlayerInputLock.WeaponsBlocked = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ---------------- UI helpers ----------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static T NewGraphic<T>(string name, Transform parent) where T : Graphic
    {
        T g = NewRect(name, parent).gameObject.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private Text NewText(string name, Transform parent, int size, Color color)
    {
        Text t = NewGraphic<Text>(name, parent);
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        return t;
    }

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
