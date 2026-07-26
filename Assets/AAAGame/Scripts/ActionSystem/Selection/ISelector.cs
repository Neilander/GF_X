using UnityEngine;
using System.Collections.Generic;

public interface ISelector
{
    void ReleaseSelection();
    void Activate();
    void ClearSelected();
    void ChangeRange(Vector3 ratio);

    int GetEntityID();
    
    void SetPosition(Vector3 pos);
}

public interface ISelector<T> : ISelector where T: ISelectable
{
    int GetSelected(out List<T> mailbox);

    void Activate(List<T> excludes);
}
