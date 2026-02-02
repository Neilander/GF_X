using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimplePlayerController : MonoBehaviour
{
    private InputModel _inputModel;
    private Vector2 inputAxis;
    private Vector3 movementDirection;
    private Matrix4x4 RotationMatrix = Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0));
    private float moveSpeed = 30f;
    private Rigidbody rb;
    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
        }
        if (_inputModel != null)
        {
            GatherInput();
            Move();
        }
    }

    void GatherInput()
    {
        inputAxis = new FixVector2(_inputModel.MoveX, _inputModel.MoveY);
        movementDirection = RotationMatrix.MultiplyPoint3x4(new Vector3(inputAxis.x, 0, inputAxis.y));
        movementDirection.Normalize();
    }

    void Move()
    {
        rb.velocity = new Vector3(movementDirection.x * moveSpeed, rb.velocity.y, movementDirection.z * moveSpeed);
    }
}
