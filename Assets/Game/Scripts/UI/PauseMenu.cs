using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Пауза в игре (Esc). Строит GameHUD, своя канва поверх HUD (экран смерти -- ещё выше).
//  - экран темнеет, время стоит (timeScale 0), звук на паузе, курсор свободен, управление заблокировано;
//  - столбик рамок с руками, как в главном меню: QUICK SAVE / QUICK LOAD (пока заглушки), SETTINGS, MAIN MENU,
//    BACK -- вернуться в игру;
//  - SETTINGS -- та же страница, что в главном меню (MenuSettingsPage); Esc или BACK -- обратно к списку;
//  - Esc на списке -- продолжить игру. Пока открыт рюкзак или игрок мёртв, Esc паузу не открывает.
[DefaultExecutionOrder(-50)]   // раньше Inventory: Esc при открытом рюкзаке закрывает рюкзак, а не открывает паузу
public class PauseMenu : MonoBehaviour
{
    public string menuScene = "MainMenu";
    [Range(0f, 1f)] public float darkness = 0.65f;
    public float fadeTime = 0.15f;
    public float buttonScale = 0.72f;
    public Color textColor = new Color(0.12f, 0.1f, 0.1f);
    public Color hoverColor = new Color(0.55f, 0.08f, 0.1f);

    public static bool IsPaused { get; private set; }

    private PlayerHealth health;
    private Inventory inventory;
    private KnightMovement movement;
    private Font font;
    private GameObject content;
    private Image dark;
    private CanvasGroup list, settings;
    private Text note;
    private float noteTime = -10f;
    private float shown;                 // 0..1 -- насколько видна пауза
    private bool inSettings;
    private bool wasLocked, movementWasEnabled;

    public static PauseMenu Create(Transform parent, PlayerHealth health, Inventory inventory)
    {
        GameObject go = new GameObject("PauseMenu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        PauseMenu p = go.AddComponent<PauseMenu>();
        p.health = health;
        p.inventory = inventory;
        p.movement = health != null ? health.GetComponent<KnightMovement>() : FindFirstObjectByType<KnightMovement>();
        p.Build();
        return p;
    }

    void OnDestroy()
    {
        if (IsPaused) Unfreeze();
        IsPaused = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!IsPaused) { if (CanPause()) Pause(); }
            else if (inSettings) ShowSettings(false);
            else Resume();
        }

        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime);
        shown = Mathf.MoveTowards(shown, IsPaused ? 1f : 0f, step);
        if (content.activeSelf != shown > 0f) content.SetActive(shown > 0f);
        dark.color = new Color(0f, 0f, 0f, darkness * shown);
        SetGroup(list, IsPaused && !inSettings ? Mathf.MoveTowards(list.alpha, 1f, step) : Mathf.MoveTowards(list.alpha, 0f, step));
        SetGroup(settings, IsPaused && inSettings ? Mathf.MoveTowards(settings.alpha, 1f, step) : Mathf.MoveTowards(settings.alpha, 0f, step));

        float na = Mathf.Clamp01(2f - (Time.unscaledTime - noteTime));
        note.color = new Color(0.92f, 0.9f, 0.88f, na);
    }

    private bool CanPause() => (health == null || !health.IsDead) && (inventory == null || !inventory.IsOpen);

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        inSettings = false;
        wasLocked = PlayerInputLock.Locked;
        PlayerInputLock.Locked = true;
        if (movement != null)
        {
            movementWasEnabled = movement.enabled;
            movement.enabled = false;            // иначе прыжок/подкат, нажатые на паузе, сработают после неё
        }
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Unfreeze();
        PlayerInputLock.Locked = wasLocked;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void Unfreeze()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (movement != null && movementWasEnabled) movement.enabled = true;
    }

    private void GoToMenu()
    {
        IsPaused = false;
        Unfreeze();
        PlayerInputLock.Locked = false;
        PlayerInputLock.WeaponsBlocked = false;
        SceneManager.LoadScene(menuScene);
    }

    private void ShowSettings(bool on)
    {
        inSettings = on;
        noteTime = -10f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    // Сохранений пока нет
    private void ShowNote(string text)
    {
        note.text = text;
        noteTime = Time.unscaledTime;
    }

    private static void SetGroup(CanvasGroup g, float alpha)
    {
        g.alpha = alpha;
        g.interactable = g.blocksRaycasts = alpha >= 0.99f;
        if (g.gameObject.activeSelf != alpha > 0f) g.gameObject.SetActive(alpha > 0f);
    }

    // ---------------- Build ----------------

    private void Build()
    {
        font = Resources.Load<Font>(MainMenu.Folder + "PirataOne-Regular");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;                        // над HUD (100), под экраном смерти (500)
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        content = NewRect("Content", transform).gameObject;
        Stretch((RectTransform)content.transform);

        dark = NewRect("Dark", content.transform).gameObject.AddComponent<Image>();
        dark.color = Color.clear;
        dark.raycastTarget = true;                        // клики мимо рамок не уходят в игру
        Stretch(dark.rectTransform);

        list = NewGroup("List");
        MainMenu.HandsCard(list.transform, font, "QUICK SAVE", new Vector2(0f, 320f), buttonScale, textColor, hoverColor, () => ShowNote("Saving is not ready yet"));
        MainMenu.HandsCard(list.transform, font, "QUICK LOAD", new Vector2(0f, 160f), buttonScale, textColor, hoverColor, () => ShowNote("Loading is not ready yet"));
        MainMenu.HandsCard(list.transform, font, "SETTINGS", new Vector2(0f, 0f), buttonScale, textColor, hoverColor, () => ShowSettings(true));
        MainMenu.HandsCard(list.transform, font, "MAIN MENU", new Vector2(0f, -160f), buttonScale, textColor, hoverColor, GoToMenu);
        MainMenu.HandsCard(list.transform, font, "BACK", new Vector2(0f, -320f), buttonScale, textColor, hoverColor, Resume);

        settings = NewGroup("Settings");
        MenuSettingsPage.Build(settings.transform, font, Resources.Load<Sprite>(MainMenu.Folder + "FrameHands"), new Vector2(0f, 50f), textColor, hoverColor);
        MainMenu.HandsCard(settings.transform, font, "BACK", new Vector2(0f, -430f), 0.66f, textColor, hoverColor, () => ShowSettings(false));

        note = NewRect("Note", content.transform).gameObject.AddComponent<Text>();
        note.font = font;
        note.fontSize = 40;
        note.alignment = TextAnchor.MiddleCenter;
        note.horizontalOverflow = HorizontalWrapMode.Overflow;
        note.raycastTarget = false;
        note.color = Color.clear;
        RectTransform nr = note.rectTransform;
        nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(0.5f, 0.5f);
        nr.anchoredPosition = new Vector2(0f, -460f);
        nr.sizeDelta = new Vector2(900f, 60f);

        SetGroup(list, 0f);
        SetGroup(settings, 0f);
        content.SetActive(false);
    }

    private CanvasGroup NewGroup(string name)
    {
        RectTransform rt = NewRect(name, content.transform);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1920f, 1080f);
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
