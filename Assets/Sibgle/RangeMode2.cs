using UnityEngine;
using System.Collections.Generic;

public class RangeMode2 : MonoBehaviour
{
    [Header("Düşman Prefab")]
    public GameObject targetPrefab;

    [Header("Spawn Noktaları")]
    public Transform spawnPointsParent; // EnemySpawnPoints objesi

    [Header("Ayarlar")]
    public int enemyCount = 3;
    public float moveSpeed = 2f;

    private List<Transform> spawnPoints = new List<Transform>();
    private bool isActive = false;

    private void Start()
    {
        if (spawnPointsParent != null)
        {
            foreach (Transform child in spawnPointsParent)
                spawnPoints.Add(child);
        }
    }

    public void StartMode()
    {
        isActive = true;

        for (int i = 0; i < enemyCount; i++)
            SpawnEnemy();
    }

    public void StopMode()
    {
        isActive = false;

        // Tüm aktif düşmanları sil
        foreach (var enemy in FindObjectsByType<RangeEnemy>(FindObjectsSortMode.None))
            Destroy(enemy.gameObject);
    }

    private void SpawnEnemy()
    {
        if (!isActive || spawnPoints.Count == 0 || targetPrefab == null) return;

        Transform sp = spawnPoints[Random.Range(0, spawnPoints.Count)];
        Vector3 spawnPos = sp.position + Vector3.up * 1f;
        GameObject enemy = Instantiate(targetPrefab, spawnPos, sp.rotation);

        RangeTarget rt = enemy.GetComponent<RangeTarget>();
        if (rt == null) rt = enemy.AddComponent<RangeTarget>();

        RangeEnemy re = enemy.AddComponent<RangeEnemy>();
        re.moveSpeed = moveSpeed;

        RangeTargetCallback cb = enemy.AddComponent<RangeTargetCallback>();
        cb.onDied = () =>
        {
            Destroy(enemy, 0.1f);
            if (isActive) SpawnEnemy();
        };
    }
}