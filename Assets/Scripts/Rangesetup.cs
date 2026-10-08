using UnityEngine;
using InfimaGames.LowPolyShooterPack;
using System.Collections;

public class RangeSetup : MonoBehaviour
{
    private void Start()
    {
        StartCoroutine(Setup());
    }

    private IEnumerator Setup()
    {
        // ScoreManager'ın Start()'ı çalışsın diye bekle
        yield return new WaitForSeconds(0.2f);

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.AddMoney(99999);

        // Silahlar spawn olana kadar bekle, bulununca infinite yap
        StartCoroutine(WaitAndSetInfiniteAmmo());
    }

    private IEnumerator WaitAndSetInfiniteAmmo()
    {
        Weapon[] weapons = new Weapon[0];

        while (weapons.Length == 0)
        {
            yield return new WaitForSeconds(0.5f);
            weapons = FindObjectsByType<Weapon>(FindObjectsSortMode.None);
        }

        foreach (var weapon in weapons)
            weapon.SetInfiniteAmmo();
    }
}