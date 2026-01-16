using UnityEngine;
using System.Collections.Generic;

public interface ISelector
{
    void ReleaseSelection();
    void Activate();
    void ClearSelected();
    void ChangeRange(Vector3 ratio);
}

public interface ISelector<T> : ISelector where T: ISelectable
{
    bool Validate(GameObject obj);
    int GetSelected(out List<T> mailbox);

    void Activate(List<T> excludes);
}
