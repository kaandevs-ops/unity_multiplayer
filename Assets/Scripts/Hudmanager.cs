using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDManager : MonoBehaviourPun
{
    public static HUDManager Instance { get; private set; }

    // ── Mevcut Alanlar ────────────────────────────────────────────────

    [Header("Can")]
    public Slider healthBar;
    public TMP_Text healthText;

    [Header("Kalkan")]
    public Slider shieldBar;
    public TMP_Text shieldText;

    [Header("Para & Skor")]
    public TMP_Text moneyText;
    public TMP_Text killDeathText;

    [Header("Kill Feed")]
    public Transform killFeedParent;
    public GameObject killFeedItemPrefab;
    public int maxKillFeedItems = 5;
    public float killFeedDuration = 4f;
    private readonly Queue<GameObject> killFeedItems = new();

    [Header("Skor Tablosu")]
    public GameObject scoreboardPanel;
    public Transform redListParent;
    public Transform blueListParent;
    public GameObject scoreRowPrefab;
    private readonly Dictionary<string, GameObject> scoreRows = new();

    [Header("Efektler")]
    public Image damageIndicator;
    public Image hitMarker;
    public float hitMarkerDuration = 0.12f;
    public float damageFlashDuration = 0.25f;

    [Header("Ölüm")]
    public GameObject deathPanel;
    public TMP_Text deathText;

    // ── Yeni: Raund UI ────────────────────────────────────────────────

    [Header("Raund Skoru (üst orta)")]
    public TMP_Text redRoundWinsText;    // "Red  2"
    public TMP_Text blueRoundWinsText;   // "3  Blue"
    public TMP_Text roundNumberText;     // "RAUND 3 / 8"

    [Header("Faz Göstergesi (üst)")]
    public GameObject phasePanel;        // Buy/Fight phase gösterge paneli
    public TMP_Text   phaseText;         // "SATIN ALMA AŞAMASI" / "SAVAŞ"
    public TMP_Text   phaseTimerText;    // "28"
    public Image      phaseTimerFill;    // Timer dolum çubuğu

    [Header("Raund Sonu Ekranı")]
    public GameObject roundWinPanel;
    public GameObject roundLosePanel;

    [Header("Bomba UI")]
    public GameObject bombPlantedBanner;  // "BOMBA KURULDU" yazısı
    public TMP_Text   bombPlantedText;

    [Header("Yarı Zaman")]
    public GameObject halfTimePanel;
    public TMP_Text   halfTimeText;

    [Header("Oyun Sonu")]
    public GameObject gameWinPanel;
    public GameObject gameLosePanel;

    [Header("ESC Menü")]
    public GameObject escPanel;

    // ── İç Değişkenler ────────────────────────────────────────────────

    private float currentPhaseMaxTime;
    private float currentPhaseTime;
    private bool  timerRunning = false;

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        if (damageIndicator) SetAlpha(damageIndicator, 0);
        if (hitMarker)       SetAlpha(hitMarker, 0);
        if (deathPanel)      deathPanel.SetActive(false);
        if (scoreboardPanel) scoreboardPanel.SetActive(false);

        if (shieldBar)  shieldBar.gameObject.SetActive(false);
        if (shieldText) shieldText.gameObject.SetActive(false);

        // Raund UI başlangıç
        if (phasePanel)        phasePanel.SetActive(false);
        if (roundWinPanel)     roundWinPanel.SetActive(false);
        if (roundLosePanel)    roundLosePanel.SetActive(false);
        if (halfTimePanel)     halfTimePanel.SetActive(false);
        if (gameWinPanel)      gameWinPanel.SetActive(false);
        if (gameLosePanel)     gameLosePanel.SetActive(false);
        if (bombPlantedBanner) bombPlantedBanner.SetActive(false);
    }

    private void Update()
    {
        // Scoreboard Tab
        if (scoreboardPanel != null)
        {
            if (Input.GetKeyDown(KeyCode.Tab)) scoreboardPanel.SetActive(true);
            if (Input.GetKeyUp(KeyCode.Tab))   scoreboardPanel.SetActive(false);
        }

        // ESC menü
        if (Input.GetKeyDown(KeyCode.Escape) && escPanel != null)
        {
            bool open = !escPanel.activeSelf;
            escPanel.SetActive(open);
            Cursor.lockState = open ? CursorLockMode.None  : CursorLockMode.Locked;
            Cursor.visible   = open;
        }

        // Faz timer'ı güncelle
        if (timerRunning && currentPhaseTime > 0f)
        {
            currentPhaseTime -= Time.deltaTime;
            currentPhaseTime  = Mathf.Max(0f, currentPhaseTime);
            UpdatePhaseTimerUI();
        }
    }

    // ── Mevcut Metodlar (değişmedi) ───────────────────────────────────

    public void UpdateStats(float hp, float maxHp, float shield, float maxShield, int money, int kills, int deaths)
    {
        if (healthBar)
        {
            healthBar.maxValue = maxHp;
            healthBar.value    = hp;
        }
        if (healthText) healthText.text = $"{Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(maxHp)}";

        bool hasShield = maxShield > 0f;
        if (shieldBar)
        {
            shieldBar.gameObject.SetActive(hasShield);
            shieldBar.maxValue = maxShield;
            shieldBar.value    = shield;
        }
        if (shieldText)
        {
            shieldText.gameObject.SetActive(hasShield);
            shieldText.text = $"{Mathf.CeilToInt(shield)} / {Mathf.CeilToInt(maxShield)}";
        }

        if (moneyText)     moneyText.text     = $"$ {money}";
        if (killDeathText) killDeathText.text = $"K: {kills}  D: {deaths}";
    }

    public void AddKillFeed(string killerName, string victimName, string weaponName = "")
    {
        if (killFeedItemPrefab == null || killFeedParent == null) return;

        if (killFeedItems.Count >= maxKillFeedItems)
        {
            GameObject old = killFeedItems.Dequeue();
            if (old) Destroy(old);
        }

        GameObject item = Instantiate(killFeedItemPrefab, killFeedParent);
        TMP_Text txt = item.GetComponentInChildren<TMP_Text>();
        if (txt)
        {
            bool isMine = killerName == PhotonNetwork.LocalPlayer.NickName;
            string killerColor = isMine ? "#FFD700" : "#FF4444";
            string weaponPart = string.IsNullOrEmpty(weaponName) ? "" : $" <color=#AAAAAA>[{weaponName}]</color>";
            txt.text = $"<color={killerColor}>{killerName}</color>{weaponPart} >> {victimName}";
        }

        killFeedItems.Enqueue(item);
        StartCoroutine(RemoveKillFeedItemAfter(item, killFeedDuration));
    }

    private IEnumerator RemoveKillFeedItemAfter(GameObject item, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (item)
        {
            killFeedItems.TryDequeue(out _);
            Destroy(item);
        }
    }

    public void UpdateScoreboard(string playerName, int kills, int deaths, string team = "Red")
    {
        Transform parent = team == "Blue" ? blueListParent : redListParent;
        if (scoreRowPrefab == null || parent == null) return;

        if (!scoreRows.TryGetValue(playerName, out GameObject row))
        {
            row = Instantiate(scoreRowPrefab, parent);
            scoreRows[playerName] = row;
        }

        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();
        if (texts.Length >= 2)
        {
            texts[0].text = playerName;
            texts[1].text = $"{kills} / {deaths}";
        }
    }

    public void ShowDamageIndicator(float damage, string region)
    {
        StopCoroutine(nameof(FlashDamage));
        StartCoroutine(FlashDamage());
    }

    private IEnumerator FlashDamage()
    {
        if (damageIndicator == null) yield break;
        SetAlpha(damageIndicator, 0.45f);
        float t = 0;
        while (t < damageFlashDuration)
        {
            t += Time.deltaTime;
            SetAlpha(damageIndicator, Mathf.Lerp(0.45f, 0, t / damageFlashDuration));
            yield return null;
        }
        SetAlpha(damageIndicator, 0);
    }

    public void ShowHitMarker(bool isKill = false)
    {
        if (hitMarker == null) return;
        hitMarker.color = isKill ? Color.red : Color.white;
        StopCoroutine(nameof(FadeHitMarker));
        StartCoroutine(FadeHitMarker());
    }

    private IEnumerator FadeHitMarker()
    {
        SetAlpha(hitMarker, 1f);
        yield return new WaitForSeconds(hitMarkerDuration);
        SetAlpha(hitMarker, 0f);
    }

    public void ShowDeathScreen(string killerName)
    {
        if (deathPanel == null) return;
        deathPanel.SetActive(true);
        if (deathText) deathText.text = $"<b>{killerName}</b> tarafından öldürüldün!";

        // Raund sistemi varsa → raund bitene kadar ekranda kalsın
        // Yoksa → 2 saniye sonra kapat
        if (RoundManager.Instance == null)
            StartCoroutine(HideDeathPanel());
    }

    private IEnumerator HideDeathPanel()
    {
        yield return new WaitForSeconds(2f);
        if (deathPanel) deathPanel.SetActive(false);
    }

    // ── Yeni: Raund Metodları ─────────────────────────────────────────

    public void OnRoundStart(int roundNum, int redWins, int blueWins, bool redAttacking, int buyTime)
    {
        // Ölüm ekranını kapat
        if (deathPanel) deathPanel.SetActive(false);

        // Raund sayacı
        if (roundNumberText)
            roundNumberText.text = $"RAUND {roundNum} / {RoundManager.TOTAL_ROUNDS}";

        // Skor güncelle
        UpdateRoundScore(redWins, blueWins);

        // Faz göstergesi
        string role = redAttacking
            ? (GetLocalTeam() == "Red" ? "SALDIRI" : "SAVUNMA")
            : (GetLocalTeam() == "Blue" ? "SALDIRI" : "SAVUNMA");

        ShowPhasePanel($"SATIN ALMA — {role}", buyTime, Color.cyan);

        // Bomba banner'ı temizle
        if (bombPlantedBanner) bombPlantedBanner.SetActive(false);
    }

    public void OnFightPhaseStart(int fightTime)
    {
        string role = RoundManager.Instance != null
            ? (RoundManager.Instance.IsRedAttacking()
                ? (GetLocalTeam() == "Red" ? "SALDIRI" : "SAVUNMA")
                : (GetLocalTeam() == "Blue" ? "SALDIRI" : "SAVUNMA"))
            : "SAVAŞ";

        ShowPhasePanel(role, fightTime, Color.white);
    }

    public void OnRoundEnd(string winnerTeam, string reason, int redWins, int blueWins)
    {
        timerRunning = false;
        if (phasePanel) phasePanel.SetActive(false);
        if (bombPlantedBanner) bombPlantedBanner.SetActive(false);

        UpdateRoundScore(redWins, blueWins);

        bool localWon = GetLocalTeam() == winnerTeam;
        float duration = RoundManager.Instance?.roundEndDuration ?? 5f;

        if (localWon)
        {
            if (roundWinPanel)
            {
                roundWinPanel.SetActive(true);
                StartCoroutine(HidePanel(roundWinPanel, duration));
            }
        }
        else
        {
            if (roundLosePanel)
            {
                roundLosePanel.SetActive(true);
                StartCoroutine(HidePanel(roundLosePanel, duration));
            }
        }
    }

    public void OnBombPlanted()
    {
        if (bombPlantedBanner)
        {
            bombPlantedBanner.SetActive(true);
            if (bombPlantedText) bombPlantedText.text = "⚠ BOMBA KURULDU ⚠";
        }
    }

    public void OnBombExploded()
    {
        if (bombPlantedBanner) bombPlantedBanner.SetActive(false);
    }

    public void OnHalfTime()
    {
        if (halfTimePanel)
        {
            halfTimePanel.SetActive(true);
            if (halfTimeText) halfTimeText.text = "DEVRE ARASI\nTakımlar yer değiştirdi!";
            StartCoroutine(HidePanel(halfTimePanel, 3f));
        }
    }

    public void OnGameOver(string winnerTeam, int redWins, int blueWins)
    {
        if (phasePanel) phasePanel.SetActive(false);

        bool localWon = GetLocalTeam() == winnerTeam;

        if (localWon)
        {
            if (gameWinPanel) gameWinPanel.SetActive(true);
        }
        else
        {
            if (gameLosePanel) gameLosePanel.SetActive(true);
        }
    }

    // ── Faz Timer UI ─────────────────────────────────────────────────

    private void ShowPhasePanel(string label, float duration, Color color)
    {
        if (phasePanel) phasePanel.SetActive(true);
        if (phaseText)
        {
            phaseText.text  = label;
            phaseText.color = color;
        }

        currentPhaseMaxTime = duration;
        currentPhaseTime    = duration;
        timerRunning        = true;
        UpdatePhaseTimerUI();
    }

    private void UpdatePhaseTimerUI()
    {
        if (phaseTimerText)
            phaseTimerText.text = Mathf.CeilToInt(currentPhaseTime).ToString();

        if (phaseTimerFill && currentPhaseMaxTime > 0f)
            phaseTimerFill.fillAmount = currentPhaseTime / currentPhaseMaxTime;
    }

    // ── Raund Skor UI ─────────────────────────────────────────────────

    private void UpdateRoundScore(int redWins, int blueWins)
    {
        if (redRoundWinsText)  redRoundWinsText.text  = redWins.ToString();
        if (blueRoundWinsText) blueRoundWinsText.text = blueWins.ToString();
    }

    // ── ESC Menü ─────────────────────────────────────────────────────

    public void OnClickLeaveRoom()
    {
        escPanel?.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Range")
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        else
            PhotonNetwork.LeaveRoom();
    }

    public void OnClickQuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ── Yardımcılar ───────────────────────────────────────────────────

    private IEnumerator HidePanel(GameObject panel, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (panel) panel.SetActive(false);
    }

    private static string GetLocalTeam()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "None";
        return "None";
    }

    private static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }
}