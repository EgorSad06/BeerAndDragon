using UnityEngine;

// Временные эффекты на игроке. Пока только ускорение от энергетика:
// умножает скорости KnightMovement и слегка расширяет FOV.
public class PlayerStatusEffects : MonoBehaviour
{
    public KnightMovement movement;          // найдётся сам
    public Camera playerCamera;             // найдётся сам
    public float boostFovAdd = 12f;
    public float fovLerpSpeed = 6f;
    public float maxBoostTime = 30f;        // стакается, но не больше этого

    public float SpeedBoostRemaining { get; private set; }
    public float SpeedBoostTotal { get; private set; }
    public float SpeedMultiplier { get; private set; } = 1f;

    private float baseWalk, baseSprint, baseSlide, baseCrouch;
    private float baseFov;

    void Awake()
    {
        if (movement == null) movement = GetComponent<KnightMovement>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();

        if (movement != null)
        {
            baseWalk = movement.walkSpeed;
            baseSprint = movement.sprintSpeed;
            baseSlide = movement.slideSpeed;
            baseCrouch = movement.crouchSpeed;
        }
        if (playerCamera != null) baseFov = playerCamera.fieldOfView;
    }

    public void ApplySpeedBoost(float multiplier, float duration)
    {
        SpeedMultiplier = Mathf.Max(SpeedMultiplier, multiplier);
        SpeedBoostRemaining = Mathf.Min(SpeedBoostRemaining + duration, maxBoostTime);
        SpeedBoostTotal = SpeedBoostRemaining;
        ApplySpeeds();
    }

    void Update()
    {
        if (SpeedBoostRemaining > 0f)
        {
            SpeedBoostRemaining -= Time.deltaTime;
            if (SpeedBoostRemaining <= 0f)
            {
                SpeedBoostRemaining = 0f;
                SpeedMultiplier = 1f;
                ApplySpeeds();
            }
        }

        if (playerCamera != null)
        {
            float target = baseFov + (SpeedBoostRemaining > 0f ? boostFovAdd : 0f);
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, target, Time.deltaTime * fovLerpSpeed);
        }
    }

    private void ApplySpeeds()
    {
        if (movement == null) return;
        movement.walkSpeed = baseWalk * SpeedMultiplier;
        movement.sprintSpeed = baseSprint * SpeedMultiplier;
        movement.slideSpeed = baseSlide * SpeedMultiplier;
        movement.crouchSpeed = baseCrouch * SpeedMultiplier;
    }
}
