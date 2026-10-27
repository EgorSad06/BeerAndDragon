using System;
using UnityEngine;
using UnityEngine.UI;

// Страница SETTINGS главного меню: строки «название -- ползунок -- значение» на большой рамке с руками
// (та же картинка, что у кнопок, растянута по 9-slice -- руки в углах не тянутся).
// Значения сразу пишутся в GameSettings (PlayerPrefs) и действуют в игре. Строит MainMenu.
public class MenuSettingsPage : MonoBehaviour
{
    private Font font;
    private Color textColor, hoverColor;
    private Action refresh;
    private bool fullscreen;

    private static readonly Color Track = new Color(0.2f, 0.17f, 0.17f, 0.55f);
    private static readonly Color Fill = new Color(0.5f, 0.07f, 0.1f);
    private static readonly Color Handle = new Color(0.12f, 0.1f, 0.1f);

    private const float LabelX = -300f, SliderX = 95f, SliderW = 200f, ValueX = 270f;

    public static MenuSettingsPage Build(Transform page, Font font, Sprite panelSprite, Vector2 pos, Color textColor, Color hoverColor)
    {
        MenuSettingsPage p = page.gameObject.AddComponent<MenuSettingsPage>();
        p.font = font;
        p.textColor = textColor;
        p.hoverColor = hoverColor;
        p.BuildPanel(page, panelSprite, pos);
        p.refresh?.Invoke();
        return p;
    }

    void OnEnable()
    {
        fullscreen = Screen.fullScreen;
        refresh?.Invoke();
    }

    private void BuildPanel(Transform page, Sprite panelSprite, Vector2 pos)
    {
        Image panel = NewGraphic<Image>("Panel", page);
        panel.sprite = panelSprite;
        panel.type = Image.Type.Sliced;
        panel.pixelsPerUnitMultiplier = 1.2f;                // руки в углах -- того же размера, что на кнопках
        Place(panel.rectTransform, pos, new Vector2(680f, 680f));
        RectTransform r = panel.rectTransform;

        Text title = NewText("Title", r, 54, TextAnchor.MiddleCenter);
        title.text = "SETTINGS";
        Place(title.rectTransform, new Vector2(0f, 262f), new Vector2(400f, 70f));

        float y = 175f;
        const float step = 72f;
        SliderRow(r, "MASTER VOLUME", y, 0f, 1f, false, () => GameSettings.MasterVolume, GameSettings.SetMasterVolume, v => Mathf.RoundToInt(v * 100f) + "%");
        y -= step;
        SliderRow(r, "MOUSE SENSITIVITY", y, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, false,
            () => GameSettings.MouseSensitivity, GameSettings.SetMouseSensitivity, v => "x" + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        y -= step;
        ToggleRow(r, "INVERT MOUSE Y", y, () => GameSettings.InvertY, () => GameSettings.SetInvertY(!GameSettings.InvertY));
        y -= step;
        SliderRow(r, "FIELD OF VIEW", y, GameSettings.MinFov, GameSettings.MaxFov, true,
            () => GameSettings.FieldOfView, GameSettings.SetFieldOfView, v => Mathf.RoundToInt(v).ToString());
        y -= step;
        SliderRow(r, "CAMERA SHAKE", y, 0f, 1f, false, () => GameSettings.CameraKick, GameSettings.SetCameraKick, v => Mathf.RoundToInt(v * 100f) + "%");
        y -= step;
        ToggleRow(r, "FULLSCREEN", y, () => fullscreen, () =>
        {
            fullscreen = !fullscreen;
            Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        });
        y -= step + 10f;

        Text reset = NewText("Reset", r, 38, TextAnchor.MiddleCenter);
        reset.text = "RESET TO DEFAULTS";
        Place(reset.rectTransform, new Vector2(0f, y), new Vector2(420f, 56f));
        TextButton(reset, () => { GameSettings.ResetToDefaults(); refresh?.Invoke(); });
    }

    // ---------------- строки ----------------

    private void SliderRow(RectTransform panel, string label, float y, float min, float max, bool whole,
        Func<float> get, Action<float> set, Func<float, string> format)
    {
        Label(panel, label, y);
        Text value = NewText("Value", panel, 38, TextAnchor.MiddleRight);
        Place(value.rectTransform, new Vector2(ValueX, y), new Vector2(90f, 50f));

        RectTransform root = NewRect(label, panel);
        Place(root, new Vector2(SliderX, y), new Vector2(SliderW, 40f));
        Image hit = NewGraphic<Image>("Hit", root);          // ловит мышь по всей высоте строки, а не только по тонкой полоске
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Stretch(hit.rectTransform);

        Image track = NewGraphic<Image>("Track", root);
        track.color = Track;
        Place(track.rectTransform, Vector2.zero, new Vector2(SliderW, 6f));

        RectTransform fillArea = NewRect("FillArea", root);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(0f, 6f);
        Image fill = NewGraphic<Image>("Fill", fillArea);
        fill.color = Fill;
        Stretch(fill.rectTransform);

        // Slider тянет ручку на всю высоту области -- область высотой с ручку, иначе ромбик вытягивается
        RectTransform handleArea = NewRect("HandleArea", root);
        handleArea.anchorMin = new Vector2(0f, 0.5f);
        handleArea.anchorMax = new Vector2(1f, 0.5f);
        handleArea.sizeDelta = new Vector2(0f, 18f);
        Image handle = NewGraphic<Image>("Handle", handleArea);
        handle.color = Color.white;
        handle.rectTransform.sizeDelta = new Vector2(18f, 0f);
        handle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);   // ромбик

        Slider s = root.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = handle;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = whole;
        s.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock c = s.colors;
        c.normalColor = c.selectedColor = Handle;
        c.highlightedColor = hoverColor;
        c.pressedColor = hoverColor * 0.8f;
        c.fadeDuration = 0.08f;
        s.colors = c;
        s.onValueChanged.AddListener(v => { set(v); value.text = format(get()); });

        refresh += () =>
        {
            s.SetValueWithoutNotify(get());
            value.text = format(get());
        };
    }

