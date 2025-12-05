using UnityEngine;

[CreateAssetMenu(fileName = "NoAtkFactory", menuName = "Atk Factory/NoAtk")]
public class NoAtkFactory : AtkCompFactory
{
    public override IAtkComp CreateAtkComp(GameObject gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        
        return gmo.AddComponent<NoAtkComp>();
    }
}

public abstract class AtkCompFactory : ScriptableObject
{
    public abstract IAtkComp CreateAtkComp(GameObject gmo);
}


