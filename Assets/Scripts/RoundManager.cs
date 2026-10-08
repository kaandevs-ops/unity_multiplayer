using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// RoundManager — 8 raundluk Valorant tarzı raund sistemi.
/// 
/// Fazlar: BuyPhase (30sn) → FightPhase → RoundEnd (5sn) → tekrar
/// 
/// Raund kazanma koşulları:
///   Saldıran (Red):  Tüm savunanları öldür VEYA bomba patlasın
///   Savunan  (Blue): Tüm saldırganları öldür VEYA bomba söndür VEYA süre dolsun
/// 
/// 4. raund sonunda takımlar yer değiştirir (attack/defend swap).
/// İlk 5 raund kazanan takım maçı kazanır (8 raundda max 5 kazanılabilir).
/// 
/// Sadece MasterClient yönetir, RPC ile herkese senkronize eder.
/// </summary>
public class RoundManager : MonoBehaviourPunCallbacks
{
    public static RoundManager Instance { get; private set; }

    // ── Raund Sabitler ────────────────────────────────────────────────
    public const int TOTAL_ROUNDS    = 12;
    public const int ROUNDS_PER_HALF = 6;
    public const int WINS_TO_VICTORY = 6;

    // ── Faz Süreleri ──────────────────────────────────────────────────
    [Header("Faz Süreleri (saniye)")]
    public float buyPhaseDuration    = 30f;
    public float fightPhaseDuration  = 120f; // Bomba yoksa round süresi
    public float roundEndDuration    = 5f;

    // ── Durum ─────────────────────────────────────────────────────────
    public enum RoundPhase { WaitingToStart, BuyPhase, FightPhase, RoundEnd, GameOver }

    private RoundPhase currentPhase = RoundPhase.WaitingToStart;
    private int  currentRound  = 0;
    private int  redRoundWins  = 0;
    private int  blueRoundWins = 0;
    private float phaseTimer   = 0f;
    private bool  roundActive  = false;
    private bool  bombPlanted  = false;

    // İlk yarıda Red saldırıyor, Blue savunuyor. 4. raunddan sonra swap.
    private bool redIsAttacking = false; // Blue saldırıyor, Red savunuyor

    // Hayatta olan oyuncu takip
    private HashSet<int> aliveRed  = new(); // ActorNumber
    private HashSet<int> aliveBlue = new();

