using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TeleportBehavior : MonoBehaviour
{
    [SerializeField] private GameObject player; // Assign the player object here
    public bool canTeleport = true;
    private MapGenerator mapGenerator;

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
    }
    // Update is called once per frame
    void Update()
    {
        if (canTeleport && Input.GetKeyDown(KeyCode.P))
        {
            TeleportToSpawn();
        }
    }

    public void TeleportToSpawn()
    {
        player.transform.position = mapGenerator.spawnPos;
    }
}

