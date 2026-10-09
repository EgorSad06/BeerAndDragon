using UnityEngine;

// Процедурные анимации рыцаря: кости двигаются кодом по состоянию игрока, без Animator.
// У рига нет позвоночника (Body / Head / руки / ноги), поэтому Humanoid-ретаргет не подходит,
// а процедурка зато сразу реагирует на всё: скорость, прыжки, стены, скейт, удары мечом.
//
// Все повороты задаются в пространстве персонажа (вперёд = +Z этого объекта),
// поэтому не важно, как оси костей выставлены в Блендере.
//
// Вешается на корень модели рыцаря (KnightVisual внутри Player/Model).
[DefaultExecutionOrder(200)]
public class KnightAnimator : MonoBehaviour
{
    [Header("Refs (найдутся сами)")]
    public Rigidbody body;
    public KnightMovement movement;
    public SkaterController skater;
    public WallClimb wallClimb;
    public WallRunSurface wallRun;
    public WeaponSwitcher weaponSwitcher;
    public PlayerHealth health;
    public ItemUser itemUser;
    public Transform viewCamera;

    [Header("Bones (найдутся по именам)")]
    public Transform boneBody;
    public Transform boneHead;
    public Transform upperArmL, lowerArmL, upperArmR, lowerArmR;
    public Transform upperLegL, lowerLegL, upperLegR, lowerLegR;
    public Transform handR;
    public Transform footL, footR;

    [Header("Skate IK (ноги на деке)")]
    [Range(0.2f, 0.9f)] public float stanceWidth = 0.55f;   // доля половины длины доски
    public float flipFootLift = 0.22f;

    public Transform HeadBone => boneHead;
    public Transform HandBone => handR;

    [Header("Feel")]
    public float poseSharpness = 12f;
    public float stepsPerMeter = 0.9f;      // частота шагов от скорости
    public float maxLegSwing = 45f;
    public float runLean = 1.2f;            // наклон вперёд, град на м/с
    public Vector3 armNeutralDirLeft = new Vector3(-0.25f, -1f, 0.05f);  // руки "по швам", а не в T-позе

    [Header("First person")]
    public bool hideInFirstPerson = true;   // в 1-м лице видна только тень (иначе шлем лезет в камеру)
    public float firstPersonDistance = 0.9f;

    // Поза: углы в градусах, "вперёд" положительно
    struct Pose
    {
        public float bodyPitch, bodyRoll, bodyYaw, bodyDrop, headPitch;
        public float legL, legR, kneeL, kneeR, legOutL, legOutR;
        public float armL, armR, armOutL, armOutR, elbowL, elbowR;

        public static Pose Lerp(Pose a, Pose b, float t)
        {
            Pose p;
            p.bodyPitch = Mathf.Lerp(a.bodyPitch, b.bodyPitch, t);
            p.bodyRoll = Mathf.Lerp(a.bodyRoll, b.bodyRoll, t);
            p.bodyYaw = Mathf.Lerp(a.bodyYaw, b.bodyYaw, t);
            p.bodyDrop = Mathf.Lerp(a.bodyDrop, b.bodyDrop, t);
            p.headPitch = Mathf.Lerp(a.headPitch, b.headPitch, t);
            p.legL = Mathf.Lerp(a.legL, b.legL, t);
            p.legR = Mathf.Lerp(a.legR, b.legR, t);
            p.kneeL = Mathf.Lerp(a.kneeL, b.kneeL, t);
            p.kneeR = Mathf.Lerp(a.kneeR, b.kneeR, t);
            p.legOutL = Mathf.Lerp(a.legOutL, b.legOutL, t);
            p.legOutR = Mathf.Lerp(a.legOutR, b.legOutR, t);
            p.armL = Mathf.Lerp(a.armL, b.armL, t);
            p.armR = Mathf.Lerp(a.armR, b.armR, t);
            p.armOutL = Mathf.Lerp(a.armOutL, b.armOutL, t);
            p.armOutR = Mathf.Lerp(a.armOutR, b.armOutR, t);
            p.elbowL = Mathf.Lerp(a.elbowL, b.elbowL, t);
            p.elbowR = Mathf.Lerp(a.elbowR, b.elbowR, t);
            return p;
        }
    }

