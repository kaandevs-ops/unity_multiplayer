using UnityEngine;

public class RangeEnemy : MonoBehaviour
{
    public float moveSpeed = 2f;
    private Transform player;
    private RangeTarget target;

    private void Start()
    {
        target = GetComponent<RangeTarget>();

        // Oyuncuyu bul — CharacterController olan obje
        var cc = FindFirstObjectByType<CharacterController>();
        if (cc != null) player = cc.transform;
    }

    private void Update()
    {
        if (player == null || target == null) return;
        if (target.GetIsDead()) return;

        // Oyuncuya doğru yürü
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;
        transform.position += direction * moveSpeed * Time.deltaTime;
        transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));
    }
}
