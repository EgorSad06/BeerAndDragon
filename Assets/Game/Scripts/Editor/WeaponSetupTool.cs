using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using G = GeneratedAssets;

// Tools > BeerAndDragon > Setup Player
//   Собирает всё на игроке: оружие под камерой (дробовик + меч), инвентарь, здоровье,
//   эффекты, HUD. Генерирует иконки, предметы и префабы подбираемых штук.
//   Можно запускать повторно -- WeaponHolder и HUD пересоздаются.
// Tools > BeerAndDragon > Spawn Test Stuff
//   Ставит перед игроком манекены, ящики, сигареты, энергетики, патроны и колючки.
public static class WeaponSetupTool
{
    const string ShotgunDir = "Assets/Game/Assets/guns/PSXShotgunPack[FIXED]/PSXShotgunPack[FIXED]";
    const string ShotgunFbx = ShotgunDir + "/Files/DoubleBarrelShotgun/Fbx/DoubleBarrelShotgun.fbx";
    const string ShotgunTex = ShotgunDir + "/Files/DoubleBarrelShotgun/Textures/DBShotgunText.png";
    const string ShellTex = ShotgunDir + "/Files/DoubleBarrelShotgun/Textures/shotgunShellTexture.jpg";
    const string BulletHoleTex = "Assets/Game/Assets/guns/tuff/bullet_holl.png";

    const string CigDir = "Assets/Game/Assets/things/PSXCigarette Pack[FIXED]";
    const string CigPackFbx = CigDir + "/CigarettePack/CigarettePacks/PackCigarettesStandard.fbx";
    const string CigPackTex = CigDir + "/Textures/packFrontTextureLowPoly.png";

    // Если положишь сюда FBX банки -- возьмётся он вместо сгенерированной
    const string MonsterFbx = "Assets/Game/Assets/things/Monster/Monster.fbx";

    const float ShotgunLength = 0.7f;

    class Items
    {
        public ItemDefinition shotgun, sword, cigarettes, monster;
        public Sprite ammoIcon;
    }

