using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Главное меню (сцена MainMenu). Всё строится из кода -- в сцене нужны только камера и объект с этим компонентом.
//  - главная страница: PLAY / SETTINGS / CREDITS / EXIT;
//  - PLAY -- три слота сохранений (рамка с крестом); сохранений пока нет: любой слот грузит gameScene;
//  - SETTINGS -- свой фон с шестерёнкой, пока пусто; CREDITS -- пусто; на обеих только BACK (и Esc);
//  - кнопки -- рамка с руками, надписи шрифтом Pirata One (в нём только латиница, поэтому по-английски);
//  - при наведении карточка «левитирует», как карточки в Steam: приподнимается над своей тенью, покачивается
//    и наклоняется в перспективе к курсору (MenuButtonHover + MenuCardTilt).
// Картинки и шрифт -- Resources/UI/Menu.
public class MainMenu : MonoBehaviour
{
    public string gameScene = "SampleScene";

    [Header("Вид")]
    public Color textColor = new Color(0.12f, 0.1f, 0.1f);      // тёмная на светло-серой рамке
    public Color hoverColor = new Color(0.55f, 0.08f, 0.1f);
    public Color slotTextColor = new Color(0.12f, 0.1f, 0.1f);
    public Color slotHoverColor = new Color(0.55f, 0.08f, 0.1f);
    public float buttonScale = 0.82f;                            // рамка с руками 462x220 -> на экране
    public float slotScale = 0.92f;                              // рамка с крестом 480x260
    public float pageFadeTime = 0.18f;

    private const string Folder = "UI/Menu/";
    private const float ColumnX = -500f;                        // левая половина фона пустая -- кнопки там

    private enum Page { Main, Play, Settings, Credits }

    private Font font;
    private RectTransform root;
    private Image background, settingsBackground;
    private readonly Dictionary<Page, CanvasGroup> pages = new Dictionary<Page, CanvasGroup>();
    private Page current = Page.Main;
    private float settingsBlend;

    void Start()
    {
        Time.timeScale = 1f;
        PlayerInputLock.Locked = false;
        PlayerInputLock.WeaponsBlocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        font = Resources.Load<Font>(Folder + "PirataOne-Regular");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);

        BuildCanvas();
        background = BuildBackground("Background", "MenuBackground");
        settingsBackground = BuildBackground("SettingsBackground", "MenuSettingsBackground");
        settingsBackground.color = new Color(1f, 1f, 1f, 0f);

        CanvasGroup main = NewPage(Page.Main);
        string[] labels = { "PLAY", "SETTINGS", "CREDITS", "EXIT" };
        UnityEngine.Events.UnityAction[] actions = { () => Open(Page.Play), () => Open(Page.Settings), () => Open(Page.Credits), Quit };
        for (int i = 0; i < labels.Length; i++)
            HandsButton(main.transform, labels[i], new Vector2(ColumnX, 300f - i * 200f), actions[i]);

        CanvasGroup play = NewPage(Page.Play);
        string[] numerals = { "I", "II", "III" };
        for (int i = 0; i < 3; i++)
            SaveSlot(play.transform, i, numerals[i], new Vector2(ColumnX, 330f - i * 260f));
        HandsButton(play.transform, "BACK", new Vector2(ColumnX, -420f), () => Open(Page.Main), 0.8f);

        HandsButton(NewPage(Page.Settings).transform, "BACK", new Vector2(ColumnX, -380f), () => Open(Page.Main));
        HandsButton(NewPage(Page.Credits).transform, "BACK", new Vector2(ColumnX, -380f), () => Open(Page.Main));

