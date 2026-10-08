using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Весь HUD строится из кода при старте -- в сцене нужен только объект с этим компонентом.
//  - хотбар сверху (1..9), выбранное оружие подсвечено;
//  - Tab -- рюкзак под хотбаром: перетащи слот на слот или кликни по двум слотам, чтобы поменять местами;
//    ПКМ по расходнику -- использовать;
//  - HP слева снизу, таймер энергетика над ним;
//  - патроны справа снизу (в магазине / в запасе);
//  - прицел, всплывающие сообщения, красная вспышка при уроне, экран смерти.
public class GameHUD : MonoBehaviour
{
    [Header("Refs (найдутся сами)")]
    public Inventory inventory;
    public PlayerHealth health;
    public PlayerStatusEffects effects;
    public WeaponSwitcher weaponSwitcher;

    [Header("Look")]
    public Sprite ammoIcon;
    public Color panelColor = new Color(0.08f, 0.06f, 0.05f, 0.82f);
    public Color slotColor = new Color(0.18f, 0.14f, 0.11f, 0.9f);
    public Color bagLeather = new Color(0.47f, 0.29f, 0.15f);
    public Color bagPocket = new Color(0.3f, 0.17f, 0.08f);
    public Color bagStitch = new Color(0.95f, 0.84f, 0.58f);
    public Color bagMetal = new Color(0.9f, 0.72f, 0.32f);
    public Color slotSelectedColor = new Color(0.85f, 0.62f, 0.2f, 0.95f);
    public Color slotPickedColor = new Color(0.3f, 0.65f, 0.95f, 0.95f);
    public Color healthColor = new Color(0.78f, 0.12f, 0.1f);
    public Color boostColor = new Color(0.45f, 0.95f, 0.2f);
    public int slotSize = 84;
    public int slotSpacing = 6;

    private Font font;
    private RectTransform root;

    private InventorySlotView[] slotViews;
    private GameObject backpackPanel;
    private RectTransform bagRoot;
    private float bagOpenTime;
    private GameObject dim;
    private Text tooltipText;
    private Image dragGhost;
    private int pickedSlot = -1;   // первый клик при обмене кликами
    private int dragFrom = -1;

    private RectTransform healthFill, healthTrail;
    private Text healthText;
    private float trailValue = 1f;

    private GameObject boostRoot;
    private RectTransform boostFill;
    private Text boostText;

    private GameObject ammoRoot;
    private Text ammoMagText, ammoReserveText, reloadText;
    private Transform lastWeaponTransform;
    private Weapon currentWeapon;
    private int lastMag = -1, lastReserve = -1;

    private RectTransform messagesRoot;
    private readonly List<(Text text, float time)> messages = new List<(Text, float)>();
    private const float MessageLifetime = 2.5f;