    private Transform[] bones;
    private Quaternion[] restLocal;
    private Vector3 bodyRestLocalPos;
    private Quaternion armNeutralL = Quaternion.identity, armNeutralR = Quaternion.identity;
    private bool limbsFollowBody;
    private float ankleHeight = 0.08f;
    private float deckLift;

    private Pose current;
    private float phase;
    private float deathT;
    private Vector3 baseLocalPos;
    private Quaternion baseLocalRot;
    private Vector3 baseLocalScale;
    private Renderer[] renderers;
    private bool shadowOnly;
    private Sword sword;
    private Weapon gun;
    private Transform lastWeapon;

    void Awake()
    {
        Transform player = transform.root;
        if (body == null) body = player.GetComponent<Rigidbody>();
        if (movement == null) movement = player.GetComponent<KnightMovement>();
        if (skater == null) skater = player.GetComponent<SkaterController>();
        if (wallClimb == null) wallClimb = player.GetComponent<WallClimb>();
        if (wallRun == null) wallRun = player.GetComponent<WallRunSurface>();
        if (weaponSwitcher == null) weaponSwitcher = player.GetComponentInChildren<WeaponSwitcher>(true);
        if (health == null) health = player.GetComponent<PlayerHealth>();
        if (itemUser == null) itemUser = player.GetComponent<ItemUser>();
        if (viewCamera == null)
        {
            CameraPOV look = player.GetComponentInChildren<CameraPOV>();
            if (look != null) viewCamera = look.transform;
        }

        if (boneBody == null) boneBody = FindBone("Body");
        if (boneHead == null) boneHead = FindBone("Head");
        if (upperArmL == null) upperArmL = FindBone("UpperArm.l");
        if (lowerArmL == null) lowerArmL = FindBone("LowerArm.l");
        if (upperArmR == null) upperArmR = FindBone("UpperArm.r");
        if (lowerArmR == null) lowerArmR = FindBone("LowerArm.r");
        if (upperLegL == null) upperLegL = FindBone("UpperLeg.l");
        if (lowerLegL == null) lowerLegL = FindBone("LowerLeg.l");
        if (upperLegR == null) upperLegR = FindBone("UpperLeg.r");
        if (lowerLegR == null) lowerLegR = FindBone("LowerLeg.r");
        if (handR == null) handR = FindBone("Hand.r");
        if (footL == null) footL = FindBone("Foot.l");
        if (footR == null) footR = FindBone("Foot.r");

        bones = new[] { boneBody, boneHead, upperArmL, lowerArmL, upperArmR, lowerArmR, upperLegL, lowerLegL, upperLegR, lowerLegR };
        restLocal = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) restLocal[i] = bones[i].localRotation;
        if (boneBody != null) bodyRestLocalPos = boneBody.localPosition;

        limbsFollowBody = boneBody != null && upperLegL != null && upperLegL.IsChildOf(boneBody);

        // Руки в T-позе -> опускаем "по швам" (считаем один раз в пространстве персонажа)
        armNeutralL = NeutralFor(upperArmL, lowerArmL, armNeutralDirLeft);
        armNeutralR = NeutralFor(upperArmR, lowerArmR, new Vector3(-armNeutralDirLeft.x, armNeutralDirLeft.y, armNeutralDirLeft.z));

