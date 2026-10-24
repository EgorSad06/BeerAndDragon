using System.Collections;
using UnityEngine;

// Останки скелета (кости или рэгдолл, их создаёт SkeletonDeath): лежат lifetime секунд,
// потом физика выключается, они уходят под землю за sinkTime и удаляются.
public class SkeletonRemains : MonoBehaviour
{
    public float lifetime = 12f;
    public float sinkTime = 2f;
    public float sinkDepth = 0.6f;

    public void Init(float lifetime, float sinkTime)
    {
        this.lifetime = lifetime;
        this.sinkTime = sinkTime;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(lifetime);
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Vector3 from = transform.position;
        for (float t = 0f; t < sinkTime; t += Time.deltaTime)
        {
            transform.position = from + Vector3.down * (sinkDepth * t / sinkTime);
            yield return null;
        }
        Destroy(gameObject);
    }
}
