#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace UGF.EditorTools
{
    internal static class DefenseDataTableBootstrap
    {
        private const string RouteExcelPath = "AAAGameData/DataTables/Level/DefendRouteTable.xlsx";
        private const string GroupExcelPath = "AAAGameData/DataTables/Level/DefendAttackGroupTable.xlsx";
        private const string RouteCodePath = "Assets/AAAGame/Scripts/DataTable/Level/DefendRouteTable.cs";
        private const string GroupCodePath = "Assets/AAAGame/Scripts/DataTable/Level/DefendAttackGroupTable.cs";

        [InitializeOnLoadMethod]
        private static void ScheduleGenerationIfRequired()
        {
            if (File.Exists(RouteCodePath) && File.Exists(GroupCodePath))
                return;

            EditorApplication.delayCall += GenerateRequiredTables;
        }

        private static void GenerateRequiredTables()
        {
            if (!File.Exists(RouteExcelPath) || !File.Exists(GroupExcelPath))
                throw new FileNotFoundException("Defense data table source Excel is missing.");

            GameDataGenerator.RefreshAllDataTable(new[]
            {
                Path.GetFullPath(RouteExcelPath),
                Path.GetFullPath(GroupExcelPath)
            });
        }
    }
}
#endif
