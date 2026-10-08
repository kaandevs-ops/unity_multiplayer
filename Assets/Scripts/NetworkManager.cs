using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// NetworkManager — Photon bağlantısı, oda yönetimi ve takım sistemi.
/// 
/// Oda Custom Properties:
///   "maxPlayers" → int (4, 8, 12, 16 …)
///   "mode"       → string ("FFA", "TeamDeathmatch")
///   "map"        → string ("Prototype Map", "Arena" …)
///   "redCount"   → int (Red takım oyuncu sayısı)
///   "blueCount"  → int (Blue takım oyuncu sayısı)
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance { get; private set; }

    // ── Oda Listesi (Lobby UI için) ───────────────────────────────────
    public List<RoomInfo> CurrentRoomList { get; private set; } = new();

    // ── Olaylar (LobbyManager UI'ı dinler) ───────────────────────────
    public System.Action OnRoomListUpdated;
    public new System.Action OnConnected;
    public System.Action<string> OnJoinFailed;

    // Oda sabitleri
    private const string PROP_MODE       = "mode";
    private const string PROP_MAP        = "map";
    private const string PROP_RED_COUNT  = "redCount";
    private const string PROP_BLUE_COUNT = "blueCount";

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.ConnectUsingSettings();
    }

    // ── Photon Temel Callbacks ────────────────────────────────────────

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Network] Master sunucusuna bağlandı.");
        PhotonNetwork.JoinLobby();
        OnConnected?.Invoke();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("[Network] Lobby'e girildi.");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        // Silinenleri çıkar, güncellenenleri ekle
        foreach (RoomInfo info in roomList)
        {
            if (info.RemovedFromList)
                CurrentRoomList.RemoveAll(r => r.Name == info.Name);
            else
            {
                CurrentRoomList.RemoveAll(r => r.Name == info.Name);
                CurrentRoomList.Add(info);
            }
        }
        OnRoomListUpdated?.Invoke();
    }

    public override void OnJoinedRoom()
{
    Debug.Log($"[Network] Odaya girildi: {PhotonNetwork.CurrentRoom.Name}");
    // Sahneye geçmiyoruz, önce takım seçimi
    LobbyManager lobbyManager = FindFirstObjectByType<LobbyManager>();
    if (lobbyManager != null)
        lobbyManager.ShowTeamSelectPanel();
}

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Network] Odaya girilemedi: {message}");
        OnJoinFailed?.Invoke(message);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Network] Oda oluşturulamadı: {message}");
        OnJoinFailed?.Invoke(message);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Network] {newPlayer.NickName} odaya katıldı. ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Network] {otherPlayer.NickName} odadan ayrıldı.");
        // Takım sayısını güncelle
        RecalculateTeamCounts();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Network] Odadan çıkıldı, Lobby sahnesine dönülüyor.");
        PhotonNetwork.LoadLevel("LobbyScene");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Network] Bağlantı kesildi: {cause}");
    }

    // ── Oda Oluşturma ─────────────────────────────────────────────────

    /// <param name="roomName">Oda adı</param>
    /// <param name="maxPlayers">Max oyuncu sayısı (2–16)</param>
    /// <param name="mode">"FFA" veya "TeamDeathmatch"</param>
    /// <param name="map">Sahne adı</param>
    public void CreateRoom(string roomName, int maxPlayers = 8,
                           string mode = "FFA", string map = "Prototype Map")
    {
        Hashtable customProps = new()
        {
            { PROP_MODE,       mode },
            { PROP_MAP,        map  },
            { PROP_RED_COUNT,  0    },
            { PROP_BLUE_COUNT, 0    },
        };

        // Lobby'de görünmesi için hangi property'lerin yayınlanacağını belirt
        string[] lobbyProps = { PROP_MODE, PROP_MAP };

        RoomOptions options = new()
        {
            MaxPlayers              = (byte)Mathf.Clamp(maxPlayers, 2, 16),
            CustomRoomProperties    = customProps,
            CustomRoomPropertiesForLobby = lobbyProps,
            IsVisible               = true,
            IsOpen                  = true,
        };

        PhotonNetwork.CreateRoom(roomName, options);
    }

    /// Oda adıyla direkt katıl
    public void JoinRoom(string roomName)
    {
        PhotonNetwork.JoinRoom(roomName);
    }

    /// Oda nesnesinden katıl (oda listesinden tıklanınca)
    public void JoinRoom(RoomInfo roomInfo)
    {
        PhotonNetwork.JoinRoom(roomInfo.Name);
    }

    /// Rastgele odaya katıl
    public void JoinRandomRoom()
    {
        PhotonNetwork.JoinRandomRoom();
    }

    public void LeaveRoom()
    {
        PhotonNetwork.LeaveRoom();
    }

    // ── Takım Sistemi ─────────────────────────────────────────────────

    /// <summary>
    /// Oyuncuyu verilen takıma atar.
    /// ScoreManager üzerinden Player Custom Property güncellenir.
    /// </summary>
    public void JoinTeam(string team) // "Red" | "Blue" | "None"
    {
        if (!PhotonNetwork.InRoom) return;

        ScoreManager.Instance?.SetTeam(team);
        RecalculateTeamCounts();

        Debug.Log($"[Network] {PhotonNetwork.LocalPlayer.NickName} → {team} takımı");
    }

    /// Kırmızı ve Mavi takımdaki oyuncu sayılarını oda property'sine yaz
    private void RecalculateTeamCounts()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int red = 0, blue = 0;
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            {
                if (t as string == "Red")  red++;
                if (t as string == "Blue") blue++;
            }
        }

        Hashtable props = new()
        {
            { PROP_RED_COUNT,  red  },
            { PROP_BLUE_COUNT, blue },
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // ── Bilgi Yardımcıları ────────────────────────────────────────────

    public int GetTeamCount(string team)
    {
        if (!PhotonNetwork.InRoom) return 0;
        string key = team == "Red" ? PROP_RED_COUNT : PROP_BLUE_COUNT;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object v))
            return v is int i ? i : 0;
        return 0;
    }

    public string GetRoomMode()
    {
        if (!PhotonNetwork.InRoom) return "FFA";
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_MODE, out object v))
            return v as string ?? "FFA";
        return "FFA";
    }

    public bool IsTeamMode() => GetRoomMode() == "TeamDeathmatch";
    public void TryLoadGameScene()
{
    if (PhotonNetwork.IsMasterClient)
        PhotonNetwork.LoadLevel("Prototype Map");
}
}