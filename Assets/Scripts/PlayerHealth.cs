using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviourPun, IPunObservable
{
    [Header("Can")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Kalkan")]
    public float maxShield = 0f;
    public float currentShield = 0f;

    [Header("UI")]
    public Slider healthSlider;

    [Header("Para")]
    public int killReward = 200;

    [Header("Görünürlük")]
    public GameObject capsuleObject;

    private bool isDead = false;
    public bool IsDead() => isDead;

    private void Start()
    {
        currentHealth = maxHealth;
        currentShield = 0f;
        UpdateHealthSlider();
        if (photonView.IsMine) UpdateHUD();
    }

    public void BuyShield(float amount)
    {
        maxShield = amount;
        currentShield = amount;
        UpdateHUD();
    }

    public void ResetShield()
    {
        currentShield = 0f;
        maxShield = 0f;
    }

    [PunRPC]
    public void TakeDamageRPC(float damage, int attackerActorNumber, string attackerName, string region, string weaponName = "Rifle")
    {
        if (isDead) return;

        if (photonView.IsMine)
        {
            if (currentShield > 0f)
            {
                float shieldDamage = Mathf.Min(damage, currentShield);
                currentShield -= shieldDamage;
                damage -= shieldDamage;
            }

            if (damage > 0f)
            {
                currentHealth -= damage;
                currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
            }

            UpdateHealthSlider();
            UpdateHUD();

            HUDManager.Instance?.ShowDamageIndicator(damage, region);

            if (currentHealth <= 0 && !isDead)
                photonView.RPC(nameof(DieRPC), RpcTarget.All, attackerName, weaponName);
        }
    }

    [PunRPC]
    public void DieRPC(string killerName, string weaponName = "Rifle")
    {
        if (isDead) return;
        isDead = true;

        if (capsuleObject != null) capsuleObject.SetActive(false);

        HUDManager.Instance?.AddKillFeed(killerName, photonView.Owner.NickName, weaponName);

        if (photonView.IsMine)
        {
            ScoreManager.Instance?.AddDeath();
            HUDManager.Instance?.ShowDeathScreen(killerName);
            ShopManager.Instance?.ResetInventory();

            // Fizik dondur — ölü karakter yerinde kalsın, hareket etmesin
            FreezeCharacter(true);

            UpdateHUD();

            if (RoundManager.Instance != null)
            {
                string team = GetLocalTeam();
                photonView.RPC(nameof(NotifyDeathToMasterRPC), RpcTarget.MasterClient,
                    PhotonNetwork.LocalPlayer.ActorNumber, team);

                // İzleme moduna geç
                StartCoroutine(EnterSpectatorMode());
            }
            else
            {
                RequestRespawn();
            }
        }
    }

    [PunRPC]
    private void NotifyDeathToMasterRPC(int actorNumber, string team)
    {
        RoundManager.Instance?.OnPlayerDied(actorNumber, team);
    }

    // ── İzleme Modu ───────────────────────────────────────────────────

    private int spectatorIndex = 0;
    private Transform originalCameraParent = null;

    private System.Collections.IEnumerator EnterSpectatorMode()
    {
        // Bir frame bekle
        yield return null;

        spectatorIndex = 0;

        // Orijinal kamera parent'ını kaydet
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null) originalCameraParent = cam.transform.parent;

        SwitchSpectatorTarget();
    }

    private void SwitchSpectatorTarget()
    {
        var teammates = new System.Collections.Generic.List<PlayerHealth>();
        string myTeam = GetLocalTeam();

        foreach (var ph in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (ph == this) continue;
            string theirTeam = "";
            if (ph.photonView.Owner.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
                theirTeam = t as string ?? "";
            if (theirTeam == myTeam && !ph.isDead)
                teammates.Add(ph);
        }

        if (teammates.Count == 0) return;

        spectatorIndex = spectatorIndex % teammates.Count;
        PlayerHealth target = teammates[spectatorIndex];

        // Kamerayı hedefin SOCKET_Camera'sına taşı
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            Transform targetSocket = target.transform.Find("SK_FP_CH_Default_Root/Armature/root/spine_01/spine_02/spine_03/neck_01/head/SOCKET_Camera");
            if (targetSocket != null)
            {
                cam.transform.SetParent(targetSocket);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
                cam.enabled = true;
            }
        }
    }

    private void Update()
    {
        if (!isDead || !photonView.IsMine) return;
        if (RoundManager.Instance == null) return;

        if (Input.GetKeyDown(KeyCode.F))
        {
            spectatorIndex++;
            SwitchSpectatorTarget();
        }
    }

    public void RespawnForRound()
    {
        if (capsuleObject != null) capsuleObject.SetActive(true);
        isDead = false;
        currentHealth = maxHealth;
        currentShield = 0f;
        maxShield = 0f;
        UpdateHealthSlider();

        if (!photonView.IsMine) return;

        // Fizik çöz — tekrar hareket edebilsin
        FreezeCharacter(false);
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            Transform socket = transform.Find("SK_FP_CH_Default_Root/Armature/root/spine_01/spine_02/spine_03/neck_01/head/SOCKET_Camera");
            if (socket != null)
            {
                cam.transform.SetParent(socket);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
                cam.enabled = true;
            }
        }

        MoveToSpawnPoint();
        UpdateHUD();

        // Diğer clientlarda da capsule'ü aç
        photonView.RPC(nameof(SetCapsuleVisibleRPC), RpcTarget.Others, true);

        // Mermileri yenile
        ShopManager.Instance?.ResetAllAmmunition();
    }

    [PunRPC]
    private void SetCapsuleVisibleRPC(bool visible)
    {
        if (capsuleObject != null) capsuleObject.SetActive(visible);
    }

    // ── Eski Respawn (Raund sistemi yokken) ───────────────────────────

    private void RequestRespawn()
    {
        photonView.RPC(nameof(RespawnRPC), RpcTarget.All);
    }

    [PunRPC]
    public void RespawnRPC()
    {
        isDead = false;
        currentHealth = maxHealth;
        currentShield = 0f;
        maxShield = 0f;
        UpdateHealthSlider();

        if (!photonView.IsMine) return;

        MoveToSpawnPoint();
        UpdateHUD();
    }

    private void MoveToSpawnPoint()
    {
        string team = GetLocalTeam();
        bool isAttacker = RoundManager.Instance != null
    ? (RoundManager.Instance.IsRedAttacking() ? team == "Red" : team == "Blue")
    : team == "Red";
        string tag = isAttacker ? "AttackerSpawn" : "DefenderSpawn";

        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag(tag);
        if (spawnPoints.Length == 0)
            spawnPoints = GameObject.FindGameObjectsWithTag("AttackerSpawn");
        if (spawnPoints.Length == 0) return;

        Transform sp = spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
        transform.position = sp.position;
        transform.rotation = sp.rotation;

        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(sp.position);
        }

        if (TryGetComponent<CharacterController>(out var cc))
        {
            cc.enabled = false;
            transform.position = sp.position;
            cc.enabled = true;
        }

        currentHealth = maxHealth;
        UpdateHealthSlider();
    }

    // ── Karakter Dondurma ─────────────────────────────────────────────

    private void FreezeCharacter(bool freeze)
    {
        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = freeze;
        }
        if (TryGetComponent<CharacterController>(out var cc))
            cc.enabled = !freeze;
    }

    // ── Yardımcılar ───────────────────────────────────────────────────

    private static string GetLocalTeam()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "Red";
        return "Red";
    }

    private void UpdateHealthSlider()
    {
        if (healthSlider == null) return;
        healthSlider.maxValue = maxHealth;
        healthSlider.value = currentHealth;
    }

    public void UpdateHUD()
    {
        if (!photonView.IsMine) return;
        if (HUDManager.Instance == null) return;

        int money = ScoreManager.Instance?.GetLocalMoney() ?? 0;
        int kills = ScoreManager.Instance?.GetLocalKills() ?? 0;
        int deaths = ScoreManager.Instance?.GetLocalDeaths() ?? 0;

        HUDManager.Instance.UpdateStats(currentHealth, maxHealth, currentShield, maxShield, money, kills, deaths);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
            stream.SendNext(isDead);
            stream.SendNext(currentShield);
        }
        else
        {
            currentHealth = (float)stream.ReceiveNext();
            isDead = (bool)stream.ReceiveNext();
            currentShield = (float)stream.ReceiveNext();
            UpdateHealthSlider();
        }
    }
}