using UnityEngine;

// Расчёты ИИ скелетов без сцены (проверяются тестами SkeletonTacticsTests):
// прицел лучника с упреждением и навесом, держать ли дистанцию, попал ли удар, как щитовику ответить на замах.
public static class SkeletonTactics
{
    public enum RangeMove { Advance, Hold, Retreat }
    public enum ShieldReaction { None, Block, Parry }

    // Направление выстрела: снаряд со скоростью speed (гравитация gravity вниз) попадает в цель, которая движется
    // с targetVelocity. Упреждение -- по времени полёта (3 уточнения), навес -- нижняя из двух баллистических
    // траекторий. Если не долетает -- прямо на точку упреждения.
    public static Vector3 AimDirection(Vector3 from, Vector3 target, Vector3 targetVelocity, float speed, float gravity)
    {
        Vector3 aim = target;
        for (int i = 0; i < 3; i++)
            aim = target + targetVelocity * (Vector3.Distance(from, aim) / speed);

        Vector3 d = aim - from;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        float x = flat.magnitude;
        if (x < 0.01f || gravity <= 0f) return d.normalized;
        float v2 = speed * speed;
        float disc = v2 * v2 - gravity * (gravity * x * x + 2f * d.y * v2);
        if (disc < 0f) return d.normalized;
        float angle = Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (gravity * x));
        return flat / x * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
    }

    // Лучник: дальше maxRange -- подойти, ближе minRange -- отступить, между -- стоять и стрелять
    public static RangeMove ArcherMove(float distance, float minRange, float maxRange)
    {
        if (distance > maxRange) return RangeMove.Advance;
        if (distance < minRange) return RangeMove.Retreat;
        return RangeMove.Hold;
    }

    // Удар достаёт цель: по горизонтали не дальше reach и не больше halfAngle от направления взгляда
    public static bool InStrike(Vector3 attacker, Vector3 forward, Vector3 target, float reach, float halfAngle)
    {
        Vector3 d = target - attacker;
        d.y = 0f;
        if (d.magnitude > reach) return false;
        if (d.sqrMagnitude < 0.0001f) return true;
        forward.y = 0f;
        return Vector3.Angle(forward, d) <= halfAngle;
    }

    // Щитовик видит меч игрока: удар начался и ещё не дошёл до момента попадания -- парировать (если выпало
    // roll < parryChance), замах или взмах -- поднять щит; игрок дальше threatRange -- ничего.
    public static ShieldReaction ReactToThreat(bool charging, bool swinging, float swingProgress, float hitMoment,
                                               float distance, float threatRange, float parryChance, float roll)
    {
        if (distance > threatRange) return ShieldReaction.None;
        if (swinging && swingProgress < hitMoment && roll < parryChance) return ShieldReaction.Parry;
        if (charging || swinging) return ShieldReaction.Block;
        return ShieldReaction.None;
    }
}
