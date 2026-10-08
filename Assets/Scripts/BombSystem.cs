using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BombSystem — Trigger tabanlı Valorant tarzı bomba sistemi.
/// 
/// Kurulum:
///   1. Haritada bomba alanı olacak Cube oluştur, Is Trigger işaretle,
///      Mesh Renderer kapat, tag olarak BombSiteA veya BombSiteB ver.
///   2. O Cube'a BombSiteTrigger.cs scriptini ekle (ayrı script).
///   3. Sahnede boş bir GameObject'e bu scripti ve PhotonView ekle.
///   4. Inspector'dan UI alanlarını bağla.
/// </summary>
public class BombSystem : MonoBehaviourPun
{
    public static BombSystem Instance { get; private set; }

    [Header("Bomba Ayarları")]
    public float plantDuration  = 3f;
    public float defuseDuration = 5f;
    public float bombTimer      = 45f;

    [Header("Ses")]
    public AudioClip plantSound;
    public AudioClip defuseSound;
    public AudioClip explosionSound;
    public AudioClip tickSound;
    private AudioSource audioSource;

    [Header("UI")]
    public GameObject bombUI;
    public Slider     progressBar;
    public TMP_Text   progressLabel;
    public GameObject bombTimerUI;
    public TMP_Text   bombTimerText;
    public Image      bombTimerFill;

    // ── Durum ─────────────────────────────────────────────────────────
    public enum BombState { Idle, Planted, Defused, Exploded }
    private BombState state = BombState.Idle;

    private Vector3 plantedPosition;
    private float   remainingBombTime;

    private bool isPlanting  = false;
    private bool isDefusing  = false;
    private Coroutine activeCoroutine;
    private Coroutine countdownCoroutine;

    // Oyuncunun şu an içinde olduğu site (BombSiteTrigger tarafından set edilir)
    private BombSiteTrigger currentSite = null;

