public class Faction //阵营，或称势力
{
    public int TeamID { get; private set; } //一个队伍可由多个阵营组成。通常玩家所在的队伍ID为0，敌对势力为1，2等。
    public Faction(int teamID)
    {
        TeamID = teamID;
    }
}