        foreach (var p in pages) SetPage(p.Value, p.Key == current ? 1f : 0f);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && current != Page.Main) Open(Page.Main);

        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, pageFadeTime);
        foreach (var p in pages)
            SetPage(p.Value, Mathf.MoveTowards(p.Value.alpha, p.Key == current ? 1f : 0f, step));
        settingsBlend = Mathf.MoveTowards(settingsBlend, current == Page.Settings ? 1f : 0f, step);
        settingsBackground.color = new Color(1f, 1f, 1f, settingsBlend);
    }

    private void Open(Page page)
    {
        current = page;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private static void SetPage(CanvasGroup g, float alpha)
    {
        g.alpha = alpha;
        g.interactable = g.blocksRaycasts = alpha >= 0.99f;
        g.gameObject.SetActive(alpha > 0f);
    }

    // Сохранений пока нет -- любой слот просто запускает игру
    private void StartGame(int slot)
    {
        GameLog.Info("Menu", "Слот " + (slot + 1) + " -> " + gameScene);
        SceneManager.LoadScene(gameScene);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- Build ----------------

    private void BuildCanvas()
    {
        GameObject go = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)go.transform;
    }

    // Фон 1920x1080 закрывает экран целиком при любом соотношении сторон (лишнее обрезается по краям)
    private Image BuildBackground(string name, string sprite)
    {
        Image img = NewImage(name, root, Resources.Load<Sprite>(Folder + sprite));
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1920f, 1080f);
        AspectRatioFitter fit = img.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = 16f / 9f;
        return img;
    }

    private CanvasGroup NewPage(Page page)
    {
        RectTransform rt = NewRect(page.ToString(), root);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1920f, 1080f);
        CanvasGroup g = rt.gameObject.AddComponent<CanvasGroup>();
        pages[page] = g;
        return g;
    }

    // Кнопка в рамке с руками: надпись -- в прямоугольнике между ладонями, чуть ниже середины картинки
    private void HandsButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick, float scale = 1f)
    {
        float k = buttonScale * scale;
        Image shadow = Card(parent, label, "FrameHands", pos, new Vector2(462f, 220f) * k, out Image frame);
        Text t = NewText("Label", frame.rectTransform, Mathf.RoundToInt(96 * k), textColor);
        t.text = label;
        Place(t.rectTransform, new Vector2(0f, -14f * k), new Vector2(330f, 150f) * k);
        AddButton(shadow, frame, t, textColor, hoverColor, onClick);
    }

    // Слот сохранения в рамке с крестом: надпись -- в сером прямоугольнике справа от креста
    private void SaveSlot(Transform parent, int index, string numeral, Vector2 pos)
    {
        float k = slotScale;
        Image shadow = Card(parent, "Slot" + (index + 1), "FrameCross", pos, new Vector2(480f, 260f) * k, out Image frame);
        Text t = NewText("Label", frame.rectTransform, Mathf.RoundToInt(78 * k), slotTextColor);
        t.text = "SAVE " + numeral;
        Place(t.rectTransform, new Vector2(42f, 32f) * k, new Vector2(340f, 90f) * k);
        Text empty = NewText("State", frame.rectTransform, Mathf.RoundToInt(44 * k), new Color(slotTextColor.r, slotTextColor.g, slotTextColor.b, 0.6f));
        empty.text = "empty";
        Place(empty.rectTransform, new Vector2(42f, -40f) * k, new Vector2(340f, 60f) * k);
        AddButton(shadow, frame, t, slotTextColor, slotHoverColor, () => StartGame(index));
    }

    // Карточка: снизу тень (та же картинка, чёрная; она же ловит мышь и стоит на месте), над ней -- сама рамка,
    // которая при наведении поднимается
    private static Image Card(Transform parent, string name, string sprite, Vector2 pos, Vector2 size, out Image frame)
    {
        Sprite s = Resources.Load<Sprite>(Folder + sprite);
        Image shadow = NewImage(name, parent, s);
        shadow.color = new Color(0f, 0f, 0f, 0f);
        Place(shadow.rectTransform, pos, size);
        frame = NewImage("Frame", shadow.rectTransform, s);
        Place(frame.rectTransform, Vector2.zero, size);
        return shadow;
    }

    private static void AddButton(Image shadow, Image frame, Text label, Color normal, Color hover, UnityEngine.Events.UnityAction onClick)
    {
        shadow.raycastTarget = true;
        Button b = shadow.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(onClick);
        MenuButtonHover h = shadow.gameObject.AddComponent<MenuButtonHover>();
        h.card = frame.rectTransform;
        h.shadow = shadow;
        h.label = label;
        h.normal = normal;
        h.hover = hover;
    }

    // ---------------- UI helpers ----------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        Image img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(string name, Transform parent, int size, Color color)
    {
        Text t = NewRect(name, parent).gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
