using UnityGameFramework.Runtime;

public enum VictoryConditionType { OccupySpecificBuildings, SurviveAmountDays, CollectAmountResources, KillSpecificUnits }
public enum FailConditionType { LoseSpecificBuildings, ArriveAmountDays, ConsumeAmountResources, LoseHero }

public class GameEndManager : GameFrameworkComponent { }