    private void ToggleRow(RectTransform panel, string label, float y, Func<bool> get, Action toggle)
    {
        Label(panel, label, y);
        Image box = NewGraphic<Image>(label, panel);
        box.sprite = UISprites.Frame(3, 6);
        box.type = Image.Type.Sliced;
        box.color = textColor;
        Place(box.rectTransform, new Vector2(ValueX - 45f, y), new Vector2(110f, 48f));
        Text t = NewText("State", box.rectTransform, 36, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform);
        TextButton(t, () => { toggle(); t.text = get() ? "ON" : "OFF"; });
        refresh += () => t.text = get() ? "ON" : "OFF";
    }

    private void Label(RectTransform panel, string label, float y)
    {
        Text t = NewText("Label", panel, 40, TextAnchor.MiddleLeft);
        t.text = label;
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.rectTransform.anchoredPosition = new Vector2(LabelX, y);
        t.rectTransform.sizeDelta = new Vector2(320f, 50f);
    }

    // Надпись-кнопка: белый текст, цвет задаёт подсветка кнопки
    private void TextButton(Text t, UnityEngine.Events.UnityAction onClick)
    {
        t.color = Color.white;
        t.raycastTarget = true;
        Button b = t.gameObject.AddComponent<Button>();
        b.targetGraphic = t;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock c = b.colors;
        c.normalColor = c.selectedColor = textColor;
        c.highlightedColor = hoverColor;
        c.pressedColor = hoverColor * 0.7f;
        c.fadeDuration = 0.08f;
        b.colors = c;
        b.onClick.AddListener(onClick);
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

    private Text NewText(string name, Transform parent, int size, TextAnchor align)
    {
        Text t = NewGraphic<Text>(name, parent);
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = textColor;
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
