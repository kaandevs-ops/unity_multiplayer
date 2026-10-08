using UnityEngine;

public class RangeTarget : MonoBehaviour
{
    [Header("Can")]
    public float maxHealth = 100f;
    private float currentHealth;
    private bool isDead = false;

    // Hasar bölgesi çarpanları
    public enum HitRegion { Head, Body, Leg }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    public bool GetIsDead() => isDead;

    public void TakeDamage(float damage, HitRegion region)
    {
        if (isDead) return;

        float finalDamage = damage;

        switch (region)
        {
            case HitRegion.Head: finalDamage = 100f;        break; // Tek kurşun
            case HitRegion.Body: finalDamage = damage;      break; // Normal hasar
            case HitRegion.Leg:  finalDamage = damage * 0.4f; break; // Azaltılmış
        }

        currentHealth -= finalDamage;

        if (currentHealth <= 0f && !isDead)
        {
            isDead = true;
            SendMessageUpwards("OnTargetDied", this, SendMessageOptions.DontRequireReceiver);
        }
    }
}
