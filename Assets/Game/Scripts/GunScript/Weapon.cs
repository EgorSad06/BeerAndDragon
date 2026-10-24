using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Минимальный интерфейс урона -- повесь TakeDamage на любой скрипт здоровья
// (врага, разрушаемого объекта и т.д.), и Weapon/Sword сами его найдут через GetComponentInParent.
public interface IDamageable
{
    void TakeDamage(float amount);
}

// Универсальное огнестрельное оружие: одним компонентом + разным набором значений в инспекторе
// можно сделать и дробовик, и мушкет, и пистолет.
//
// - FireMode.Automatic   -- держишь fireKey, стреляет с fireRate пока держишь.
// - FireMode.SemiAuto    -- один клик fireKey = один выстрел.
// - FireMode.Manual      -- один клик = один выстрел, но после него нужно
//                            передёрнуть (manualCycleKey), пока не передёрнул -- не выстрелит.
//                            Можно включить autoCycleAfterFire, чтобы передёргивание
//                            происходило само по таймеру, без отдельной кнопки.
//
// Иерархия, под которую рассчитан скрипт (её собирает Tools > BeerAndDragon > Setup Player Weapons):
//   PlayerCam (Camera + CameraPOV)
//     WeaponHolder (WeaponSwitcher, WeaponSway)
//       Shotgun (Weapon)
//         Kick        <- recoilTransform, дёргается при выстреле и наклоняется при перезарядке
//           Model
//           Muzzle    <- muzzlePoint
public class Weapon : MonoBehaviour
{
    public enum FireMode { Manual, SemiAuto, Automatic }

    [Header("Fire Mode")]
    public FireMode fireMode = FireMode.SemiAuto;
    public KeyCode fireKey = KeyCode.Mouse0;
    public KeyCode manualCycleKey = KeyCode.Q;
    public float manualCycleDuration = 0.35f;
    public bool autoCycleAfterFire = false;

    [Header("Fire Rate")]
    public float roundsPerMinute = 600f;    // скорострельность, из неё считается кулдаун между выстрелами
    private float nextFireTime;

    [Header("Shot")]
    public int pelletsPerShot = 1;          // >1 -- дробовик (несколько дробин за выстрел)
    public float damage = 20f;              // урон ОДНОЙ дробины
    public float range = 100f;
    public LayerMask hitMask = ~0;
    public float hitForce = 4f;             // толчок Rigidbody, в который попали (на каждую дробину)

    [Header("Damage falloff / падение урона с дистанцией")]
    public float falloffStart = 10f;        // до этой дистанции полный урон
    public float falloffEnd = 30f;          // после этой -- minDamageMultiplier
    [Range(0f, 1f)] public float minDamageMultiplier = 0.2f;

    [Header("Spread / разброс")]
    public float baseSpreadAngle = 0.5f;    // конус разброса в покое, градусы
    public float maxSpreadAngle = 6f;       // предел разброса при долгой стрельбе
    public float spreadPerShot = 0.6f;      // насколько растёт разброс за выстрел
    public float spreadRecoverySpeed = 5f;  // восстановление разброса, град/сек
    private float currentSpreadBloom;

    [Header("Recoil: камера")]
    public CameraPOV cameraLook;              // скрипт мыши на PlayerCam; найдётся сам
    public float recoilKickPitch = 3f;      // дёрг камеры вверх за выстрел, градусы
    public Vector2 recoilKickYawRange = new Vector2(-1f, 1f);

    [Header("Recoil: модель оружия (view kick)")]
    public Transform recoilTransform;       // дочерний пивот модели ("Kick")
    public float viewKickBack = 0.08f;      // на сколько модель отъезжает назад, м
    public float viewKickPitch = 12f;       // на сколько задирается ствол, градусы
    public float viewKickSnappiness = 30f;
    public float viewKickReturnSpeed = 8f;
    public Vector3 reloadTiltEuler = new Vector3(35f, -15f, 25f); // поза во время перезарядки
    public float reloadTiltSpeed = 10f;
    private Vector3 kickCurrent, kickTarget;   // x,y,z = pitch, yaw, back
    private Vector3 kickRestPos;
    private Quaternion kickRestRot;
    private Quaternion tiltCurrent = Quaternion.identity;

    [Header("Self knockback / отдача в игрока")]
    public Rigidbody playerBody;            // найдётся сам (Rigidbody на корне игрока)
    public float selfKnockback = 0f;        // >0 -- выстрел толкает игрока назад от прицела (shotgun jump)

