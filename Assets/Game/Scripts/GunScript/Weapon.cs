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
//   PlayerCam (Camera + Movment)
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
    public Movment cameraLook;              // скрипт мыши на PlayerCam; найдётся сам
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
    private int currentAmmo;
    private bool isReloading;
    private bool isCycling;
    private bool manualChambered = true;
    private Coroutine reloadRoutine;

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
    public AudioClip fireSound;
    public AudioClip reloadSound;
    public AudioClip emptySound;

    [Header("Bullet holes (decals)")]
    public GameObject bulletHolePrefab;
    public bool flipDecalNormal = false;
    public float decalOffset = 0.01f;
    public float decalLifetime = 20f;       // 0 = никогда не удалять по времени
    public int maxDecals = 60;
    public LayerMask decalIgnoreLayers;
    private static readonly Queue<GameObject> spawnedDecals = new Queue<GameObject>();

    [Header("Animation hooks (если есть Animator)")]
    public Animator animator;
    public string fireTrigger = "Fire";
    public string reloadTrigger = "Reload";
    public string cycleTrigger = "Cycle";
    public string emptyTrigger = "Empty";

    public int CurrentAmmo => currentAmmo;
    public int ReserveAmmo => reserveAmmo;
    public int MagazineSize => magazineSize;
    public int MaxReserveAmmo => maxReserveAmmo;
    public bool InfiniteReserve => infiniteReserve;
    public bool IsReloading => isReloading;

    private float FireCooldown => 60f / Mathf.Max(1f, roundsPerMinute);

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    void Awake()
    {
        currentAmmo = magazineSize;

        if (owner == null) owner = transform.root;
        if (aimOrigin == null)
        {
            Camera cam = GetComponentInParent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam != null) aimOrigin = cam.transform;
        }
        if (cameraLook == null) cameraLook = GetComponentInParent<Movment>();
        if (playerBody == null && owner != null) playerBody = owner.GetComponent<Rigidbody>();

        if (recoilTransform != null)
        {
            kickRestPos = recoilTransform.localPosition;
            kickRestRot = recoilTransform.localRotation;
        }
    }

    // Переключение оружия выключает объект -- прерываем перезарядку/передёргивание,
    // иначе корутина умрёт на середине и isReloading залипнет в true.
    void OnDisable()
    {
        StopAllCoroutines();
        reloadRoutine = null;
        isReloading = false;
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
        HandleInput();
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
        if (isReloading && reloadOneByOne && wantsToFire && currentAmmo > 0)
            CancelReload();

        if (isReloading) return;

        if (Input.GetKeyDown(reloadKey))
            TryStartReload();

        if (fireMode == FireMode.Manual && Input.GetKeyDown(manualCycleKey) && !manualChambered && !isCycling)
            StartCoroutine(CycleRoutine());

        if (wantsToFire)
            TryFire();
    }

    private void TryFire()
    {
        if (isReloading || isCycling || Time.time < nextFireTime) return;

        if (currentAmmo <= 0)
        {
            PlayEmptyFeedback();
            if (autoReloadWhenEmpty) TryStartReload();
            return;
        }

        if (fireMode == FireMode.Manual && !manualChambered)
        {
            PlayEmptyFeedback();
            return;
        }

        Fire();
    }

    private void Fire()
    {
        nextFireTime = Time.time + FireCooldown;
        currentAmmo--;

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

        if (currentAmmo <= 0 && autoReloadWhenEmpty)
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
            if (damageable != null)
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

        Vector3 normal = flipDecalNormal ? -hit.normal : hit.normal;
        Quaternion rot = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        Vector3 pos = hit.point + hit.normal * decalOffset;

        GameObject decal = Instantiate(bulletHolePrefab, pos, rot, hit.collider.transform);

        spawnedDecals.Enqueue(decal);
        while (spawnedDecals.Count > maxDecals)
        {
            GameObject oldest = spawnedDecals.Dequeue();
            if (oldest != null) Destroy(oldest);
        }

        if (decalLifetime > 0f)
            Destroy(decal, decalLifetime);
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

        Quaternion tiltTarget = isReloading ? Quaternion.Euler(reloadTiltEuler) : Quaternion.identity;
        tiltCurrent = Quaternion.Slerp(tiltCurrent, tiltTarget, Time.deltaTime * reloadTiltSpeed);

        if (recoilTransform == null) return;
        recoilTransform.localPosition = kickRestPos + new Vector3(0f, 0f, -kickCurrent.z);
        recoilTransform.localRotation = kickRestRot * tiltCurrent * Quaternion.Euler(kickCurrent.x, kickCurrent.y, 0f);
    }

    private void TryStartReload()
    {
        if (isReloading || currentAmmo >= magazineSize) return;
        if (!infiniteReserve && reserveAmmo <= 0) return;
        reloadRoutine = StartCoroutine(ReloadRoutine());
    }

    private void CancelReload()
    {
        if (reloadRoutine != null) StopCoroutine(reloadRoutine);
        reloadRoutine = null;
        isReloading = false;
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        if (animator != null)
            animator.SetTrigger(reloadTrigger);

        if (reloadOneByOne)
        {
            while (currentAmmo < magazineSize && (infiniteReserve || reserveAmmo > 0))
            {
                if (audioSource != null && reloadSound != null)
                    audioSource.PlayOneShot(reloadSound);
                yield return new WaitForSeconds(reloadTime);
                currentAmmo++;
                if (!infiniteReserve) reserveAmmo--;
            }
        }
        else
        {
            if (audioSource != null && reloadSound != null)
                audioSource.PlayOneShot(reloadSound);
            yield return new WaitForSeconds(reloadTime);

            int needed = magazineSize - currentAmmo;
            int toLoad = infiniteReserve ? needed : Mathf.Min(needed, reserveAmmo);
            currentAmmo += toLoad;
            if (!infiniteReserve) reserveAmmo -= toLoad;
        }

        if (fireMode == FireMode.Manual)
            manualChambered = true;

        isReloading = false;
        reloadRoutine = null;
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
    public int AddAmmo(int amount)
    {
        if (infiniteReserve || amount <= 0) return 0;
        int take = Mathf.Min(amount, Mathf.Max(0, maxReserveAmmo - reserveAmmo));
        reserveAmmo += take;
        return take;
    }

    private void PlayFireFeedback()
    {
        if (animator != null)
            animator.SetTrigger(fireTrigger);

        if (audioSource != null && fireSound != null)
            audioSource.PlayOneShot(fireSound);

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
        if (audioSource != null && emptySound != null)
            audioSource.PlayOneShot(emptySound);
    }
}