    // Photon room property anahtarları
    private const string PROP_PHASE        = "rPhase";
    private const string PROP_ROUND        = "rNum";
    private const string PROP_TIMER        = "rTimer";
    private const string PROP_RED_WINS     = "rRW";
    private const string PROP_BLUE_WINS    = "rBW";
    private const string PROP_RED_ATK      = "rRA";

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(StartFirstRound());
    }

    private void Update()
    {
        // Sadece MasterClient timer'ı yönetir
        if (!PhotonNetwork.IsMasterClient) return;
        if (!roundActive) return;

        phaseTimer -= Time.deltaTime;

        // Timer'ı oda property'sine yaz (her saniyede bir — sık yazmayalım)
        if (Mathf.FloorToInt(phaseTimer) != Mathf.FloorToInt(phaseTimer + Time.deltaTime))
            SyncTimerToRoom();

        if (phaseTimer <= 0f)
            OnPhaseTimerExpired();
    }

    // ── Raund Başlatma ────────────────────────────────────────────────

    private IEnumerator StartFirstRound()
    {
        yield return new WaitForSeconds(1f); // Sahne tam yüklensin
        currentRound = 1;
        redIsAttacking = false; // Blue saldırıyor, Red savunuyor
        StartBuyPhase();
    }

    private void StartBuyPhase()
    {
        roundActive   = true;
        bombPlanted   = false;
        currentPhase  = RoundPhase.BuyPhase;
        phaseTimer    = buyPhaseDuration;

        // Bariyerleri aç
        photonView.RPC(nameof(SetBarriersRPC), RpcTarget.All, true);

        // Tüm oyuncuları respawn et (raund başı)
        photonView.RPC(nameof(RoundStartRPC), RpcTarget.All,
            currentRound, redRoundWins, blueRoundWins, redIsAttacking);

        SyncPhaseToRoom();
        RebuildAliveSets();
    }

    private void StartFightPhase()
    {
        currentPhase = RoundPhase.FightPhase;
        phaseTimer   = fightPhaseDuration;
        photonView.RPC(nameof(SetBarriersRPC), RpcTarget.All, false);
        photonView.RPC(nameof(FightPhaseStartRPC), RpcTarget.All);
        SyncPhaseToRoom();
    }

    private void EndRound(string winnerTeam, string reason)
    {
        if (currentPhase == RoundPhase.RoundEnd || currentPhase == RoundPhase.GameOver) return;

        roundActive  = false;
        currentPhase = RoundPhase.RoundEnd;

        if (winnerTeam == "Red")  redRoundWins++;
        else                      blueRoundWins++;

        // Para dağıtımı — kazanan +3000, kaybeden +1900
        DistributeRoundMoney(winnerTeam);

        photonView.RPC(nameof(RoundEndRPC), RpcTarget.All,
            winnerTeam, reason, redRoundWins, blueRoundWins);

        SyncPhaseToRoom();
        StartCoroutine(WaitThenNextRound());
    }

    private IEnumerator WaitThenNextRound()
    {
        yield return new WaitForSeconds(roundEndDuration);

        // Oyun bitti mi?
        if (redRoundWins >= WINS_TO_VICTORY || blueRoundWins >= WINS_TO_VICTORY
            || currentRound >= TOTAL_ROUNDS)
        {
            string winner = redRoundWins > blueRoundWins ? "Red" : "Blue";
            photonView.RPC(nameof(GameOverRPC), RpcTarget.All, winner, redRoundWins, blueRoundWins);
            currentPhase = RoundPhase.GameOver;
            yield break;
        }

        currentRound++;

        // 4. raund geçince takım rolleri değişir
        if (currentRound == ROUNDS_PER_HALF + 1)
        {
            redIsAttacking = !redIsAttacking;
            photonView.RPC(nameof(HalfTimeRPC), RpcTarget.All);
            yield return new WaitForSeconds(3f);
        }

        StartBuyPhase();
    }

    // ── Faz Timer Doldu ───────────────────────────────────────────────

    private void OnPhaseTimerExpired()
    {
        phaseTimer = 0f;
        roundActive = false;

        switch (currentPhase)
        {
            case RoundPhase.BuyPhase:
                StartFightPhase();
                break;

            case RoundPhase.FightPhase:
                // Bomba kuruluysa süre dolunca raund bitmez — BombSystem patlayacak
                if (bombPlanted) { roundActive = true; return; }
                // Süre doldu → savunan kazanır
                string defender = redIsAttacking ? "Blue" : "Red";
                EndRound(defender, "Süre doldu");
                break;
        }
    }

    // ── Ölüm Bildirimi (PlayerHealth tarafından çağrılır) ─────────────

    /// <summary>
    /// Bir oyuncu öldüğünde PlayerHealth.DieRPC buraya haber verir.
    /// Sadece MasterClient üzerinde çalışır.
    /// </summary>
    public void OnPlayerDied(int actorNumber, string team)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (currentPhase != RoundPhase.FightPhase) return;

        if (team == "Red")  aliveRed.Remove(actorNumber);
        else                aliveBlue.Remove(actorNumber);

        CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        string attacker = redIsAttacking ? "Red" : "Blue";
        string defender = redIsAttacking ? "Blue" : "Red";

        if (aliveRed.Count == 0 && aliveBlue.Count == 0)
        {
            EndRound(attacker, "Tüm oyuncular öldü");
            return;
        }

        // Saldıranlar öldü — bomba kuruluysa raund devam eder
        if (attacker == "Red" && aliveRed.Count == 0)
        {
            if (!bombPlanted) EndRound(defender, "Tüm saldıranlar öldürüldü");
            return;
        }
        if (attacker == "Blue" && aliveBlue.Count == 0)
        {
            if (!bombPlanted) EndRound(defender, "Tüm saldıranlar öldürüldü");
            return;
        }

        // Savunanlar öldü — saldıranlar kazanır
        if (defender == "Red" && aliveRed.Count == 0)
        {
            EndRound(attacker, "Tüm savunanlar öldürüldü");
            return;
        }
        if (defender == "Blue" && aliveBlue.Count == 0)
        {
            EndRound(attacker, "Tüm savunanlar öldürüldü");
            return;
        }
    }

    // Bomba sistemi tarafından çağrılır
    public void OnBombPlantedNotify()
    {
        bombPlanted = true;
        // Bomba kuran takıma bonus para
        if (PhotonNetwork.IsMasterClient)
            photonView.RPC(nameof(BombPlantBonusRPC), RpcTarget.All);
    }

    public void OnBombExploded()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bombPlanted = false;
        string attacker = redIsAttacking ? "Red" : "Blue";
        EndRound(attacker, "Bomba patladı!");
    }

    private void DistributeRoundMoney(string winnerTeam)
    {
        photonView.RPC(nameof(RoundMoneyRPC), RpcTarget.All, winnerTeam);
    }

    [PunRPC]
    private void RoundMoneyRPC(string winnerTeam)
    {
        string localTeam = GetTeam(PhotonNetwork.LocalPlayer);
        if (localTeam == winnerTeam)
            ScoreManager.Instance?.AddRoundWinBonus();
        else
            ScoreManager.Instance?.AddRoundLossBonus();
    }

    [PunRPC]
    private void BombPlantBonusRPC()
    {
        // Sadece saldıran takım alır
        string localTeam = GetTeam(PhotonNetwork.LocalPlayer);
        string attacker  = redIsAttacking ? "Red" : "Blue";
        if (localTeam == attacker)
            ScoreManager.Instance?.AddBombPlantBonus();
    }

    public void OnBombDefused()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bombPlanted = false;
        string defender = redIsAttacking ? "Blue" : "Red";
        EndRound(defender, "Bomba söndürüldü!");
    }

    // ── Alive Set Yönetimi ────────────────────────────────────────────

    private void RebuildAliveSets()
    {
        aliveRed.Clear();
        aliveBlue.Clear();

        foreach (Player p in PhotonNetwork.PlayerList)
        {
            string team = GetTeam(p);
            if (team == "Red")  aliveRed.Add(p.ActorNumber);
            if (team == "Blue") aliveBlue.Add(p.ActorNumber);
        }
    }

    private static string GetTeam(Player p)
    {
        if (p.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            return t as string ?? "None";
        return "None";
    }

    // ── Getters (HUD ve diğerleri için) ──────────────────────────────

    public RoundPhase GetPhase()         => currentPhase;
    public int  GetCurrentRound()        => currentRound;
    public int  GetRedWins()             => redRoundWins;
    public int  GetBlueWins()            => blueRoundWins;
    public float GetPhaseTimer()         => phaseTimer;
    public bool IsRedAttacking()         => redIsAttacking;
    public bool IsBuyPhase()             => currentPhase == RoundPhase.BuyPhase;
    public bool IsFightPhase()           => currentPhase == RoundPhase.FightPhase;

    // ── Photon Room Sync ──────────────────────────────────────────────

    private void SyncPhaseToRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Hashtable props = new()
        {
            { PROP_PHASE,    (int)currentPhase },
            { PROP_ROUND,    currentRound      },
            { PROP_RED_WINS, redRoundWins      },
            { PROP_BLUE_WINS,blueRoundWins     },
            { PROP_RED_ATK,  redIsAttacking    },
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void SyncTimerToRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Hashtable props = new() { { PROP_TIMER, Mathf.CeilToInt(phaseTimer) } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // ── RPCs ──────────────────────────────────────────────────────────

    [PunRPC]
    private void RoundStartRPC(int roundNum, int redWins, int blueWins, bool redAtk)
    {
        currentRound    = roundNum;
        redRoundWins    = redWins;
        blueRoundWins   = blueWins;
        redIsAttacking  = redAtk;
        currentPhase    = RoundPhase.BuyPhase;

        // Tüm oyuncuları respawn et
        var health = FindLocalPlayerHealth();
        if (health != null)
        {
            health.RespawnForRound();
        }

        // Mermileri yenile
        ShopManager.Instance?.ResetAllAmmunition();

        // Bombayı sıfırla
        BombSystem.Instance?.ResetBomb();

        // Shop aç (buy phase)
        ShopManager.Instance?.OpenShopForBuyPhase();

        // HUD güncelle
        HUDManager.Instance?.OnRoundStart(roundNum, redWins, blueWins, redAtk, (int)buyPhaseDuration);

        Debug.Log($"[Round] Raund {roundNum} başladı — BUY PHASE");
    }

    [PunRPC]
    private void FightPhaseStartRPC()
    {
        currentPhase = RoundPhase.FightPhase;

        // Shop kapat
        ShopManager.Instance?.CloseShop();

        // HUD
        HUDManager.Instance?.OnFightPhaseStart((int)fightPhaseDuration);

        Debug.Log("[Round] FIGHT PHASE başladı");
    }

    [PunRPC]
    private void RoundEndRPC(string winnerTeam, string reason, int redWins, int blueWins)
    {
        currentPhase  = RoundPhase.RoundEnd;
        redRoundWins  = redWins;
        blueRoundWins = blueWins;

        HUDManager.Instance?.OnRoundEnd(winnerTeam, reason, redWins, blueWins);

        Debug.Log($"[Round] Raund bitti — Kazanan: {winnerTeam} ({reason})");
    }

    [PunRPC]
    private void HalfTimeRPC()
    {
        HUDManager.Instance?.OnHalfTime();
        Debug.Log("[Round] DEVRE ARASI — Takımlar yer değiştirdi!");
    }

    [PunRPC]
    private void GameOverRPC(string winnerTeam, int redWins, int blueWins)
    {
        currentPhase = RoundPhase.GameOver;
        HUDManager.Instance?.OnGameOver(winnerTeam, redWins, blueWins);
        Debug.Log($"[Round] OYUN BİTTİ — Kazanan: {winnerTeam} ({redWins}-{blueWins})");
        StartCoroutine(ReturnToLobby());
    }

    private IEnumerator ReturnToLobby()
    {
        yield return new WaitForSeconds(5f);
        PhotonNetwork.LeaveRoom();
    }

    [PunRPC]
    private void SetBarriersRPC(bool active)
    {
        // FindGameObjectsWithTag disabled objeleri bulamaz — tüm objeleri tara
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go.scene.isLoaded) continue;
            if (go.CompareTag("RedBarrier") || go.CompareTag("BlueBarrier"))
                go.SetActive(active);
        }
        Debug.Log($"[Round] Bariyerler: {(active ? "AÇIK" : "KAPALI")}");
    }

    // ── Yardımcı ─────────────────────────────────────────────────────

    private static PlayerHealth FindLocalPlayerHealth()
    {
        foreach (var ph in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (ph.photonView.IsMine) return ph;
        }
        return null;
    }
}