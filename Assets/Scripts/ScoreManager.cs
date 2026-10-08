using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class ScoreManager : MonoBehaviourPunCallbacks
{
    public static ScoreManager Instance { get; private set; }

    public class PlayerScore
    {
        public string Name;
        public int Kills;
        public int Deaths;
        public int Money;
        public string Team;

        public float KDR => Deaths == 0 ? Kills : (float)Kills / Deaths;
    }

    private readonly Dictionary<int, PlayerScore> scores = new();

    // Lokal anlık cache — Photon gecikmesinden bağımsız
    private int _localKills  = 0;
    private int _localDeaths = 0;
    private int _localMoney  = 800;

    public const string KEY_KILLS  = "kills";
    public const string KEY_DEATHS = "deaths";
    public const string KEY_MONEY  = "money";
    public const string KEY_TEAM   = "team";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        // Lokal cache'i başlat
        _localKills  = 0;
        _localDeaths = 0;
        _localMoney  = 800;

        // Photon'a da yaz
        SetLocalPlayerProperty(KEY_KILLS,  _localKills);
        SetLocalPlayerProperty(KEY_DEATHS, _localDeaths);
        SetLocalPlayerProperty(KEY_MONEY,  _localMoney);
    }

    // ── Property Güncelleme ───────────────────────────────────────────

    public void AddKill()
    {
        _localKills++;
        SetLocalPlayerProperty(KEY_KILLS, _localKills);
    }

    public void AddDeath()
    {
        _localDeaths++;
        SetLocalPlayerProperty(KEY_DEATHS, _localDeaths);
    }

    public void AddMoney(int amount)
    {
        _localMoney = Mathf.Max(0, _localMoney + amount);
        SetLocalPlayerProperty(KEY_MONEY, _localMoney);
    }

    public void SpendMoney(int amount)
    {
        _localMoney = Mathf.Max(0, _localMoney - amount);
        SetLocalPlayerProperty(KEY_MONEY, _localMoney);
    }

    // ── Raund Para Bonusları ──────────────────────────────────────────

    /// Raund kazananlar +3000
    public void AddRoundWinBonus()  => AddMoney(3000);

    /// Raund kaybedenler +1900 (Valorant mantığı)
    public void AddRoundLossBonus() => AddMoney(1900);

    /// Bomba kuran saldırgan takım +300
    public void AddBombPlantBonus() => AddMoney(300);

    // Lokal cache'ten anında oku — Photon gecikmesi yok
    public int GetLocalMoney()  => _localMoney;
    public int GetLocalKills()  => _localKills;
    public int GetLocalDeaths() => _localDeaths;

    public void SetTeam(string team)
    {
        SetLocalPlayerProperty(KEY_TEAM, team);
    }

    // ── Photon Callbacks ──────────────────────────────────────────────

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        UpdateCache(targetPlayer);
        RefreshHUD(targetPlayer);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateCache(newPlayer);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        scores.Remove(otherPlayer.ActorNumber);
        RefreshScoreboard();
    }

    // ── Cache ─────────────────────────────────────────────────────────

    private void UpdateCache(Player player)
    {
        if (!scores.ContainsKey(player.ActorNumber))
            scores[player.ActorNumber] = new PlayerScore { Name = player.NickName };

        PlayerScore ps = scores[player.ActorNumber];
        ps.Name   = player.NickName;
        ps.Kills  = GetInt(player, KEY_KILLS);
        ps.Deaths = GetInt(player, KEY_DEATHS);
        ps.Money  = GetInt(player, KEY_MONEY);
        ps.Team   = GetString(player, KEY_TEAM);
    }

    private void RefreshHUD(Player player)
    {
        if (HUDManager.Instance == null) return;

        if (scores.TryGetValue(player.ActorNumber, out PlayerScore ps))
            HUDManager.Instance.UpdateScoreboard(ps.Name, ps.Kills, ps.Deaths, ps.Team);

        RefreshScoreboard();
    }

    private void RefreshScoreboard()
    {
        if (HUDManager.Instance == null) return;
        foreach (var kv in scores)
            HUDManager.Instance.UpdateScoreboard(kv.Value.Name, kv.Value.Kills, kv.Value.Deaths, kv.Value.Team);
    }

    // ── Sıralama ──────────────────────────────────────────────────────

    public List<PlayerScore> GetSortedScores()
    {
        var list = new List<PlayerScore>(scores.Values);
        list.Sort((a, b) => b.Kills.CompareTo(a.Kills));
        return list;
    }

    public (int red, int blue) GetTeamScores()
    {
        int red = 0, blue = 0;
        foreach (var ps in scores.Values)
        {
            if (ps.Team == "Red")  red  += ps.Kills;
            if (ps.Team == "Blue") blue += ps.Kills;
        }
        return (red, blue);
    }

    // ── Yardımcılar ───────────────────────────────────────────────────

    private static void SetLocalPlayerProperty(string key, object value)
    {
        Hashtable props = new() { { key, value } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    private static int GetInt(Player player, string key)
    {
        if (player.CustomProperties.TryGetValue(key, out object v))
            return v is int i ? i : 0;
        return 0;
    }

    private static string GetString(Player player, string key)
    {
        if (player.CustomProperties.TryGetValue(key, out object v))
            return v as string ?? "None";
        return "None";
    }
}