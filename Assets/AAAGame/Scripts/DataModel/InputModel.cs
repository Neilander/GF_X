using System.Collections;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;

public class InputModel : DataModelBase
{
    public Fix64 MoveX { get; set; }
    public Fix64 MoveY { get; set; }
    
    public bool InteractionPressed { get; set; }

    public void Reset()
    {
        MoveX = Fix64.Zero;
        MoveY = Fix64.Zero;
        InteractionPressed = false;
    }

    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        Reset();
    }

    protected override void OnRelease()
    {
        base.OnRelease();
        Reset();
    }
}