    [Header("Magazine / Reload")]
    public string ammoType = "shells";      // коробки AmmoPickup с таким же типом пополняют запас
    public int magazineSize = 30;
    public int reserveAmmo = 90;
    public int maxReserveAmmo = 180;        // больше этого с коробок не наберёшь
    public bool infiniteReserve = false;    // бесконечный запас, как в ультракилле
    public float reloadTime = 1.8f;
    public bool reloadOneByOne = false;     // заряжать по одному патрону (помпа); выстрел прерывает
    public bool autoReloadWhenEmpty = true;
    public KeyCode reloadKey = KeyCode.R;
    private bool isCycling;
    private bool manualChambered = true;

    // Правила магазина и запаса -- в AmmoMagazine (чистый C#, тестируется без сцены). Поля выше -- только настройки;
    // магазин создаётся из них при первом обращении (в EditMode-тестах Awake не вызывается).
    private AmmoMagazine ammo;
    private AmmoMagazine Ammo => ammo ??= new AmmoMagazine(magazineSize, reserveAmmo, maxReserveAmmo, reloadTime,
                                                           infiniteReserve, reloadOneByOne);

    [Header("Aim / Muzzle")]
    public Transform aimOrigin;             // камера; если пусто -- найдётся сама
    public Transform muzzlePoint;           // дуло -- для вспышки и трассеров
    public Transform owner;                 // корень игрока, по нему не попадаем; пусто = transform.root

    [Header("Tracers")]
    public bool drawTracers = false;
    public Material tracerMaterial;
    public Color tracerColor = new Color(1f, 0.85f, 0.4f, 1f);
    public float tracerWidth = 0.02f;
    public float tracerLifetime = 0.06f;

    [Header("VFX / SFX (можно оставить пустым)")]
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashLifetime = 0.1f;
    public GameObject impactPrefab;
    public float impactLifetime = 1f;
    public AudioSource audioSource;
    public AudioClip fireSound;             // не назначен -- выстрел генерируется в коде (WeaponSounds)
    [Range(0f, 1f)] public float fireVolume = 0.8f;   // выстрел чуть тише записи
    public AudioClip reloadSound;           // вставка одного патрона: звучит на каждый патрон; не назначен --
                                            // вся перезарядка одним сгенерированным звуком (WeaponSounds)
    public AudioClip emptySound;            // не назначен -- щелчок генерируется в коде (FR-WPN-03.1)

    [Header("Bullet holes (decals)")]
    public GameObject bulletHolePrefab;
    public bool flipDecalNormal = false;
    public float decalOffset = 0.01f;
    public float decalLifetime = 20f;       // 0 = никогда не удалять по времени
    public int maxDecals = 60;
    public LayerMask decalIgnoreLayers;
    private BulletHoles bulletHoles;          // потребитель пула; реализацию пула выбирает SetupBulletHoles

    /// <summary>Дырки от пуль этого оружия (null, если префаб не назначен). Для диагностики.</summary>
    public BulletHoles BulletHoles => bulletHoles;

    [Header("Animation hooks (если есть Animator)")]
    public Animator animator;
    public string fireTrigger = "Fire";
    public string reloadTrigger = "Reload";
    public string cycleTrigger = "Cycle";
    public string emptyTrigger = "Empty";

    public int CurrentAmmo => Ammo.Magazine;
    public int ReserveAmmo => Ammo.Reserve;
    public int MagazineSize => magazineSize;
    public int MaxReserveAmmo => maxReserveAmmo;
    public bool InfiniteReserve => infiniteReserve;
    public bool IsReloading => Ammo.IsReloading;

    private float FireCooldown => 60f / Mathf.Max(1f, roundsPerMinute);

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    void Awake()
    {
        // FR-WPN-04.1: неверные настройки магазина -- ошибка в журнал, оружие выключено, игра продолжается
        try { ammo = new AmmoMagazine(magazineSize, reserveAmmo, maxReserveAmmo, reloadTime, infiniteReserve, reloadOneByOne); }
        catch (System.ArgumentOutOfRangeException e)
        {
            GameLog.Error("Weapon", $"{name}: {e.Message}");
            enabled = false;
            return;
        }

        if (owner == null) owner = transform.root;
        if (aimOrigin == null)
        {
            Camera cam = GetComponentInParent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam != null) aimOrigin = cam.transform;
        }
        if (cameraLook == null) cameraLook = GetComponentInParent<CameraPOV>();
        if (playerBody == null && owner != null) playerBody = owner.GetComponent<Rigidbody>();

