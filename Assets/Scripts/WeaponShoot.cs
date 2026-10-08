using Photon.Pun;
using UnityEngine;
using System.Collections;

public enum WeaponType { Pistol, Rifle, Shotgun, Sniper }
public enum HitRegion { Head, Body, Arm, Leg }

public class WeaponShoot : MonoBehaviourPun
{
    [Header("Silah Tipi")]
    public WeaponType weaponType = WeaponType.Rifle;

    [Header("Temel Ayarlar")]
    public float range = 100f;

    [Header("Hasar Çarpanları")]
    public float headMultiplier = 4f;
    public float bodyMultiplier = 1f;
    public float armMultiplier  = 0.6f;
    public float legMultiplier  = 0.5f;

    [Header("Ödül")]
    public int killReward = 200;

    [Header("Impact Efektleri")]
    public GameObject woodImpactPrefab;
    public GameObject metalImpactPrefab;
    public GameObject concreteImpactPrefab;
    public GameObject dirtImpactPrefab;
    public GameObject bloodImpactPrefab;

    [Header("Muzzle Flash")]
    public GameObject muzzleFlashPrefab;
    public Transform  muzzlePoint;

    [Header("Bullet Trail")]
    public GameObject bulletTrailPrefab;
    public float      trailDuration = 0.05f;

    private Camera playerCamera;

    // -----------------------------------------------------------------------

    private InfimaGames.LowPolyShooterPack.Weapon GetActiveWeapon()
    {
        var weapons = GetComponentsInChildren<InfimaGames.LowPolyShooterPack.Weapon>(false);
        foreach (var w in weapons)
            if (w.gameObject.activeSelf) return w;
        return null;
    }

    private void Start()
    {
        if (photonView.IsMine)
            playerCamera = Camera.main;
    }

    // -----------------------------------------------------------------------
    // Character.cs'in Fire() metodu buraya çağırır.
    // WeaponShoot kendi başına asla ateş etmez.

    public void FireOnce()
    {
        if (!photonView.IsMine) return;
        if (playerCamera == null) return;

        if (weaponType == WeaponType.Shotgun)
            for (int i = 0; i < 6; i++) Shoot(0.05f);
        else
            Shoot();
    }

    // -----------------------------------------------------------------------

    private void Shoot(float spreadAmount = 0f)
    {
        Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
        Vector3 spread = new Vector3(
            Random.Range(-spreadAmount, spreadAmount),
            Random.Range(-spreadAmount, spreadAmount), 0);

        Ray ray = playerCamera.ScreenPointToRay(screenCenter + spread * Screen.width);

        // Muzzle pozisyonu
        Vector3 muzzlePos = muzzlePoint != null
            ? muzzlePoint.position
            : playerCamera.transform.position;

        Vector3 hitPoint = ray.origin + ray.direction * range; // varsayılan: menzil sonu

        if (Physics.Raycast(ray, out RaycastHit hit, range))
        {
            hitPoint = hit.point;

            // Impact efekti herkese
            photonView.RPC(nameof(SpawnImpactRPC), RpcTarget.All,
                hit.point, hit.normal, hit.collider.tag);

            // Range sahnesi — RangeTarget'a hasar ver
            RangeTarget rangeTarget = hit.collider.GetComponentInParent<RangeTarget>();
            if (rangeTarget == null) rangeTarget = hit.collider.GetComponent<RangeTarget>();
            if (rangeTarget != null)
            {
                float relY = hit.point.y - rangeTarget.transform.position.y;
                RangeTarget.HitRegion region;
                if      (relY > 0.5f)  region = RangeTarget.HitRegion.Head;
                else if (relY > -0.5f) region = RangeTarget.HitRegion.Body;
                else                   region = RangeTarget.HitRegion.Leg;

                Debug.Log($"relY: {relY}, Region: {region}");

                rangeTarget.TakeDamage(BaseDamage(), region);
                HUDManager.Instance?.ShowHitMarker(isKill: false);
            }

            // Hasar — sadece karşı oyuncuya
            PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
            if (health != null && health.photonView.ViewID != photonView.ViewID && !health.IsDead())
            {
                // Takım arkadaşına hasar verme — aynı takımsa geç
                string localTeam  = GetLocalTeam();
                string targetTeam = GetTargetTeam(health);
                bool sameTeam = !string.IsNullOrEmpty(localTeam) && localTeam == targetTeam;

                if (!sameTeam)
                {
                    HitRegion region   = DetectRegion(hit);
                    float     damage   = CalculateDamage(region);
                    float remainingDamage = damage - health.currentShield;
                    bool willKill = (health.currentHealth - Mathf.Max(0f, remainingDamage)) <= 0;

                    string weaponName = weaponType.ToString();
                    health.photonView.RPC("TakeDamageRPC",
                        RpcTarget.All,
                        damage,
                        PhotonNetwork.LocalPlayer.ActorNumber,
                        PhotonNetwork.LocalPlayer.NickName,
                        region.ToString(),
                        weaponName);

                    HUDManager.Instance?.ShowHitMarker(isKill: willKill);

                    if (willKill)
                    {
                        ScoreManager.Instance?.AddKill();
                        ScoreManager.Instance?.AddMoney(killReward);
                        if (HUDManager.Instance != null)
                        {
                            int money  = ScoreManager.Instance?.GetLocalMoney()  ?? 0;
                            int kills  = ScoreManager.Instance?.GetLocalKills()  ?? 0;
                            int deaths = ScoreManager.Instance?.GetLocalDeaths() ?? 0;
                            HUDManager.Instance.UpdateStats(100, 100, 0, 0, money, kills, deaths);
                        }
                    }
                }
            }
        }

        // Muzzle flash + bullet trail herkese
        photonView.RPC(nameof(SpawnMuzzleAndTrailRPC), RpcTarget.All, muzzlePos, hitPoint);
    }

