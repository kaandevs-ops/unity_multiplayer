using UnityEngine;
using System.Collections.Generic;

public class RangeMode1 : MonoBehaviour
{
    [Header("Hedef Prefab")]
    public GameObject targetPrefab;

    [Header("Spawn Noktaları")]
    public Transform spawnPointsParent; // SpawnPoints objesi

    [Header("Ayarlar")]
    public int targetCount = 3; // Aynı anda kaç hedef olsun

    private List<Transform> spawnPoints = new List<Transform>();
    private List<GameObject> activeTargets = new List<GameObject>();

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
        activeTargets.Clear();

        for (int i = 0; i < targetCount; i++)
            SpawnTarget();
    }

    public void StopMode()
    {
        isActive = false;

        foreach (var t in activeTargets)
            if (t != null) Destroy(t);

        activeTargets.Clear();
    }

    private void SpawnTarget()
    {
        if (!isActive || spawnPoints.Count == 0 || targetPrefab == null) return;

        Transform sp = GetRandomSpawnPoint();
        Vector3 spawnPos = sp.position + Vector3.up * 1f;
        GameObject target = Instantiate(targetPrefab, spawnPos, sp.rotation);
        RangeTarget rt = target.GetComponent<RangeTarget>();
        if (rt == null) rt = target.AddComponent<RangeTarget>();

        // Ölünce bu modu haberdar et
        RangeTargetCallback cb = target.AddComponent<RangeTargetCallback>();
        cb.onDied = () =>
        {
            activeTargets.Remove(target);
            Destroy(target, 0.1f);
            if (isActive) SpawnTarget(); // Başka yerde yeni biri çıkar
        };

        activeTargets.Add(target);
    }

    private Transform GetRandomSpawnPoint()
    {
        return spawnPoints[Random.Range(0, spawnPoints.Count)];
    }

    // RangeManager tarafından çağrılır
    public void OnTargetDied(RangeTarget target)
    {
        // RangeTargetCallback hallediyor
    }
}