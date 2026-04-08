public class Faction
{
    public int TeamID { get; private set; } //通常玩家所在的队伍ID为0，敌对势力为1，2等。
    public Faction(int teamID)
    {
        TeamID = teamID;
    }
}