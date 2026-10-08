using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviourPunCallbacks
{
    public GameObject playerPrefab;
    private bool spawned = false;

    private void Start()
    {
        Debug.Log($"[Spawner] Start - InRoom: {PhotonNetwork.InRoom}, spawned: {spawned}");
        if (PhotonNetwork.InRoom && !spawned)
            StartCoroutine(WaitForTeamAndSpawn());
    }

    public override void OnJoinedRoom()
    {
        if (!spawned)
            StartCoroutine(WaitForTeamAndSpawn());
    }

    private IEnumerator WaitForTeamAndSpawn()
    {
        if (spawned) yield break;
        spawned = true; // Race condition'ı önlemek için hemen set et

        // 1.5f yerine 0 — sadece bir frame bekle
        yield return null;

        float timeout = 5f;
        while (timeout > 0f)
        {
            if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object val))
            {
                if (val is string team && team != "None" && !string.IsNullOrEmpty(team))
                    break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }

        SpawnPlayer();
    }

    private void SpawnPlayer()
    {
        string team = "Red";
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            team = t as string ?? "Red";

        Debug.Log($"[Spawn] Takım: {team}");

        bool isAttacker = RoundManager.Instance != null
    ? (RoundManager.Instance.IsRedAttacking() ? team == "Red" : team == "Blue")
    : team == "Red";
        string tag = isAttacker ? "AttackerSpawn" : "DefenderSpawn";
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag(tag);

        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[Spawn] {tag} bulunamadı, RedSpawn kullanılıyor");
            spawnPoints = GameObject.FindGameObjectsWithTag("AttackerSpawn");
        }

        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
        PhotonNetwork.Instantiate(playerPrefab.name, spawnPoint.position, spawnPoint.rotation);
    }

    public void RespawnPlayer()
    {
        SpawnPlayer();
    }
}