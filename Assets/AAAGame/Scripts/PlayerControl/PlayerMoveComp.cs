using System.Collections;
using System.Collections.Generic;

public class PlayerMoveComp : IMoveComp
{
    private InputModel _inputModel;
    public void Move()
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }
        
        //GF.Log("移动按键的值是"+ _inputModel.MoveX +","+_inputModel.MoveY);
        //GF.Log("交互按键的值是"+ _inputModel.InteractionPressed);

    }
}