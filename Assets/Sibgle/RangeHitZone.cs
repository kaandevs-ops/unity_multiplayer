using UnityEngine;

public class RangeHitZone : MonoBehaviour
{
    public RangeTarget.HitRegion region;
    private RangeTarget target;

    private void Awake()
    {
        target = GetComponentInParent<RangeTarget>();
    }

    public void ReceiveDamage(float damage)
    {
        if (target != null)
            target.TakeDamage(damage, region);
    }
}