    // -----------------------------------------------------------------------

    [PunRPC]
    private void SpawnImpactRPC(Vector3 position, Vector3 normal, string tag)
    {
        GameObject prefab = tag switch
        {
            "Wood"     => woodImpactPrefab,
            "Metal"    => metalImpactPrefab,
            "Concrete" => concreteImpactPrefab,
            "Dirt"     => dirtImpactPrefab,
            "Blood"    => bloodImpactPrefab,
            _          => woodImpactPrefab
        };

        if (prefab != null)
            Instantiate(prefab, position, Quaternion.LookRotation(normal));
    }

    [PunRPC]
    private void SpawnMuzzleAndTrailRPC(Vector3 fromPos, Vector3 toPos)
    {
        // Flash — asset'in kendi muzzle sistemini kullan, world space spawn değil
        var weapon = GetActiveWeapon();
        if (weapon != null)
            weapon.PlayMuzzleEffect();

        // Bullet trail
        if (bulletTrailPrefab != null)
        {
            GameObject trail = Instantiate(bulletTrailPrefab, fromPos, Quaternion.identity);
            StartCoroutine(MoveTrail(trail, fromPos, toPos));
        }
    }

    private IEnumerator MoveTrail(GameObject trail, Vector3 from, Vector3 to)
    {
        if (trail == null) yield break;
        float elapsed = 0f;
        while (elapsed < trailDuration)
        {
            if (trail == null) yield break;
            elapsed += Time.deltaTime;
            trail.transform.position = Vector3.Lerp(from, to, elapsed / trailDuration);
            yield return null;
        }
        if (trail != null) Destroy(trail);
    }

    // -----------------------------------------------------------------------

    private HitRegion DetectRegion(RaycastHit hit)
    {
        string n = hit.collider.name.ToLower();
        if (n.Contains("head")  || n.Contains("kafa")  || n.Contains("skull")) return HitRegion.Head;
        if (n.Contains("leg")   || n.Contains("thigh") || n.Contains("foot") || n.Contains("bacak")) return HitRegion.Leg;
        if (n.Contains("arm")   || n.Contains("hand")  || n.Contains("kol"))  return HitRegion.Arm;

        float relativeY = hit.point.y - hit.collider.transform.root.position.y;
        if (relativeY > 1.5f) return HitRegion.Head;
        if (relativeY > 0.8f) return HitRegion.Body;
        return HitRegion.Leg;
    }

    private float CalculateDamage(HitRegion region)
    {
        float b = BaseDamage();
        return region switch
        {
            HitRegion.Head => b * headMultiplier,
            HitRegion.Body => b * bodyMultiplier,
            HitRegion.Arm  => b * armMultiplier,
            HitRegion.Leg  => b * legMultiplier,
            _              => b
        };
    }

    private static string GetLocalTeam()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "";
        return "";
    }

    private static string GetTargetTeam(PlayerHealth ph)
    {
        if (ph.photonView.Owner.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "";
        return "";
    }

    private float BaseDamage() => weaponType switch
    {
        WeaponType.Pistol  => 25f,
        WeaponType.Rifle   => 35f,
        WeaponType.Shotgun => 20f,
        WeaponType.Sniper  => 85f,
        _                  => 30f
    };
}