        // Высота щиколотки над подошвой -- чтобы ставить на деку подошву, а не кость
        Renderer firstRenderer = GetComponentInChildren<Renderer>();
        if (footL != null && firstRenderer != null)
            ankleHeight = Mathf.Clamp(footL.position.y - firstRenderer.bounds.min.y, 0.02f, 0.3f);

        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        baseLocalScale = transform.localScale;
        renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
    }

    private Transform FindBone(string boneName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    private Quaternion NeutralFor(Transform upper, Transform lower, Vector3 targetDirChar)
    {
        if (upper == null || lower == null) return Quaternion.identity;
        Vector3 restDir = transform.InverseTransformDirection(lower.position - upper.position).normalized;
        return Quaternion.FromToRotation(restDir, targetDirChar.normalized);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        KeepScaleAgainstParent();
        UpdateVisibility();

        Pose target = ItemUsePose(ComputeTargetPose(dt));
        current = Pose.Lerp(current, target, 1f - Mathf.Exp(-poseSharpness * dt));
        ApplyPose(current);
        ApplyDeath(dt);
    }

    // KnightMovement при приседании сжимает Model по Y -- рыцаря не плющим, приседание рисуем позой
    private void KeepScaleAgainstParent()
    {
        Transform p = transform.parent;
        if (p == null) return;
        Vector3 ps = p.localScale;
        if (Mathf.Approximately(ps.x, 0f) || Mathf.Approximately(ps.y, 0f) || Mathf.Approximately(ps.z, 0f)) return;
        transform.localScale = new Vector3(baseLocalScale.x / ps.x, baseLocalScale.y / ps.y, baseLocalScale.z / ps.z);
        float targetLift = skater != null ? skater.DeckLift : 0f;
        deckLift = Mathf.MoveTowards(deckLift, targetLift, Time.deltaTime * 2f);
        Vector3 pos = new Vector3(baseLocalPos.x / ps.x, (baseLocalPos.y + deckLift) / ps.y, baseLocalPos.z / ps.z);
        if (deathT <= 0f) transform.localPosition = pos;
    }

    private void UpdateVisibility()
    {
        if (!hideInFirstPerson || viewCamera == null || boneHead == null) return;
        bool fp = Vector3.Distance(viewCamera.position, boneHead.position) < firstPersonDistance && (health == null || !health.IsDead);
        if (fp == shadowOnly) return;
        shadowOnly = fp;
        foreach (Renderer r in renderers)
            if (r != null)
                r.shadowCastingMode = fp ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
    }

    // ---------------- Выбор позы ----------------

    private Pose ComputeTargetPose(float dt)
    {
        Pose p = default;
        float t = Time.time;

        Vector3 vel = body != null ? body.linearVelocity : Vector3.zero;
        Vector3 flat = Vector3.ProjectOnPlane(vel, Vector3.up);
        float speed = flat.magnitude;

        // Оружие в руках
        Transform w = weaponSwitcher != null ? weaponSwitcher.CurrentWeapon : null;
        if (w != lastWeapon)
        {
            lastWeapon = w;
            sword = w != null ? w.GetComponent<Sword>() : null;
            gun = w != null ? w.GetComponent<Weapon>() : null;
        }

        if (health != null && health.IsDead)
        {
            p.armOutL = 70f; p.armOutR = 70f; p.kneeL = 20f; p.kneeR = 10f; p.headPitch = -20f;
            return p;
        }

        if (skater != null && skater.IsRiding)
            return SkatePose(p, t, speed, vel);

        if (wallClimb != null && wallClimb.IsClinging)
        {
            // Висим на стене: руки вверх, ноги чуть согнуты
            p.armL = 150f; p.armR = 150f; p.armOutL = 15f; p.armOutR = 15f; p.elbowL = 20f; p.elbowR = 20f;
            p.legL = 15f; p.legR = -5f; p.kneeL = 40f; p.kneeR = 30f; p.headPitch = -25f;
            return p;
        }

        KnightMovement.MovementState state = movement != null ? movement.state : KnightMovement.MovementState.walking;
        bool grounded = movement == null || movement.IsGrounded();
        bool wallRunning = wallRun != null && wallRun.IsRunning;

        if (wallRunning)
        {
            p = RunCycle(p, Mathf.Max(speed, 8f), dt, 1.2f);
            p.bodyPitch += 8f;
            p.armOutL += 25f; p.armOutR += 25f;
        }
        else if (state == KnightMovement.MovementState.sliding)
        {
            // Подкат: одна нога вперёд, корпус назад
            p.bodyPitch = -25f; p.bodyDrop = 0.45f;
            p.legL = 70f; p.kneeL = 10f;
            p.legR = 20f; p.kneeR = 95f;
            p.armL = 40f; p.armR = -20f; p.armOutL = 35f; p.armOutR = 45f; p.elbowL = 30f;
            p.headPitch = 10f;
        }
        else if (!grounded)
        {
            // В воздухе: группируемся, на падении руки вверх
            float fall = Mathf.Clamp01(-vel.y / 10f);
            p.legL = 35f; p.kneeL = 70f;
            p.legR = 5f; p.kneeR = 45f;
            p.armL = 20f + fall * 60f; p.armR = 10f + fall * 70f;
            p.armOutL = 40f + fall * 20f; p.armOutR = 40f + fall * 20f;
            p.elbowL = 30f; p.elbowR = 30f;
            p.bodyPitch = 5f - fall * 10f;
        }
        else if (state == KnightMovement.MovementState.croaching)
        {
            p = RunCycle(p, speed, dt, 0.6f);
            p.bodyDrop = 0.4f; p.bodyPitch += 15f;
            p.legL += 45f; p.legR += 45f; p.kneeL += 80f; p.kneeR += 80f;
        }
        else if (speed > 0.4f)
        {
            float sprint = movement != null && movement.sprintSpeed > 0f ? speed / movement.sprintSpeed : speed / 10f;
            p = RunCycle(p, speed, dt, Mathf.Lerp(0.7f, 1.15f, Mathf.Clamp01(sprint)));
        }
        else
        {
            // Стоим: дышим
            float breath = Mathf.Sin(t * 2f);
            p.bodyDrop = breath * 0.008f;
            p.armOutL = 8f + breath * 2f; p.armOutR = 8f + breath * 2f;
            p.elbowL = 10f; p.elbowR = 10f;
            p.headPitch = breath * 2f;
        }

        return WeaponArms(p);
    }

    // Курим / пьём: правая рука ко рту, при питье голова запрокидывается
    private Pose ItemUsePose(Pose p)
    {
        if (itemUser == null || !itemUser.IsUsing || (health != null && health.IsDead)) return p;
        float w = itemUser.MouthWeight;
        p.armR = Mathf.Lerp(p.armR, 100f, w);
        p.armOutR = Mathf.Lerp(p.armOutR, -15f, w);
        p.elbowR = Mathf.Lerp(p.elbowR, 140f, w);
        if (itemUser.IsDrinking) p.headPitch = Mathf.Lerp(p.headPitch, -35f, w * itemUser.DrinkTilt);
        return p;
    }

    private Pose RunCycle(Pose p, float speed, float dt, float amplitudeScale)
    {
        phase += speed * stepsPerMeter * Mathf.PI * dt;
        float amp = Mathf.Clamp(speed * 6f, 10f, maxLegSwing) * amplitudeScale;
        float s = Mathf.Sin(phase), c = Mathf.Cos(phase);

        p.legL = s * amp;
        p.legR = -s * amp;
        p.kneeL = Mathf.Max(0f, -c) * amp * 1.3f + 5f;
        p.kneeR = Mathf.Max(0f, c) * amp * 1.3f + 5f;
        p.armL = -s * amp * 0.8f;
        p.armR = s * amp * 0.8f;
        p.armOutL = 10f; p.armOutR = 10f;
        p.elbowL = 25f + Mathf.Max(0f, -s) * 30f;
        p.elbowR = 25f + Mathf.Max(0f, s) * 30f;
        p.bodyPitch = Mathf.Min(speed * runLean, 20f);
        p.bodyDrop = Mathf.Abs(Mathf.Sin(phase)) * 0.04f * amplitudeScale - 0.02f;
        p.bodyYaw = s * 6f;
        p.headPitch = -p.bodyPitch * 0.6f;
        return p;
    }

    private Pose WeaponArms(Pose p)
    {
        if (gun != null)
        {
            // Дробовик у плеча, двумя руками
            p.armR = 70f; p.armOutR = 10f; p.elbowR = 40f;
            p.armL = 80f; p.armOutL = -15f; p.elbowL = 20f;
        }
        else if (sword != null)
        {
            if (sword.IsSwinging)
            {
                float k = sword.SwingProgress;
                float side = sword.SwingMirrored ? -1f : 1f;
                p.armR = Mathf.Lerp(120f, 40f, k);
                p.armOutR = Mathf.Lerp(70f * side, -40f * side, k);
                p.elbowR = Mathf.Lerp(60f, 5f, k);
                p.bodyYaw += Mathf.Lerp(25f, -25f, k) * side;
            }
            else if (sword.IsCharging)
            {
                p.armR = 140f; p.armOutR = 50f; p.elbowR = 70f; p.bodyYaw += 20f; // замах
            }
            else
            {
                p.armR = Mathf.Max(p.armR, 35f); p.elbowR = 50f; p.armOutR = 15f;
            }
        }
        return p;
    }

    private Pose SkatePose(Pose p, float t, float speed, Vector3 vel)
    {
        // Стойка боком: ноги шире, колени согнуты, руки для баланса
        p.legOutL = 16f; p.legOutR = 16f;
        p.kneeL = 25f; p.kneeR = 25f;
        p.legL = 12f; p.legR = 12f;
        p.bodyDrop = 0.12f;
        p.bodyPitch = 10f;
        p.armOutL = 28f; p.armOutR = 28f; p.armL = 10f; p.armR = -5f; p.elbowL = 30f; p.elbowR = 30f;
        p.headPitch = -5f;

        switch (skater.CurrentState)
        {
            case SkaterController.State.Ground:
                if (Time.time - skater.LastPushTime < 0.35f)
                {
                    // Толчок задней ногой
                    float k = Mathf.Sin((Time.time - skater.LastPushTime) / 0.35f * Mathf.PI);
                    p.legOutR = 16f - 40f * k; p.kneeR = 25f + 20f * k; p.kneeL = 45f; p.bodyDrop = 0.2f;
                }
                if (skater.InManual)
                {
                    p.bodyPitch = -5f; p.armL = 40f; p.armR = 40f; p.armOutL = 70f; p.armOutR = 70f;
                    p.bodyRoll = skater.Balance * 20f;
                }
                break;

            case SkaterController.State.Air:
                p.kneeL = 55f; p.kneeR = 55f; p.legL = 35f; p.legR = 35f; p.bodyDrop = 0.25f;
                p.armOutL = 40f; p.armOutR = 40f; p.armL = 25f; p.armR = 15f; p.elbowL = 35f; p.elbowR = 35f;
                if (skater.IsGrabbing)
                {
                    p.kneeL = 100f; p.kneeR = 100f; p.legL = 70f; p.legR = 70f; p.bodyDrop = 0.45f;
                    p.armR = 20f; p.armOutR = -10f; p.elbowR = 10f; p.bodyPitch = 30f; // тянемся к доске
                }
                break;

            case SkaterController.State.Grind:
                p.armOutL = 62f; p.armOutR = 62f; p.armL = 15f; p.armR = 15f;
                p.bodyRoll = skater.Balance * 25f;
                p.kneeL = 35f; p.kneeR = 35f;
                break;
        }

        if (skater.IsChargingOllie)
        {
            p.kneeL = 70f; p.kneeR = 70f; p.legL = 40f; p.legR = 40f; p.bodyDrop = 0.4f; p.bodyPitch = 20f;
        }
        return p;
    }

    // ---------------- Применение ----------------

    private void ApplyPose(Pose p)
    {
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) bones[i].localRotation = restLocal[i];
        if (boneBody != null) boneBody.localPosition = bodyRestLocalPos;

        Quaternion charRot = transform.rotation;
        Vector3 R = charRot * Vector3.right, F = charRot * Vector3.forward, U = charRot * Vector3.up;

        Quaternion bodyDelta = Quaternion.AngleAxis(p.bodyYaw, U) * Quaternion.AngleAxis(p.bodyRoll, F) * Quaternion.AngleAxis(p.bodyPitch, R);
        if (boneBody != null)
        {
            boneBody.rotation = bodyDelta * boneBody.rotation;
            boneBody.position -= U * p.bodyDrop;
        }

        // Конечности, висящие на Body, уже повернулись вместе с ним -- оси для них тоже поворачиваем,
        // а ноги дополнительно "выпрямляем" обратно на угол наклона корпуса
        Quaternion limbFrame = limbsFollowBody ? bodyDelta * charRot : charRot;
        Vector3 r = limbFrame * Vector3.right, f = limbFrame * Vector3.forward;
        float legComp = limbsFollowBody ? p.bodyPitch : 0f;

        if (boneHead != null)
            boneHead.rotation = Quaternion.AngleAxis(-p.headPitch, r) * boneHead.rotation;

        Leg(upperLegL, lowerLegL, p.legL + legComp, p.kneeL, -p.legOutL, r, f);
        Leg(upperLegR, lowerLegR, p.legR + legComp, p.kneeR, p.legOutR, r, f);

        Arm(upperArmL, lowerArmL, armNeutralL, limbFrame, p.armL, -p.armOutL, p.elbowL, r, f);
        Arm(upperArmR, lowerArmR, armNeutralR, limbFrame, p.armR, p.armOutR, p.elbowR, r, f);

        if (skater != null && skater.IsRiding && skater.Board != null && (health == null || !health.IsDead))
            SkateLegIK();
    }

    // Ноги стоят на деке: передняя (левая) ближе к носу, задняя -- к хвосту.
    // Толчок -- задняя нога сходит на землю сбоку; флип -- обе ноги подлетают над доской.
    private void SkateLegIK()
    {
        Quaternion fr = skater.FootFrameRotation;
        Vector3 deck = skater.FootFramePosition;
        Vector3 bf = fr * Vector3.forward, bu = fr * Vector3.up, br = fr * Vector3.right;
        float half = Mathf.Max(0.2f, skater.Board.deckHalfLength) * stanceWidth;

        Vector3 lift = bu * ankleHeight;
        if (skater.IsFlipping) lift += Vector3.up * flipFootLift * Mathf.Sin(skater.FlipProgress * Mathf.PI);

        Vector3 targetL = deck + bf * half + lift;
        Vector3 targetR = deck - bf * half + lift;

        float pushAge = Time.time - skater.LastPushTime;
        if (skater.CurrentState == SkaterController.State.Ground && pushAge < 0.35f)
        {
            float k = Mathf.Sin(pushAge / 0.35f * Mathf.PI);
            Vector3 ground = skater.BoardGroundPosition - bf * half * 1.2f + br * 0.28f + Vector3.up * ankleHeight;
            targetR = Vector3.Lerp(targetR, ground, k);
        }

        Vector3 pole = transform.forward; // колени гнутся вперёд (персонаж стоит к доске боком)
        TwoBoneIK(upperLegL, lowerLegL, footL, targetL, pole);
        TwoBoneIK(upperLegR, lowerLegR, footR, targetR, pole);
    }

    // Аналитический IK на 2 кости в мировых координатах -- не зависит от осей костей
    private static void TwoBoneIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        if (upper == null || lower == null || end == null) return;
        Vector3 a = upper.position;
        float lenA = Vector3.Distance(a, lower.position);
        float lenB = Vector3.Distance(lower.position, end.position);
        if (lenA < 1e-4f || lenB < 1e-4f) return;

        Vector3 toTarget = target - a;
        float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lenA - lenB) + 1e-3f, lenA + lenB - 1e-3f);
        Vector3 dir = toTarget.normalized;

        float cosA = Mathf.Clamp((lenA * lenA + dist * dist - lenB * lenB) / (2f * lenA * dist), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 bendDir = Vector3.ProjectOnPlane(pole, dir);
        if (bendDir.sqrMagnitude < 1e-6f) bendDir = Vector3.ProjectOnPlane(Vector3.forward, dir);
        bendDir.Normalize();

        Vector3 kneeTarget = a + (dir * cosA + bendDir * sinA) * lenA;
        upper.rotation = Quaternion.FromToRotation(lower.position - a, kneeTarget - a) * upper.rotation;
        Vector3 footGoal = a + dir * dist;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, footGoal - lower.position) * lower.rotation;
    }

    private static void Leg(Transform upper, Transform lower, float swing, float knee, float outAngle, Vector3 r, Vector3 f)
    {
        if (upper != null)
            upper.rotation = Quaternion.AngleAxis(-swing, r) * Quaternion.AngleAxis(outAngle, f) * upper.rotation;
        if (lower != null)
            lower.rotation = Quaternion.AngleAxis(knee, r) * lower.rotation;
    }

    private static void Arm(Transform upper, Transform lower, Quaternion neutralChar, Quaternion frame,
        float swing, float outAngle, float elbow, Vector3 r, Vector3 f)
    {
        if (upper != null)
        {
            Quaternion neutralWorld = frame * neutralChar * Quaternion.Inverse(frame);
            upper.rotation = Quaternion.AngleAxis(-swing, r) * Quaternion.AngleAxis(outAngle, f) * neutralWorld * upper.rotation;
        }
        if (lower != null)
            lower.rotation = Quaternion.AngleAxis(-elbow, r) * lower.rotation;
    }

    // Смерть: заваливаемся на спину
    private void ApplyDeath(float dt)
    {
        bool dead = health != null && health.IsDead;
        deathT = Mathf.MoveTowards(deathT, dead ? 1f : 0f, dt * 2.5f);
        if (deathT <= 0f)
        {
            transform.localRotation = baseLocalRot;
            return;
        }
        float e = 1f - (1f - deathT) * (1f - deathT);
        transform.localRotation = baseLocalRot * Quaternion.Euler(-85f * e, 0f, 0f);
        Transform parent = transform.parent;
        Vector3 ps = parent != null ? parent.localScale : Vector3.one;
        transform.localPosition = new Vector3(baseLocalPos.x / ps.x, (baseLocalPos.y + 0.25f * e) / ps.y, (baseLocalPos.z - 0.6f * e) / ps.z);
    }
}