    private Image damageFlash;
    private GameObject deathScreen;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Start()
    {
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (health == null) health = FindFirstObjectByType<PlayerHealth>();
        if (effects == null) effects = FindFirstObjectByType<PlayerStatusEffects>();
        if (weaponSwitcher == null) weaponSwitcher = FindFirstObjectByType<WeaponSwitcher>(FindObjectsInactive.Include);

        EnsureEventSystem();
        BuildCanvas();
        BuildCrosshair();
        BuildDamageFlash();
        BuildHealth();
        BuildAmmo();
        BuildMessages();
        BuildInventory();
        BuildDeathScreen();

        if (inventory != null)
        {
            inventory.Changed += RefreshSlots;
            inventory.OpenChanged += OnInventoryOpenChanged;
            RefreshSlots();
        }
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }
        HudMessages.Posted += ShowMessage;
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.Changed -= RefreshSlots;
            inventory.OpenChanged -= OnInventoryOpenChanged;
        }
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
        HudMessages.Posted -= ShowMessage;
    }

    void Update()
    {
        UpdateHealth();
        UpdateBoost();
        UpdateAmmo();
        UpdateMessages();
        AnimateBag();

        if (damageFlash != null && damageFlash.color.a > 0f)
        {
            Color c = damageFlash.color;
            c.a = Mathf.MoveTowards(c.a, 0f, Time.deltaTime * 1.2f);
            damageFlash.color = c;
        }
    }

    // ---------------- Build ----------------

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        es.transform.SetParent(transform, false);
    }

    private void BuildCanvas()
    {
        GameObject go = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)go.transform;
    }

    private void BuildCrosshair()
    {
        RectTransform ch = NewRect("Crosshair", root);
        Place(ch, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        const float gap = 6f, len = 10f, thick = 2f;
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (Vector2 d in dirs)
        {
            Image line = NewImage("Tick", ch, Color.white);
            Vector2 size = d.x != 0 ? new Vector2(len, thick) : new Vector2(thick, len);
            Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), d * (gap + len * 0.5f), size);
            line.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
        }
    }

    private void BuildDamageFlash()
    {
        damageFlash = NewImage("DamageFlash", root, new Color(0.8f, 0f, 0f, 0f));
        Stretch(damageFlash.rectTransform);
    }

    private void BuildHealth()
    {
        const float w = 420f, h = 34f;
        RectTransform box = NewRect("Health", root);
        Place(box, Vector2.zero, Vector2.zero, new Vector2(32 + w * 0.5f, 40 + h * 0.5f), new Vector2(w, h));

        Image frame = NewImage("Frame", box, new Color(0f, 0f, 0f, 0.7f));
        Stretch(frame.rectTransform, -3f);

        healthTrail = NewImage("Trail", box, new Color(1f, 0.9f, 0.8f, 0.9f)).rectTransform;
        FillRect(healthTrail, 1f);
        healthFill = NewImage("Fill", box, healthColor).rectTransform;
        FillRect(healthFill, 1f);

        healthText = NewText("Text", box, 22, TextAnchor.MiddleCenter, Color.white);
        Stretch(healthText.rectTransform);

        Text label = NewText("Label", box, 18, TextAnchor.LowerLeft, new Color(1f, 0.85f, 0.7f));
        label.text = "ЗДОРОВЬЕ";
        Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, 16f), new Vector2(200f, 24f));

        // Энергетик -- полоска над HP
        boostRoot = NewRect("Boost", root).gameObject;
        RectTransform b = (RectTransform)boostRoot.transform;
        Place(b, Vector2.zero, Vector2.zero, new Vector2(32 + w * 0.5f, 40 + h + 40f), new Vector2(w, 10f));
        Image bFrame = NewImage("Frame", b, new Color(0f, 0f, 0f, 0.7f));
        Stretch(bFrame.rectTransform, -2f);
        boostFill = NewImage("Fill", b, boostColor).rectTransform;
        FillRect(boostFill, 1f);
        boostText = NewText("Text", b, 18, TextAnchor.LowerLeft, boostColor);
        Place(boostText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, 16f), new Vector2(300f, 24f));
        boostRoot.SetActive(false);
    }

    private void BuildAmmo()
    {
        ammoRoot = NewRect("Ammo", root).gameObject;
        RectTransform a = (RectTransform)ammoRoot.transform;
        Place(a, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-190f, 70f), new Vector2(320f, 90f));

        if (ammoIcon != null)
        {
            Image icon = NewImage("Icon", a, Color.white);
            icon.sprite = ammoIcon;
            icon.preserveAspect = true;
            Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(70f, 80f));
        }

        ammoMagText = NewText("Mag", a, 72, TextAnchor.MiddleRight, Color.white);
        Place(ammoMagText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(100f, 90f));
        ammoReserveText = NewText("Reserve", a, 36, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.7f));
        Place(ammoReserveText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(260f, -8f), new Vector2(120f, 60f));

        reloadText = NewText("Reload", a, 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.3f));
        reloadText.text = "ПЕРЕЗАРЯДКА...";
        Place(reloadText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 16f), new Vector2(300f, 30f));
        reloadText.gameObject.SetActive(false);

        ammoRoot.SetActive(false);
    }

    private void BuildMessages()
    {
        messagesRoot = NewRect("Messages", root);
        Place(messagesRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(232f, 0f), new Vector2(400f, 300f));
    }

    private void BuildInventory()
    {
        if (inventory == null) return;

        int hotbar = inventory.HotbarSize;
        int total = inventory.Slots.Length;
        int cols = hotbar;
        int rows = Mathf.CeilToInt((total - hotbar) / (float)cols);
        float step = slotSize + slotSpacing;
        float width = cols * step - slotSpacing;

        // Затемнение фона (видно только при открытом инвентаре)
        dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.5f)).gameObject;
        Stretch((RectTransform)dim.transform);
        dim.SetActive(false);

        // ---- Сумка ----
        const float flapHeight = 112f;
        float bagW = width + 80f;
        float bagH = flapHeight + rows * step - slotSpacing + 110f;
        bagRoot = NewRect("Bag", root);
        bagRoot.anchorMin = bagRoot.anchorMax = new Vector2(0.5f, 0.5f);
        bagRoot.pivot = new Vector2(0.5f, 0.5f);
        bagRoot.anchoredPosition = new Vector2(0f, -40f);
        bagRoot.sizeDelta = new Vector2(bagW, bagH);
        backpackPanel = bagRoot.gameObject;

        // Ручка -- первой, чтобы оказалась за корпусом
        Image handle = NewImage("Handle", bagRoot, Color.white);
        handle.sprite = UISprites.Handle(bagLeather * 0.75f);
        RectTransform hrt = handle.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, -24f);
        hrt.sizeDelta = new Vector2(bagW * 0.5f, 130f);

        Image body = NewImage("Body", bagRoot, Color.white);
        body.sprite = UISprites.Leather(bagLeather, bagStitch, 26, 11);
        body.type = Image.Type.Sliced;
        Stretch(body.rectTransform);

        Image flap = NewImage("Flap", bagRoot, Color.white);
        flap.sprite = UISprites.Flap(bagLeather * 0.82f, bagStitch);
        RectTransform frt = flap.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);
        frt.pivot = new Vector2(0.5f, 1f);
        frt.anchoredPosition = new Vector2(0f, 6f);
        frt.sizeDelta = new Vector2(bagW + 14f, flapHeight);
        flap.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);

        Text title = NewText("Title", frt, 34, TextAnchor.MiddleCenter, bagStitch);
        title.text = "С У М К А";
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(400f, 40f));

        Image buckle = NewImage("Buckle", frt, Color.white);
        buckle.sprite = UISprites.Buckle(bagMetal);
        Place(buckle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(52f, 44f));

        tooltipText = NewText("Tooltip", bagRoot, 20, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.85f));
        Place(tooltipText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 62f), new Vector2(-60f, 30f));

        Text hint = NewText("Hint", bagRoot, 16, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.75f, 0.7f));
        hint.text = "Перетащи или кликни два кармана -- поменять местами.  ПКМ -- использовать.  Tab -- закрыть";
        Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(-60f, 24f));

        backpackPanel.SetActive(false);

        // ---- Хотбар на кожаном ремне (поверх затемнения) ----
        RectTransform bar = NewRect("Hotbar", root);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -22f);
        bar.sizeDelta = new Vector2(width, slotSize);

        Image belt = NewImage("Belt", bar, Color.white);
        belt.sprite = UISprites.Leather(bagLeather * 0.7f, bagStitch * 0.85f, 12, 5);
        belt.type = Image.Type.Sliced;
        Stretch(belt.rectTransform, -10f);

        slotViews = new InventorySlotView[total];
        for (int i = 0; i < total; i++)
        {
            bool isHotbar = i < hotbar;
            Transform parent = isHotbar ? (Transform)bar : bagRoot;
            int col = isHotbar ? i : (i - hotbar) % cols;
            int row = isHotbar ? 0 : (i - hotbar) / cols;
            float x = -width * 0.5f + col * step + slotSize * 0.5f;
            float y = isHotbar ? -slotSize * 0.5f : -(flapHeight + 18f) - row * step - slotSize * 0.5f;
            slotViews[i] = BuildSlot(parent, i, new Vector2(x, y), isHotbar ? (i + 1).ToString() : null);
        }

        dragGhost = NewImage("DragGhost", root, new Color(1f, 1f, 1f, 0.85f));
        dragGhost.raycastTarget = false;
        dragGhost.preserveAspect = true;
        dragGhost.rectTransform.sizeDelta = new Vector2(slotSize, slotSize);
        dragGhost.gameObject.SetActive(false);
    }

    // Слот -- кожаный кармашек со строчкой; выбранный подсвечивается рамкой
    private InventorySlotView BuildSlot(Transform parent, int index, Vector2 pos, string key)
    {
        Image bg = NewImage("Slot" + index, parent, Color.white);
        bg.sprite = UISprites.Leather(bagPocket, bagStitch * 0.8f, 12, 6, 64);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;
        RectTransform rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        Image icon = NewImage("Icon", rt, Color.white);
        icon.preserveAspect = true;
        Stretch(icon.rectTransform, 11f);

        Image highlight = NewImage("Highlight", rt, Color.clear);
        highlight.sprite = UISprites.Frame(4, 12);
        highlight.type = Image.Type.Sliced;
        Stretch(highlight.rectTransform, -3f);

        Text count = NewText("Count", rt, 20, TextAnchor.LowerRight, Color.white);
        Stretch(count.rectTransform, 7f);

        if (key != null)
        {
            Text keyText = NewText("Key", rt, 16, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.7f, 0.8f));
            keyText.text = key;
            Stretch(keyText.rectTransform, 7f);
        }

        InventorySlotView view = bg.gameObject.AddComponent<InventorySlotView>();
        view.Init(this, index, icon, count, highlight);
        return view;
    }

    private void BuildDeathScreen()
    {
        Image bg = NewImage("Death", root, new Color(0.25f, 0f, 0f, 0.6f));
        Stretch(bg.rectTransform);
        Text t = NewText("Text", bg.rectTransform, 110, TextAnchor.MiddleCenter, new Color(0.9f, 0.1f, 0.1f));
        t.text = "ПОТРАЧЕНО";
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform);
        deathScreen = bg.gameObject;
        deathScreen.SetActive(false);
    }

    // ---------------- Update ----------------

    private void UpdateHealth()
    {
        if (health == null || healthFill == null) return;
        float v = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.maxHealth));
        FillRect(healthFill, v);
        trailValue = trailValue > v ? Mathf.MoveTowards(trailValue, v, Time.deltaTime * 0.6f) : v;
        FillRect(healthTrail, trailValue);
        healthText.text = $"{Mathf.CeilToInt(health.Health)} / {Mathf.CeilToInt(health.maxHealth)}";
    }

    private void UpdateBoost()
    {
        if (boostRoot == null) return;
        bool active = effects != null && effects.SpeedBoostRemaining > 0f;
        if (boostRoot.activeSelf != active) boostRoot.SetActive(active);
        if (!active) return;
        FillRect(boostFill, effects.SpeedBoostRemaining / Mathf.Max(0.01f, effects.SpeedBoostTotal));
        boostText.text = $"ЭНЕРГЕТИК  x{effects.SpeedMultiplier:0.#}  {effects.SpeedBoostRemaining:0.0}с";
    }

    private void UpdateAmmo()
    {
        if (ammoRoot == null) return;

        Transform w = weaponSwitcher != null ? weaponSwitcher.CurrentWeapon : null;
        if (w != lastWeaponTransform)
        {
            lastWeaponTransform = w;
            currentWeapon = w != null ? w.GetComponent<Weapon>() : null;
            lastMag = lastReserve = -1;
        }

        bool show = currentWeapon != null;
        if (ammoRoot.activeSelf != show) ammoRoot.SetActive(show);
        if (!show) return;

        int mag = currentWeapon.CurrentAmmo;
        int reserve = currentWeapon.InfiniteReserve ? -2 : currentWeapon.ReserveAmmo;
        if (mag != lastMag || reserve != lastReserve)
        {
            lastMag = mag;
            lastReserve = reserve;
            ammoMagText.text = mag.ToString();
            ammoMagText.color = mag == 0 ? new Color(1f, 0.3f, 0.25f) : Color.white;
            ammoReserveText.text = reserve == -2 ? "/ ∞" : "/ " + reserve;
            ammoReserveText.color = reserve == 0 ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.85f, 0.7f);
        }
        reloadText.gameObject.SetActive(currentWeapon.IsReloading);
    }

    // Сумка "выпрыгивает" с пружинкой
    private void AnimateBag()
    {
        if (bagRoot == null || !bagRoot.gameObject.activeSelf) return;
        float t = Mathf.Clamp01((Time.unscaledTime - bagOpenTime) / 0.22f);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float back = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); // easeOutBack
        float k = Mathf.LerpUnclamped(0.75f, 1f, back);
        bagRoot.localScale = new Vector3(k, k, 1f);
        bagRoot.localRotation = Quaternion.Euler(0f, 0f, (1f - t) * -4f);
    }

    private void UpdateMessages()
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            (Text text, float time) = messages[i];
            float age = Time.time - time;
            if (age > MessageLifetime)
            {
                Destroy(text.gameObject);
                messages.RemoveAt(i);
                continue;
            }
            Color c = text.color;
            c.a = Mathf.Clamp01((MessageLifetime - age) / 0.5f);
            text.color = c;
        }
        for (int i = 0; i < messages.Count; i++)
            messages[i].text.rectTransform.anchoredPosition = new Vector2(0f, -i * 30f);
    }

    // ---------------- Events ----------------

    private void ShowMessage(string msg)
    {
        if (messagesRoot == null) return;
        Text t = NewText("Msg", messagesRoot, 24, TextAnchor.MiddleLeft, new Color(1f, 0.95f, 0.8f));
        t.text = msg;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(400f, 30f);
        messages.Insert(0, (t, Time.time));
        while (messages.Count > 6)
        {
            Destroy(messages[messages.Count - 1].text.gameObject);
            messages.RemoveAt(messages.Count - 1);
        }
    }

    private void OnDamaged(float amount)
    {
        if (damageFlash == null) return;
        Color c = damageFlash.color;
        c.a = Mathf.Clamp(c.a + 0.15f + amount * 0.01f, 0f, 0.45f);
        damageFlash.color = c;
    }

    private void OnDied()
    {
        if (inventory != null) inventory.SetOpen(false);
        PlayerInputLock.Locked = true;
        if (deathScreen != null) deathScreen.SetActive(true);
    }

    private void OnInventoryOpenChanged(bool open)
    {
        if (backpackPanel != null) backpackPanel.SetActive(open);
        bagOpenTime = Time.unscaledTime;
        if (dim != null) dim.SetActive(open);
        pickedSlot = -1;
        dragFrom = -1;
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        if (tooltipText != null) tooltipText.text = "";
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        if (slotViews == null || inventory == null) return;
        for (int i = 0; i < slotViews.Length; i++)
        {
            InventorySlot s = inventory.Slots[i];
            Color hl = i == pickedSlot ? slotPickedColor
                     : i == inventory.SelectedHotbar ? slotSelectedColor
                     : Color.clear;
            slotViews[i].Show(s.IsEmpty ? null : s.item.icon, s.IsEmpty || s.count <= 1 ? "" : s.count.ToString(), hl,
                s.IsEmpty ? "" : s.item.displayName);
        }
    }

    // --- вызывается из InventorySlotView ---

    internal void SlotClicked(int index, PointerEventData.InputButton button)
    {
        if (inventory == null || !inventory.IsOpen) return;

        if (button == PointerEventData.InputButton.Right)
        {
            inventory.Use(index);
            return;
        }
        if (button != PointerEventData.InputButton.Left) return;

        if (pickedSlot < 0)
        {
            if (!inventory.Slots[index].IsEmpty) pickedSlot = index;
        }
        else
        {
            int from = pickedSlot;
            pickedSlot = -1;
            inventory.Swap(from, index);
        }
        RefreshSlots();
    }

    internal void SlotBeginDrag(int index, PointerEventData e)
    {
        if (inventory == null || !inventory.IsOpen || inventory.Slots[index].IsEmpty) return;
        dragFrom = index;
        pickedSlot = -1;
        dragGhost.sprite = inventory.Slots[index].item.icon;
        dragGhost.gameObject.SetActive(dragGhost.sprite != null);
        dragGhost.rectTransform.position = e.position;
        RefreshSlots();
    }

    internal void SlotDrag(PointerEventData e)
    {
        if (dragFrom >= 0) dragGhost.rectTransform.position = e.position;
    }

    internal void SlotDrop(int index)
    {
        if (dragFrom < 0) return;
        int from = dragFrom;
        dragFrom = -1;
        inventory.Swap(from, index);
    }

    internal void SlotEndDrag()
    {
        dragFrom = -1;
        dragGhost.gameObject.SetActive(false);
    }

    internal void SlotHover(int index, bool enter)
    {
        if (tooltipText == null || inventory == null || !inventory.IsOpen) return;
        InventorySlot s = inventory.Slots[index];
        if (!enter || s.IsEmpty) { tooltipText.text = ""; return; }
        tooltipText.text = string.IsNullOrEmpty(s.item.description)
            ? s.item.displayName
            : $"{s.item.displayName} -- {s.item.description}";
    }

    // ---------------- UI helpers ----------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(string name, Transform parent, int size, TextAnchor align, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        rt.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        return t;
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    // Полоска, заполненная слева на долю v (без спрайтов, через якоря)
    private static void FillRect(RectTransform rt, float v)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
