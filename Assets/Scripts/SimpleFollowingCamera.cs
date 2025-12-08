using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleFollowingCamera : MonoBehaviour
{
    private Transform playerTransform;
    // Start is called before the first frame update
    void Start()
    {
        playerTransform = GameObject.FindWithTag("Player").transform;
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
    }
}
