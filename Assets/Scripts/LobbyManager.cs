using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyManager : MonoBehaviour
{
    [Header("Ana Panel")]
    public TMP_InputField playerNameInput;
    public TMP_InputField roomNameInput;
    public TMP_Dropdown   maxPlayersDropdown;
    public TMP_Dropdown   gameModeDropdown;
    public Button         createButton;
    public Button         joinButton;
    public Button         randomJoinButton;
    public TMP_Text       statusText;

    [Header("Oda Listesi")]
    public GameObject roomListPanel;
    public Transform  roomListContent;
    public GameObject roomRowPrefab;

    [Header("Takım Seçimi")]
    public GameObject teamSelectPanel;
    public Button     redTeamButton;
    public Button     blueTeamButton;
    public TMP_Text   redCountText;
    public TMP_Text   blueCountText;
    public Button     startGameButton;   // Yeni — Inspector'da bağla
    public TMP_Text   startGameStatusText; // Yeni — "Herkes takım seçmedi" uyarısı

    [Header("Karakter Seçimi")]
    public GameObject        characterSelectPanel;
    public List<Button>      characterButtons;
    public List<string>      characterPrefabNames;
    public TMP_Text          selectedCharacterText;
    private string           selectedCharacter = "";

    private readonly Dictionary<string, GameObject> roomRows = new();
    private int[] maxPlayerOptions = { 2, 4, 6, 8, 10 };

    private void Start()
    {
        // Lobi sahnesinde mouse her zaman serbest
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        SetupDropdowns();

        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i].onClick.AddListener(() => OnSelectCharacter(idx));
        }

        if (teamSelectPanel)      teamSelectPanel.SetActive(false);
        if (characterSelectPanel) characterSelectPanel.SetActive(false);

        if (PlayerPrefs.HasKey("PlayerName"))
            if (playerNameInput) playerNameInput.text = PlayerPrefs.GetString("PlayerName");

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnRoomListUpdated += RefreshRoomList;
            NetworkManager.Instance.OnJoinFailed      += OnJoinFailed;
        }

        if (PhotonNetwork.InRoom)
            ShowTeamSelectPanel();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnRoomListUpdated -= RefreshRoomList;
            NetworkManager.Instance.OnJoinFailed      -= OnJoinFailed;
        }
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && teamSelectPanel != null && teamSelectPanel.activeSelf)
        {
            UpdateTeamCounts();
            UpdateStartButton();
        }
    }

    private void SetupDropdowns()
    {
        if (maxPlayersDropdown)
        {
            maxPlayersDropdown.ClearOptions();
            List<string> opts = new();
            foreach (int n in maxPlayerOptions) opts.Add(n + " oyuncu");
            maxPlayersDropdown.AddOptions(opts);
            maxPlayersDropdown.value = 4; // Varsayılan 10 oyuncu (index 4)
        }

        if (gameModeDropdown)
        {
            gameModeDropdown.ClearOptions();
            gameModeDropdown.AddOptions(new List<string> { "FFA", "Team Deathmatch" });
        }
    }

    public void OnClickCreate()
    {
        if (!ValidateInputs()) return;

        string name       = playerNameInput.text.Trim();
        string room       = roomNameInput.text.Trim();
        int    maxPlayers = maxPlayerOptions[maxPlayersDropdown.value];
        string mode       = gameModeDropdown.value == 0 ? "FFA" : "TeamDeathmatch";

        PlayerPrefs.SetString("PlayerName", name);
        PhotonNetwork.NickName = name;

        SetStatus("Oda oluşturuluyor...");
        NetworkManager.Instance.CreateRoom(room, maxPlayers, mode);
    }

    public void OnClickJoin()
    {
        if (!ValidateInputs()) return;

        string name = playerNameInput.text.Trim();
        string room = roomNameInput.text.Trim();

        PlayerPrefs.SetString("PlayerName", name);
        PhotonNetwork.NickName = name;

        SetStatus("Odaya girilmeye çalışılıyor...");
        NetworkManager.Instance.JoinRoom(room);
    }

    public void OnClickRandomJoin()
    {
        if (string.IsNullOrWhiteSpace(playerNameInput?.text)) { SetStatus("İsim gir!"); return; }

        string name = playerNameInput.text.Trim();
        PlayerPrefs.SetString("PlayerName", name);
        PhotonNetwork.NickName = name;

        SetStatus("Rastgele oda aranıyor...");
        NetworkManager.Instance.JoinRandomRoom();
    }

    private void RefreshRoomList()
    {
        if (roomListContent == null || roomRowPrefab == null) return;

        List<string> toRemove = new();
        foreach (var kv in roomRows)
            if (NetworkManager.Instance.CurrentRoomList.TrueForAll(r => r.Name != kv.Key))
                toRemove.Add(kv.Key);

        foreach (string k in toRemove)
        {
            if (roomRows[k]) Destroy(roomRows[k]);
            roomRows.Remove(k);
        }

        foreach (RoomInfo info in NetworkManager.Instance.CurrentRoomList)
        {
            if (!roomRows.ContainsKey(info.Name))
            {
                GameObject row = Instantiate(roomRowPrefab, roomListContent);
                roomRows[info.Name] = row;

                TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();
                Button joinBtn   = row.GetComponentInChildren<Button>();

                string capturedName = info.Name;
                joinBtn?.onClick.AddListener(() =>
                {
                    PhotonNetwork.NickName = playerNameInput != null ? playerNameInput.text.Trim() : "Oyuncu";
                    NetworkManager.Instance.JoinRoom(capturedName);
                });

                UpdateRoomRow(row, info);
            }
            else
            {
                UpdateRoomRow(roomRows[info.Name], info);
            }
        }
    }

    private static void UpdateRoomRow(GameObject row, RoomInfo info)
    {
        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();
        string mode = "FFA";
        if (info.CustomProperties.TryGetValue("mode", out object m)) mode = m as string ?? "FFA";

        if (texts.Length >= 1) texts[0].text = info.Name;
        if (texts.Length >= 2) texts[1].text = mode;
        if (texts.Length >= 3) texts[2].text = $"{info.PlayerCount}/{info.MaxPlayers}";
    }

    public void ShowTeamSelectPanel()
    {
        GameObject mainPanel = GameObject.Find("Panel");
        if (mainPanel) mainPanel.SetActive(false);

        if (teamSelectPanel) teamSelectPanel.SetActive(true);
        UpdateTeamCounts();
        UpdateStartButton();
    }

    private void OnClickTeam(string team)
    {
        NetworkManager.Instance?.JoinTeam(team);
        SetStatus($"{team} takımına katıldın!");
        UpdateTeamCounts();
        UpdateStartButton();
    }

    // ── Başlatma Butonu ───────────────────────────────────────────────

    private void UpdateStartButton()
    {
        if (startGameButton == null) return;

        // Sadece MasterClient görsün
        bool isMaster = PhotonNetwork.IsMasterClient;
        startGameButton.gameObject.SetActive(isMaster);

        if (!isMaster) return;

        bool allReady = AllPlayersSelectedTeam();
        startGameButton.interactable = allReady;

        if (startGameStatusText)
            startGameStatusText.text = allReady
                ? "Başlatmaya hazır!"
                : "Tüm oyuncular takım seçmeli...";
    }

    private bool AllPlayersSelectedTeam()
    {
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
                return false;
            string team = t as string;
            if (string.IsNullOrEmpty(team) || team == "None")
                return false;
        }
        return true;
    }

    public void OnClickStartGame()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (!AllPlayersSelectedTeam())
        {
            SetStatus("Tüm oyuncular takım seçmeli!");
            return;
        }

        NetworkManager.Instance?.TryLoadGameScene();
    }

    // ── Karakter Seçimi ───────────────────────────────────────────────

    public void ShowCharacterSelectPanel()
    {
        if (characterSelectPanel) characterSelectPanel.SetActive(true);
    }

    private void OnSelectCharacter(int index)
    {
        if (index < 0 || index >= characterPrefabNames.Count) return;
        selectedCharacter = characterPrefabNames[index];
        PlayerPrefs.SetString("SelectedCharacter", selectedCharacter);

        if (selectedCharacterText)
            selectedCharacterText.text = $"Seçili: {selectedCharacter}";

        for (int i = 0; i < characterButtons.Count; i++)
        {
            ColorBlock cb = characterButtons[i].colors;
            cb.normalColor = i == index ? Color.green : Color.white;
            characterButtons[i].colors = cb;
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(playerNameInput?.text)) { SetStatus("İsim gir!"); return false; }
        if (string.IsNullOrWhiteSpace(roomNameInput?.text))   { SetStatus("Oda adı gir!"); return false; }
        if (!PhotonNetwork.IsConnected)                        { SetStatus("Bağlantı yok!"); return false; }
        return true;
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log($"[Lobby] {msg}");
    }

    private void OnJoinFailed(string reason)
    {
        SetStatus($"Hata: {reason}");
    }

    public void OnClickRange()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("Range");
    }

    public void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnClickRedTeam()  => OnClickTeam("Red");
    public void OnClickBlueTeam() => OnClickTeam("Blue");
    private void UpdateTeamCounts()
{
    if (!PhotonNetwork.InRoom) return;
    
    int red = 0, blue = 0;
    foreach (var player in PhotonNetwork.PlayerList)
    {
        if (player.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
        {
            if (t as string == "Red") red++;
            if (t as string == "Blue") blue++;
        }
    }
    
    if (redCountText)  redCountText.text  = $"Kırmızı: {red}";
    if (blueCountText) blueCountText.text = $"Mavi: {blue}";
}
}