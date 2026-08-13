using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleFollowingCamera : MonoBehaviour
{
    private Transform playerTransform;
    // Start is called before the first frame update
    void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            throw new System.InvalidOperationException("SimpleFollowingCamera.Start failed: player object is missing.");

        Transform presentation = player.transform.Find("Display");
        if (presentation == null)
            throw new System.InvalidOperationException("SimpleFollowingCamera.Start failed: player presentation transform is missing.");
        if (CameraController.Instance == null)
            throw new System.InvalidOperationException("SimpleFollowingCamera.Start failed: CameraController.Instance is null.");

        CameraController.Instance.SetFollowTargetLegacyIsometric(presentation, false);
        // playerTransform = GameObject.FindWithTag("Player").transform;
    }

    // // Update is called once per frame
    // void Update()
    // {
    //     transform.position = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
    // }
}