    [MenuItem("Tools/BeerAndDragon/Setup Player")]
    public static void SetupPlayer()
    {
        KnightMovement player = Object.FindFirstObjectByType<KnightMovement>();
        CameraPOV look = Object.FindFirstObjectByType<CameraPOV>();
        if (player == null || look == null)
        {
            EditorUtility.DisplayDialog("Setup Player", "Не нашёл KnightMovement или CameraPOV (скрипт камеры) в открытой сцене.", "Ок");
            return;
        }

        G.EnsureFolder(G.Dir);
        G.EnsureFolder(G.IconsDir);
        G.EnsureFolder(G.PrefabsDir);
        G.EnsureFolder(G.ItemsDir);

        Items items = CreateItems();
        AudioSource audio = SetupWeapons(player, look);
        SetupPlayerComponents(player, items, audio);
        SetupHud(items);
        BuildPickupPrefabs(items);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[Setup] Готово. 1..9 -- хотбар, Tab -- инвентарь, R -- перезарядка. Сохрани сцену (Ctrl+S). " +
                  "Тестовые предметы: Tools > BeerAndDragon > Spawn Test Stuff");
    }

    // ---------------- Items ----------------

    static Items CreateItems()
    {
        Items it = new Items();

        Sprite shotgunIcon = G.IconFromImage(ShotgunDir + "/Images/DBSG.png", new RectInt(125, 175, 805, 140), "Icon_Shotgun");
        Sprite cigIcon = G.IconFromImage(CigDir + "/Images/Pack.png", new RectInt(95, 100, 490, 690), "Icon_Cigarettes");
        it.ammoIcon = G.IconFromImage(ShotgunDir + "/Images/Shells.png", new RectInt(300, 70, 130, 215), "Icon_Shells");

        it.shotgun = Item("Shotgun", i =>
        {
            i.displayName = "Двустволка";
            i.description = "2 патрона в стволах. Выстрел под ноги подбрасывает вверх.";
            i.kind = ItemDefinition.ItemKind.Weapon;
            i.weaponObjectName = "Shotgun";
            i.icon = shotgunIcon;
        });
        it.sword = Item("Sword", i =>
        {
            i.displayName = "Меч";
            i.description = "Зажми ЛКМ -- тяжёлый удар. Удар вниз в воздухе -- пого-прыжок.";
            i.kind = ItemDefinition.ItemKind.Weapon;
            i.weaponObjectName = "Sword";
            i.icon = G.SwordIcon();
        });
        it.cigarettes = Item("Cigarettes", i =>
        {
            i.displayName = "Сигареты";
            i.description = "Минздрав предупреждал. +35 HP за 2 секунды.";
            i.kind = ItemDefinition.ItemKind.Consumable;
            i.effect = ItemDefinition.ConsumableEffect.Heal;
            i.amount = 35f;
            i.duration = 2f;
            i.maxStack = 5;
            i.icon = cigIcon;
        });
        it.monster = Item("MonsterEnergy", i =>
        {
            i.displayName = "Монстр";
            i.description = "Скорость x1.5 на 8 секунд. Стакается по времени.";
            i.kind = ItemDefinition.ItemKind.Consumable;
            i.effect = ItemDefinition.ConsumableEffect.SpeedBoost;
            i.amount = 1.5f;
            i.duration = 8f;
            i.maxStack = 3;
            i.icon = G.CanIcon();
        });
        return it;
    }

    static ItemDefinition Item(string file, System.Action<ItemDefinition> fill)
    {
        string path = G.ItemsDir + "/" + file + ".asset";
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            fill(item);
            AssetDatabase.CreateAsset(item, path);
        }
        else
        {
            fill(item);
            EditorUtility.SetDirty(item);
        }
        return item;
    }

    // ---------------- Weapons ----------------

    static AudioSource SetupWeapons(KnightMovement player, CameraPOV look)
    {
        Camera cam = look.GetComponent<Camera>();
        Transform camT = look.transform;

        // Старый Weapon без настроек висел прямо на игроке -- убираем, иначе он тоже стреляет.
        foreach (Weapon w in player.GetComponents<Weapon>())
            Undo.DestroyObjectImmediate(w);

        Transform old = camT.Find("WeaponHolder");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        if (cam != null && cam.nearClipPlane > 0.03f)
        {
            Undo.RecordObject(cam, "Near clip");
            cam.nearClipPlane = 0.03f; // иначе оружие у лица обрезается
        }

        GameObject holder = new GameObject("WeaponHolder");
        Undo.RegisterCreatedObjectUndo(holder, "Setup Weapons");
        holder.transform.SetParent(camT, false);
        holder.AddComponent<WeaponSwitcher>();
        holder.AddComponent<WeaponSway>();

        AudioSource audio = holder.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;

        BuildShotgun(holder.transform, camT, player, look, audio);
        BuildSword(holder.transform, camT, player, audio);
        return audio;
    }

    static void BuildShotgun(Transform holder, Transform camT, KnightMovement player, CameraPOV look, AudioSource audio)
    {
        Material gunMat = G.Material("M_DoubleBarrel", G.Lit, Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(ShotgunTex));
        Material shellMat = G.Material("M_Shell", G.Lit, Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(ShellTex));

        GameObject root = new GameObject("Shotgun");
        root.transform.SetParent(holder, false);
        root.transform.localPosition = new Vector3(0.22f, -0.22f, 0.35f);

        GameObject kick = new GameObject("Kick");
        kick.transform.SetParent(root.transform, false);

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(kick.transform, false);

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunFbx);
        if (fbx != null)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(kick.transform, false);

            // Патроны в модели висят в воздухе рядом с ружьём -- прячем
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t != model.transform && t.name.ToLower().Contains("shell"))
                    t.gameObject.SetActive(false);

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = (n.Contains("184") || n.Contains("185")) ? shellMat : gunMat;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            muzzle.transform.localPosition = FitShotgun(model.transform, kick.transform, ShotgunLength);
        }
        else
        {
            Debug.LogWarning("[Setup] Не нашёл модель дробовика: " + ShotgunFbx);
            muzzle.transform.localPosition = new Vector3(0f, 0f, ShotgunLength * 0.6f);
        }

        Weapon w = root.AddComponent<Weapon>();
        w.fireMode = Weapon.FireMode.SemiAuto;
        w.roundsPerMinute = 240f;
        w.pelletsPerShot = 12;
        w.damage = 9f;
        w.range = 60f;
        w.hitForce = 3f;
        w.falloffStart = 6f;
        w.falloffEnd = 25f;
        w.minDamageMultiplier = 0.25f;
        w.baseSpreadAngle = 6f;
        w.maxSpreadAngle = 2f;
        w.spreadPerShot = 0f;
        w.spreadRecoverySpeed = 10f;
        w.recoilKickPitch = 6f;
        w.recoilKickYawRange = new Vector2(-1.5f, 1.5f);
        w.recoilTransform = kick.transform;
        w.viewKickBack = 0.12f;
        w.viewKickPitch = 18f;
        w.selfKnockback = 6f;
        w.ammoType = "shells";
        w.magazineSize = 2;
        w.reserveAmmo = 16;
        w.maxReserveAmmo = 40;
        w.infiniteReserve = false;
        w.reloadTime = 0.9f;
        w.reloadOneByOne = false;
        w.autoReloadWhenEmpty = true;
        w.aimOrigin = camT;
        w.muzzlePoint = muzzle.transform;
        w.owner = player.transform;
        w.cameraLook = look;
        w.playerBody = player.GetComponent<Rigidbody>();
        w.drawTracers = false;
        w.muzzleFlashPrefab = G.MuzzleFlashPrefab();
        w.muzzleFlashLifetime = 0.05f;
        w.audioSource = audio;
        w.bulletHolePrefab = G.BulletHolePrefab(AssetDatabase.LoadAssetAtPath<Texture2D>(BulletHoleTex));
        w.flipDecalNormal = true; // у Quad лицевая сторона смотрит в -Z
        w.decalOffset = 0.01f;
        w.decalLifetime = 15f;
    }

    // Ставит двустволку стволами вперёд (+Z родителя), длиной targetLength.
    // Направление берём по дочерним объектам модели: Body (приклад) -> Barrel (стволы).
    // Возвращает позицию дула в координатах parent.
    static Vector3 FitShotgun(Transform model, Transform parent, float targetLength)
    {
        Transform barrel = FindChildContaining(model, "barrel");
        Transform body = FindChildContaining(model, "body");

        List<Vector3> pts = G.CollectVertices(model, model);
        Bounds b = G.BoundsOf(pts);
        Vector3 size = b.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        float len = size[axis];

        bool muzzleAtMax = true;
        if (barrel != null && body != null)
        {
            Bounds bb = G.BoundsOf(G.CollectVertices(barrel, model));
            Bounds bo = G.BoundsOf(G.CollectVertices(body, model));
            muzzleAtMax = bb.center[axis] > bo.center[axis];
        }
        else
        {
            Debug.LogWarning("[Setup] В модели дробовика нет Barrel/Body -- направление ствола может быть неверным. " +
                             "Если он смотрит назад -- поверни Shotgun/Kick/Model на 180 по Y.");
        }

        Vector3 dir = Vector3.zero;
        dir[axis] = muzzleAtMax ? 1f : -1f;
        Vector3 up = Vector3.zero;
        up[axis == 1 ? 2 : 1] = 1f;

        model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(dir, up));
        model.localScale = Vector3.one * (targetLength / Mathf.Max(0.0001f, len));
        model.localPosition = Vector3.zero;

        Bounds pb = G.BoundsOf(G.CollectVertices(model, parent));
        // Центрируем по X/Y, приклад немного за пивотом
        Vector3 shift = new Vector3(-pb.center.x, -pb.center.y, -(pb.min.z + targetLength * 0.35f));
        model.localPosition = shift;

        if (barrel != null)
        {
            Bounds bb = G.BoundsOf(G.CollectVertices(barrel, parent));
            return new Vector3(bb.center.x, bb.center.y, bb.max.z + 0.02f);
        }
        return new Vector3(0f, 0f, pb.max.z + shift.z);
    }

    static Transform FindChildContaining(Transform root, string part)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.ToLower().Contains(part)) return t;
        return null;
    }

    static void BuildSword(Transform holder, Transform camT, KnightMovement player, AudioSource audio)
    {
        Material steel = G.Material("M_SwordSteel", G.Lit, new Color(0.78f, 0.8f, 0.85f), null, 0.9f, 0.65f);
        Material gold = G.Material("M_SwordGold", G.Lit, new Color(0.85f, 0.62f, 0.2f), null, 1f, 0.5f);
        Material leather = G.Material("M_SwordLeather", G.Lit, new Color(0.3f, 0.16f, 0.08f), null, 0f, 0.15f);

        GameObject root = new GameObject("Sword");
        root.transform.SetParent(holder, false);
        root.transform.localPosition = new Vector3(0.28f, -0.32f, 0.4f);

        GameObject pivot = new GameObject("Pivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localRotation = Quaternion.Euler(35f, -10f, 20f);

        GameObject model = G.MeshObject("Model", pivot.transform, G.SwordMesh(), steel, gold, leather);
        model.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Sword s = root.AddComponent<Sword>();
        s.swingPivot = pivot.transform;
        s.aimOrigin = camT;
        s.owner = player.transform;
        s.playerBody = player.GetComponent<Rigidbody>();
        s.movement = player;
        s.audioSource = audio;
    }

    // ---------------- Player / HUD ----------------

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static void SetupPlayerComponents(KnightMovement player, Items items, AudioSource audio)
    {
        GameObject go = player.gameObject;
        GetOrAdd<PlayerHealth>(go);
        GetOrAdd<PlayerStatusEffects>(go);
        Inventory inv = GetOrAdd<Inventory>(go);

        Undo.RecordObject(inv, "Setup Inventory");
        inv.startingItems = new List<ItemStack>
        {
            new ItemStack { item = items.shotgun, count = 1 },
            new ItemStack { item = items.sword, count = 1 },
        };
        inv.audioSource = audio;
        inv.weaponSwitcher = go.GetComponentInChildren<WeaponSwitcher>(true);
        EditorUtility.SetDirty(inv);
    }

    static void SetupHud(Items items)
    {
        GameHUD hud = Object.FindFirstObjectByType<GameHUD>();
        if (hud == null)
        {
            GameObject go = new GameObject("GameHUD");
            Undo.RegisterCreatedObjectUndo(go, "Setup HUD");
            hud = go.AddComponent<GameHUD>();
        }
        Undo.RecordObject(hud, "Setup HUD");
        hud.ammoIcon = items.ammoIcon;
        EditorUtility.SetDirty(hud);
    }

    // ---------------- Pickups ----------------

    static void BuildPickupPrefabs(Items items)
    {
        // Сигареты
        {
            GameObject root = G.PickupBase("Pickup_Cigarettes", out Transform visual);
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CigPackFbx);
            if (fbx != null)
            {
                GameObject model = (GameObject)Object.Instantiate(fbx, visual);
                model.name = "Model";
                Material m = G.Material("M_CigPack", G.Lit, Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(CigPackTex));
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = m;
                    r.sharedMaterials = mats;
                }
                foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                G.FitToSize(model.transform, 0.35f);
            }
            ItemPickup p = root.AddComponent<ItemPickup>();
            p.item = items.cigarettes;
            p.count = 1;
            p.visual = visual;
            G.SavePrefab(root, "Pickup_Cigarettes");
        }

        // Энергетик
        {
            GameObject root = G.PickupBase("Pickup_Monster", out Transform visual);
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterFbx);
            if (fbx != null)
            {
                GameObject model = (GameObject)Object.Instantiate(fbx, visual);
                model.name = "Model";
                foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                G.FitToSize(model.transform, 0.4f);
            }
            else
            {
                Material body = G.Material("M_MonsterCan", G.Lit, Color.white, G.CanTexture(), 0.6f, 0.6f);
                Material metal = G.Material("M_CanMetal", G.Lit, new Color(0.75f, 0.76f, 0.8f), null, 1f, 0.7f);
                GameObject model = G.MeshObject("Model", visual, G.CanMesh(), body, metal);
                model.transform.localScale = Vector3.one * 2.5f;
                model.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            }
            ItemPickup p = root.AddComponent<ItemPickup>();
            p.item = items.monster;
            p.count = 1;
            p.visual = visual;
            G.SavePrefab(root, "Pickup_Monster");
        }

        // Коробка патронов
        {
            GameObject root = G.PickupBase("Pickup_Ammo", out Transform visual);
            Material wood = G.Material("M_AmmoWood", G.Lit, new Color(0.45f, 0.28f, 0.14f), null, 0f, 0.1f);
            Material red = G.Material("M_AmmoRed", G.Lit, new Color(0.75f, 0.1f, 0.08f), null, 0f, 0.3f);
            Material brass = G.Material("M_AmmoBrass", G.Lit, new Color(0.85f, 0.7f, 0.3f), null, 1f, 0.6f);
            GameObject model = G.MeshObject("Model", visual, G.AmmoBoxMesh(), wood, red, brass);
            model.transform.localScale = Vector3.one * 1.6f;
            model.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            AmmoPickup a = root.AddComponent<AmmoPickup>();
            a.ammoType = "shells";
            a.amount = 8;
            a.displayName = "патронов";
            a.visual = visual;
            G.SavePrefab(root, "Pickup_Ammo");
        }
    }

    // ---------------- Test stuff ----------------

    [MenuItem("Tools/BeerAndDragon/Spawn Test Stuff")]
    public static void SpawnTestStuff()
    {
        KnightMovement player = Object.FindFirstObjectByType<KnightMovement>();
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 fwd = player != null && player.orientation != null ? player.orientation.forward : Vector3.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        // Ставим на землю под точкой, если она есть
        Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }

        GameObject group = new GameObject("TestStuff");
        Undo.RegisterCreatedObjectUndo(group, "Spawn Test Stuff");

        for (int i = -1; i <= 1; i++)
        {
            GameObject d = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            d.name = "Dummy";
            d.transform.SetParent(group.transform);
            d.transform.position = Ground(origin + fwd * 9f + right * (i * 2.5f)) + Vector3.up * 1f;
            d.AddComponent<Damageable>().maxHealth = 150f;
            d.AddComponent<Rigidbody>().mass = 3f;
        }

        for (int i = 0; i < 4; i++)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Crate";
            c.transform.SetParent(group.transform);
            c.transform.localScale = Vector3.one * 0.6f;
            c.transform.position = Ground(origin + fwd * 6f + right * (i - 1.5f) * 0.8f) + Vector3.up * 0.3f;
            c.AddComponent<Damageable>().maxHealth = 40f;
            c.AddComponent<Rigidbody>();
        }

        void Place(string prefabName, Vector3 pos)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(G.PrefabsDir + "/" + prefabName + ".prefab");
            if (prefab == null) return;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            go.transform.position = Ground(pos);
        }

        Place("Pickup_Cigarettes", origin + fwd * 3f - right * 2f);
        Place("Pickup_Cigarettes", origin + fwd * 3f - right * 3f);
        Place("Pickup_Monster", origin + fwd * 3f + right * 2f);
        Place("Pickup_Monster", origin + fwd * 3f + right * 3f);
        Place("Pickup_Ammo", origin + fwd * 4.5f - right * 1f);
        Place("Pickup_Ammo", origin + fwd * 4.5f + right * 1f);

        // Колючки -- чтобы было обо что потерять здоровье и проверить сигареты
        GameObject spikes = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spikes.name = "Spikes (HurtZone)";
        spikes.transform.SetParent(group.transform);
        spikes.transform.localScale = new Vector3(1.5f, 0.3f, 1.5f);
        spikes.transform.position = Ground(origin - right * 4f) + Vector3.up * 0.15f;
        spikes.GetComponent<Renderer>().sharedMaterial = G.Material("M_Spikes", G.Lit, new Color(0.8f, 0.1f, 0.1f), null);
        spikes.AddComponent<HurtZone>();

        if (AssetDatabase.LoadAssetAtPath<GameObject>(G.PrefabsDir + "/Pickup_Ammo.prefab") == null)
            Debug.LogWarning("[Setup] Префабов подбираемых предметов нет -- сначала запусти Tools > BeerAndDragon > Setup Player");

        EditorSceneManager.MarkSceneDirty(group.scene);
    }
}
