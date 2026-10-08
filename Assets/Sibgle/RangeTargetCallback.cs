using UnityEngine;
using System;

public class RangeTargetCallback : MonoBehaviour
{
    public Action onDied;

    // RangeTarget bu mesajı gönderir
    private void OnTargetDied(RangeTarget t)
    {
        onDied?.Invoke();
    }
}
