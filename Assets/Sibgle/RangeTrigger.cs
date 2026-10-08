using UnityEngine;

public class RangeTrigger : MonoBehaviour
{
    public enum Mode { Mode1, Mode2 }
    public Mode mode;

    private RangeMode1 mode1;
    private RangeMode2 mode2;

    private void Start()
    {
        mode1 = FindFirstObjectByType<RangeMode1>();
        mode2 = FindFirstObjectByType<RangeMode2>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Önce ikisini de durdur
        mode1?.StopMode();
        mode2?.StopMode();

        // Seçilen modu başlat
        if (mode == Mode.Mode1) mode1?.StartMode();
        else                    mode2?.StartMode();
    }
}