    // Bomba kurulduktan sonra görsel marker
    private GameObject bombMarker;

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        StartCoroutine(FindUILate());
    }

    private IEnumerator FindUILate()
    {
        float timeout = 10f;
        while (timeout > 0f)
        {
            if (bombUI == null)
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go.name == "BombActionPanel" && go.scene.isLoaded)
                    {
                        bombUI      = go;
                        progressBar = go.GetComponentInChildren<Slider>(true);
                        var texts   = go.GetComponentsInChildren<TMP_Text>(true);
                        if (texts.Length > 0) progressLabel = texts[0];
                        break;
                    }
                }
            }

            if (bombTimerUI == null)
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go.name == "BombTimerPanel" && go.scene.isLoaded)
                    {
                        bombTimerUI = go;
                        var texts   = go.GetComponentsInChildren<TMP_Text>(true);
                        if (texts.Length > 0) bombTimerText = texts[0];
                        break;
                    }
                }
            }

            if (bombUI != null && bombTimerUI != null)
            {
                HideBombUI();
                HideBombTimer();
                Debug.Log("[BombSystem] UI bulundu!");
                yield break;
            }

            timeout -= Time.deltaTime;
            yield return null;
        }
        Debug.LogWarning("[BombSystem] UI bulunamadi!");
    }

    private void Update()
    {
        if (state == BombState.Idle)
            HandlePlantInput();
        else if (state == BombState.Planted)
            HandleDefuseInput();
    }

    // ── Trigger Callbacks (BombSiteTrigger çağırır) ───────────────────

    public void OnPlayerEnteredSite(BombSiteTrigger site)
    {
        currentSite = site;
    }

    public void OnPlayerExitedSite(BombSiteTrigger site)
    {
        if (currentSite == site)
        {
            currentSite = null;
            if (isPlanting) CancelAction();
        }
    }

    // ── Kurma ─────────────────────────────────────────────────────────

    private void HandlePlantInput()
    {
        if (!IsLocalPlayerAttacker()) return;
        if (currentSite == null) return; // Alan dışında

        // Ölü oyuncu bomba kuramaz
        Transform playerT = FindLocalPlayerTransform();
        PlayerHealth ph = playerT != null ? playerT.GetComponent<PlayerHealth>() : null;
        if (ph != null && ph.IsDead()) return;

        if (Input.GetKey(KeyCode.F))
        {
            if (!isPlanting)
            {
                isPlanting = true;
                // Kurma pozisyonu: oyuncunun ayağının önü
                Vector3 plantPos  = playerT != null ? playerT.position : currentSite.transform.position;
                activeCoroutine   = StartCoroutine(PlantCoroutine(plantPos));
            }
        }
        else
        {
            if (isPlanting) CancelAction();
        }
    }

    private IEnumerator PlantCoroutine(Vector3 position)
    {
        ShowBombUI("Bomba Kuruluyor... [F]");
        PlaySound(plantSound);

        float elapsed = 0f;
        while (elapsed < plantDuration)
        {
            if (!Input.GetKey(KeyCode.F)) { CancelAction(); yield break; }

            elapsed += Time.deltaTime;
            SetProgress(elapsed / plantDuration);
            yield return null;
        }

        isPlanting = false;
        HideBombUI();

        photonView.RPC(nameof(BombPlantedRPC), RpcTarget.All, position);
    }

    // ── Söndürme ──────────────────────────────────────────────────────

    private void HandleDefuseInput()
    {
        if (!IsLocalPlayerDefender()) return;

        // Ölü oyuncu bomba söndüremez
        Transform playerT = FindLocalPlayerTransform();
        if (playerT == null) return;
        PlayerHealth ph = playerT.GetComponent<PlayerHealth>();
        if (ph != null && ph.IsDead()) return;

        float dist = Vector3.Distance(playerT.position, plantedPosition);
        if (dist > 5f)
        {
            if (isDefusing) CancelAction();
            return;
        }

        if (Input.GetKey(KeyCode.F))
        {
            if (!isDefusing)
            {
                isDefusing = true;
                activeCoroutine = StartCoroutine(DefuseCoroutine());
            }
        }
        else
        {
            if (isDefusing) CancelAction();
        }
    }

    private IEnumerator DefuseCoroutine()
    {
        ShowBombUI("Bomba Söndürülüyor... [F]");
        PlaySound(defuseSound);

        float elapsed = 0f;
        while (elapsed < defuseDuration)
        {
            if (!Input.GetKey(KeyCode.F)) { CancelAction(); yield break; }

            elapsed += Time.deltaTime;
            SetProgress(elapsed / defuseDuration);
            yield return null;
        }

        isDefusing = false;
        HideBombUI();

        photonView.RPC(nameof(BombDefusedRPC), RpcTarget.All);
    }

    // ── Geri Sayım ────────────────────────────────────────────────────

    private IEnumerator BombCountdown()
    {
        // Sadece MasterClient sayar ve patlama RPC'sini gönderir
        if (!PhotonNetwork.IsMasterClient) yield break;

        remainingBombTime = bombTimer;

        while (remainingBombTime > 0f)
        {
            remainingBombTime -= Time.deltaTime;

            if (remainingBombTime < 10f &&
                Mathf.FloorToInt(remainingBombTime) != Mathf.FloorToInt(remainingBombTime + Time.deltaTime))
            {
                PlaySound(tickSound);
            }

            if (state != BombState.Planted) yield break;
            yield return null;
        }

        if (state == BombState.Planted)
            photonView.RPC(nameof(BombExplodedRPC), RpcTarget.All);
    }

    private IEnumerator BombTimerUICoroutine()
    {
        // Tüm clientlarda UI'ı günceller (kendi lokal sayacıyla)
        float localTimer = bombTimer;
        while (localTimer > 0f && state == BombState.Planted)
        {
            localTimer -= Time.deltaTime;
            remainingBombTime = localTimer; // UI için kullanılan alan
            UpdateBombTimerUI();
            yield return null;
        }
    }

    // ── RPCs ──────────────────────────────────────────────────────────

    [PunRPC]
    private void BombPlantedRPC(Vector3 position)
    {
        state           = BombState.Planted;
        plantedPosition = position;

        HideBombUI();
        ShowBombTimer();

        // Küçük görsel marker — bombanın nerede olduğunu gösterir
        SpawnBombMarker(position);

        countdownCoroutine = StartCoroutine(BombCountdown());       // Sadece MasterClient sayar
        StartCoroutine(BombTimerUICoroutine());                       // Herkes UI günceller

        // MasterClient'a bomba kuruldu bildir
        if (PhotonNetwork.IsMasterClient)
            RoundManager.Instance?.OnBombPlantedNotify();

        HUDManager.Instance?.OnBombPlanted();
        Debug.Log($"[Bomb] Bomba kuruldu: {position}");
    }

    [PunRPC]
    private void BombDefusedRPC()
    {
        state = BombState.Defused;
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        DestroyBombMarker();
        HideBombUI();
        HideBombTimer();
        RoundManager.Instance?.OnBombDefused();
        Debug.Log("[Bomb] Bomba söndürüldü!");
    }

    [PunRPC]
    private void BombExplodedRPC()
    {
        state = BombState.Exploded;
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        DestroyBombMarker();
        HideBombUI();
        HideBombTimer();
        PlaySound(explosionSound);
        HUDManager.Instance?.OnBombExploded();

        if (PhotonNetwork.IsMasterClient)
            RoundManager.Instance?.OnBombExploded();

        Debug.Log("[Bomb] BOMBA PATLADI!");
    }

    // ── Reset ─────────────────────────────────────────────────────────

    public void ResetBomb()
    {
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        state      = BombState.Idle;
        isPlanting = false;
        isDefusing = false;
        currentSite = null;
        DestroyBombMarker();
        HideBombUI();
        HideBombTimer();
    }

    // ── Bomba Marker (küçük kırmızı küp) ─────────────────────────────

    private void SpawnBombMarker(Vector3 pos)
    {
        DestroyBombMarker();
        bombMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bombMarker.name = "BombMarker";
        bombMarker.transform.position = pos + Vector3.up * 0.2f;
        bombMarker.transform.localScale = Vector3.one * 0.3f;
        Destroy(bombMarker.GetComponent<Collider>());

        var rend = bombMarker.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(Shader.Find("Standard"));
            rend.material.color = Color.red;
            rend.material.EnableKeyword("_EMISSION");
            rend.material.SetColor("_EmissionColor", Color.red * 2f);
        }
    }

    private void DestroyBombMarker()
    {
        if (bombMarker != null) Destroy(bombMarker);
    }

    // ── UI ────────────────────────────────────────────────────────────

    private void ShowBombUI(string label)
    {
        if (bombUI)       bombUI.SetActive(true);
        if (progressBar)  progressBar.value = 0f;
        if (progressLabel) progressLabel.text = label;
    }

    private void HideBombUI()
    {
        if (bombUI) bombUI.SetActive(false);
    }

    private void SetProgress(float t)
    {
        if (progressBar) progressBar.value = t;
    }

    private void ShowBombTimer()
    {
        if (bombTimerUI) bombTimerUI.SetActive(true);
    }

    private void HideBombTimer()
    {
        if (bombTimerUI) bombTimerUI.SetActive(false);
    }

    private void UpdateBombTimerUI()
    {
        if (bombTimerText)
        {
            int sec = Mathf.CeilToInt(remainingBombTime);
            bombTimerText.text = $"0:{sec:D2}";
        }

        if (bombTimerFill)
        {
            float t = remainingBombTime / bombTimer;
            bombTimerFill.fillAmount = t;
            bombTimerFill.color = Color.Lerp(Color.red, Color.yellow, t);
        }
    }

    private void CancelAction()
    {
        isPlanting = false;
        isDefusing = false;
        if (activeCoroutine != null) StopCoroutine(activeCoroutine);
        HideBombUI();
    }

    // ── Yardımcılar ───────────────────────────────────────────────────

    private bool IsLocalPlayerAttacker()
    {
        if (RoundManager.Instance == null) return false;
        string team = GetLocalTeam();
        return RoundManager.Instance.IsRedAttacking() ? team == "Red" : team == "Blue";
    }

    private bool IsLocalPlayerDefender()
    {
        if (RoundManager.Instance == null) return false;
        string team = GetLocalTeam();
        return RoundManager.Instance.IsRedAttacking() ? team == "Blue" : team == "Red";
    }

    private static string GetLocalTeam()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "None";
        return "None";
    }

    private static Transform FindLocalPlayerTransform()
    {
        foreach (var ph in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            if (ph.photonView.IsMine) return ph.transform;
        return null;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip);
    }

    public BombState GetState() => state;
}