        if (recoilTransform != null)
        {
            kickRestPos = recoilTransform.localPosition;
            kickRestRot = recoilTransform.localRotation;
        }
        GameLog.Verbose("Weapon", $"{name}: Awake, пул декалей...");
        SetupBulletHoles();
    }

    void Start()
    {
        // -decalProbe N: воспроизводимый прогон пула декалей (модуль-кандидат ПЗ-2)
        if (bulletHoles != null) BulletHoleProbe.RunIfRequested(bulletHoles, System.Environment.GetCommandLineArgs());
    }

    // Точка сборки: реализация пула декалей -- из конфигурации (decalPool), потребитель BulletHoles получает
    // только IObjectPool<GameObject>. Декали создаются заранее (Prewarm), чтобы в бою не было Instantiate.
    private void SetupBulletHoles()
    {
        if (bulletHolePrefab == null || maxDecals <= 0) return;

        Transform root = new GameObject("[BulletHoles] " + name).transform;
        IObjectPool<GameObject> pool = ObjectPoolFactory.Create(GameConfig.DecalPoolKind,
            () =>
            {
                GameObject go = Instantiate(bulletHolePrefab, root);
                go.SetActive(false);
                return go;
            },
            maxDecals, PoolOverflow.ReuseOldest,
            onGet: go => go.SetActive(true),
            onRelease: go => go.SetActive(false));
        GameLog.Verbose("Weapon", $"{name}: заготовка {maxDecals} декалей");
        pool.Prewarm(maxDecals);
        bulletHoles = new BulletHoles(pool, decalOffset, decalLifetime, flipDecalNormal);
        GameLog.Verbose("Weapon", $"{name}: пул декалей {pool.GetType().Name}, лимит {maxDecals}");
    }

    // Переключение оружия выключает объект -- прерываем перезарядку/передёргивание,
    // иначе перезарядка продолжилась бы в выключенном оружии.
    void OnDisable()
    {
        StopAllCoroutines();
        Ammo.CancelReload();
        if (isCycling) { isCycling = false; manualChambered = true; }
        kickCurrent = kickTarget = Vector3.zero;
        tiltCurrent = Quaternion.identity;
        if (recoilTransform != null)
        {
            recoilTransform.localPosition = kickRestPos;
            recoilTransform.localRotation = kickRestRot;
        }
    }

    void Update()
    {
        TickReload();
        HandleInput();
        bulletHoles?.Tick(Time.time);
        currentSpreadBloom = Mathf.Max(0f, currentSpreadBloom - spreadRecoverySpeed * Time.deltaTime);
        UpdateViewKick();
    }

    private void HandleInput()
    {
        if (PlayerInputLock.Locked) return;

        bool wantsToFire = fireMode == FireMode.Automatic
            ? Input.GetKey(fireKey)
            : Input.GetKeyDown(fireKey);

        // Помпу можно прервать выстрелом, если хоть один патрон уже зарядили
        if (Ammo.IsReloading && reloadOneByOne && wantsToFire && Ammo.Magazine > 0)
            Ammo.CancelReload();

        if (Ammo.IsReloading) return;

        if (Input.GetKeyDown(reloadKey))
            TryStartReload();

        if (fireMode == FireMode.Manual && Input.GetKeyDown(manualCycleKey) && !manualChambered && !isCycling)
            StartCoroutine(CycleRoutine());

        if (wantsToFire)
            TryFire();
    }

    private void TryFire()
    {
        if (isCycling || Time.time < nextFireTime) return;

        if (fireMode == FireMode.Manual && !manualChambered && Ammo.Magazine > 0 && !Ammo.IsReloading)
        {
            PlayEmptyFeedback();
            return;
        }

        switch (Ammo.TryFire())
        {
            case FireResult.Blocked:
                return;
            case FireResult.Empty:
                nextFireTime = Time.time + EmptyClickInterval; // в автоматическом режиме не щёлкать каждый кадр
                PlayEmptyFeedback();
                if (autoReloadWhenEmpty) TryStartReload();
                return;
        }

        Fire();
    }

    // Патрон уже списан в Ammo.TryFire
    private void Fire()
    {
        nextFireTime = Time.time + FireCooldown;

        if (fireMode == FireMode.Manual)
        {
            manualChambered = false;
            if (autoCycleAfterFire)
                StartCoroutine(CycleRoutine());
        }

        currentSpreadBloom = Mathf.Min(currentSpreadBloom + spreadPerShot, maxSpreadAngle);

        Vector3 origin = aimOrigin != null ? aimOrigin.position : transform.position;
        Vector3 forward = aimOrigin != null ? aimOrigin.forward : transform.forward;

        for (int i = 0; i < Mathf.Max(1, pelletsPerShot); i++)
        {
            Vector3 dir = SpreadDirection(forward, baseSpreadAngle + currentSpreadBloom);
            FireSinglePellet(origin, dir);
        }

        ApplyRecoil(forward);
        PlayFireFeedback();

        if (Ammo.Magazine <= 0 && autoReloadWhenEmpty)
            TryStartReload();
    }

    // Случайное направление внутри конуса вокруг forward радиусом coneAngle (градусы).
    // sqrt даёт равномерное распределение по площади круга, а не кучку в центре.
    public static Vector3 SpreadDirection(Vector3 forward, float coneAngle)
    {
        if (coneAngle <= 0f) return forward;

        float angle = Mathf.Sqrt(Random.value) * coneAngle * Mathf.Deg2Rad;
        float rotation = Random.Range(0f, 360f) * Mathf.Deg2Rad;

        float x = Mathf.Sin(angle) * Mathf.Cos(rotation);
        float y = Mathf.Sin(angle) * Mathf.Sin(rotation);
        float z = Mathf.Cos(angle);

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f) up = Vector3.right;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector3 trueUp = Vector3.Cross(forward, right).normalized;

        return (right * x + trueUp * y + forward * z).normalized;
    }

    // Множитель урона от дистанции: до start -- 1, после end -- minMultiplier, между -- линейно
    public const float EmptyClickInterval = 0.2f;

    private static AudioClip defaultEmptyClick;
    private static AudioClip DefaultEmptyClick => defaultEmptyClick != null ? defaultEmptyClick : (defaultEmptyClick = CreateEmptyClick());

    // Щелчок пустого оружия: 50 мс -- удар бойка (короткий шум) + металлический призвук 2,2 кГц,
    // оба быстро затухают. Шум с фиксированным seed, чтобы звук был одинаковым при каждом запуске.
    public static AudioClip CreateEmptyClick(int sampleRate = 44100)
    {
        int length = Mathf.Max(1, sampleRate / 20);
        float[] data = new float[length];
        System.Random rng = new System.Random(7);
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 900f);
            float ring = Mathf.Sin(2f * Mathf.PI * 2200f * t) * Mathf.Exp(-t * 120f);
            data[i] = Mathf.Clamp(0.6f * noise + 0.35f * ring, -1f, 1f);
        }
        AudioClip clip = AudioClip.Create("EmptyClick", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public static float FalloffMultiplier(float distance, float start, float end, float minMultiplier)
    {
        if (end <= start) return 1f;
        return Mathf.Lerp(1f, minMultiplier, Mathf.InverseLerp(start, end, distance));
    }

    private void FireSinglePellet(Vector3 origin, Vector3 dir)
    {
        Vector3 endPoint = origin + dir * range;

        if (RaycastIgnoringOwner(origin, dir, out RaycastHit hit))
        {
            endPoint = hit.point;

            float mult = FalloffMultiplier(hit.distance, falloffStart, falloffEnd, minDamageMultiplier);

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable is Damageable target)
                target.TakeHit(damage * mult, hit.point, dir * hitForce * mult);
            else if (damageable != null)
                damageable.TakeDamage(damage * mult);

            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(dir * hitForce * mult, hit.point, ForceMode.Impulse);

            if (impactPrefab != null)
                Destroy(Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal)), impactLifetime);

            SpawnBulletHole(hit);
        }

        if (drawTracers)
            SpawnTracer(muzzlePoint != null ? muzzlePoint.position : origin, endPoint);
    }

    // Обычный Raycast мог попасть в коллайдер самого игрока -- берём все попадания и
    // выбираем ближайшее, которое не принадлежит owner.
    private bool RaycastIgnoringOwner(Vector3 origin, Vector3 dir, out RaycastHit best)
    {
        int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, hitMask, QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (owner != null && h.transform.IsChildOf(owner)) continue;
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = h;
            }
        }
        return bestDist < float.MaxValue;
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        if (tracerMaterial == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null) { drawTracers = false; return; }
            tracerMaterial = new Material(s);
        }

        GameObject go = new GameObject("Tracer");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = tracerMaterial;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = tracerWidth;
        lr.endWidth = tracerWidth * 0.3f;
        lr.startColor = tracerColor;
        lr.endColor = tracerColor;
        Destroy(go, tracerLifetime);
    }

    private void SpawnBulletHole(RaycastHit hit)
    {
        if (bulletHolePrefab == null) return;
        if (((1 << hit.collider.gameObject.layer) & decalIgnoreLayers) != 0) return;
        if (hit.rigidbody != null) return; // на летающих ящиках дырки выглядят странно

        bulletHoles?.Place(hit.point, hit.normal, Random.Range(0f, 360f), Time.time);
    }

    private void ApplyRecoil(Vector3 aimDir)
    {
        if (cameraLook != null)
            cameraLook.AddRecoil(recoilKickPitch, Random.Range(recoilKickYawRange.x, recoilKickYawRange.y));

        kickTarget += new Vector3(-viewKickPitch, Random.Range(-2f, 2f), viewKickBack);

        if (playerBody != null && selfKnockback > 0f)
            playerBody.AddForce(-aimDir * selfKnockback, ForceMode.VelocityChange);
    }

    private void UpdateViewKick()
    {
        kickCurrent = Vector3.Lerp(kickCurrent, kickTarget, Time.deltaTime * viewKickSnappiness);
        kickTarget = Vector3.Lerp(kickTarget, Vector3.zero, Time.deltaTime * viewKickReturnSpeed);

        Quaternion tiltTarget = Ammo.IsReloading ? Quaternion.Euler(reloadTiltEuler) : Quaternion.identity;
        tiltCurrent = Quaternion.Slerp(tiltCurrent, tiltTarget, Time.deltaTime * reloadTiltSpeed);

        if (recoilTransform == null) return;
        recoilTransform.localPosition = kickRestPos + new Vector3(0f, 0f, -kickCurrent.z);
        recoilTransform.localRotation = kickRestRot * tiltCurrent * Quaternion.Euler(kickCurrent.x, kickCurrent.y, 0f);
    }

    private void TryStartReload()
    {
        if (!Ammo.TryStartReload(Time.time)) return;
        if (animator != null)
            animator.SetTrigger(reloadTrigger);
        if (reloadSound != null && !reloadOneByOne) StartCoroutine(ShellSoundsRoutine(ShellsToLoad()));
        else PlayReloadSound();
    }

    // Сколько патронов войдёт за эту перезарядку (до её завершения)
    private int ShellsToLoad()
    {
        int room = Ammo.MagazineSize - Ammo.Magazine;
        return Mathf.Max(1, Ammo.InfiniteReserve ? room : Mathf.Min(room, Ammo.Reserve));
    }

    // Обычная перезарядка (все патроны разом): звук вставки на каждый патрон, равномерно за reloadTime
    private IEnumerator ShellSoundsRoutine(int shells)
    {
        float step = reloadTime / shells;
        for (int i = 0; i < shells && Ammo.IsReloading; i++)
        {
            PlayReloadSound();
            yield return new WaitForSeconds(step);
        }
    }

    private void TickReload()
    {
        int loaded = Ammo.Tick(Time.time);
        if (loaded <= 0) return;
        if (Ammo.IsReloading) PlayReloadSound();          // помпа: звук на каждый следующий патрон
        else if (fireMode == FireMode.Manual) manualChambered = true;
    }

    private void PlayReloadSound()
    {
        AudioClip clip = reloadSound != null ? reloadSound : (Application.isBatchMode ? null : WeaponSounds.Reload);
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private IEnumerator CycleRoutine()
    {
        isCycling = true;
        if (animator != null)
            animator.SetTrigger(cycleTrigger);
        yield return new WaitForSeconds(manualCycleDuration);
        manualChambered = true;
        isCycling = false;
    }

    // Возвращает, сколько патронов реально взяли (упёрлись в maxReserveAmmo -- меньше)
    public int AddAmmo(int amount) => Ammo.AddAmmo(amount);

    private void PlayFireFeedback()
    {
        if (animator != null)
            animator.SetTrigger(fireTrigger);

        // Звук не назначен -- сгенерированный (WeaponSounds), в batchmode без звука
        AudioClip shot = fireSound != null ? fireSound : (Application.isBatchMode ? null : WeaponSounds.RandomShot());
        if (audioSource != null && shot != null)
            audioSource.PlayOneShot(shot, fireVolume);

        if (muzzleFlashPrefab != null && muzzlePoint != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);
            Destroy(flash, muzzleFlashLifetime);
        }
    }

    private void PlayEmptyFeedback()
    {
        if (animator != null)
            animator.SetTrigger(emptyTrigger);
        if (audioSource == null) return;
        // Щелчок создаётся при первом использовании, а не в Awake: в batchmode (CI, без аудиоустройства) он не нужен
        if (emptySound == null && !Application.isBatchMode) emptySound = DefaultEmptyClick;
        if (emptySound != null) audioSource.PlayOneShot(emptySound);
    }
}
