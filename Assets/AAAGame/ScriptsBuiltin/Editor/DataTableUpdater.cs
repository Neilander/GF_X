#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UGF.EditorTools
{
    public static partial class DataTableUpdater
    {
        static IList<string> tableFileChangedList;
        static IList<string> configFileChangedList;
        static IList<string> languageFileChangedList;
        static FileSystemWatcher tableWatcher;
        static FileSystemWatcher configWatcher;
        static FileSystemWatcher languageWatcher;

        static bool isInitialized = false;
        static AppConfigs appConfigs = null;
        [InitializeOnLoadMethod]
        private static void Init()
        {
            if (isInitialized) return;
            InitGlobalCulture();
            tableFileChangedList = new List<string>();
            configFileChangedList = new List<string>();
            languageFileChangedList = new List<string>();
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                StartWatchers();
            appConfigs = AppConfigs.GetInstanceEditor();
            isInitialized = true;
        }

        private static void StartWatchers()
        {
            if (tableWatcher != null && configWatcher != null && languageWatcher != null)
                return;
            if (tableWatcher != null || configWatcher != null || languageWatcher != null)
                throw new InvalidOperationException("DataTableUpdater file watchers are already running.");

            tableWatcher = new FileSystemWatcher(ConstEditor.DataTableExcelPath, "*.xlsx")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            tableWatcher.Changed += OnDataTableChanged;
            tableWatcher.Deleted += OnDataTableChanged;

            configWatcher = new FileSystemWatcher(ConstEditor.ConfigExcelPath, "*.xlsx")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            configWatcher.Changed += OnConfigChanged;
            configWatcher.Deleted += OnConfigChanged;

            languageWatcher = new FileSystemWatcher(ConstEditor.LanguageExcelPath, "*.xlsx")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            languageWatcher.Changed += OnLanguageChanged;
            languageWatcher.Deleted += OnLanguageChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                StopWatchers();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
                StartWatchers();
        }

        private static void StopWatchers()
        {
            DisposeWatcher(ref tableWatcher);
            DisposeWatcher(ref configWatcher);
            DisposeWatcher(ref languageWatcher);
        }

        private static void DisposeWatcher(ref FileSystemWatcher watcher)
        {
            if (watcher == null)
                return;

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
        }
        static void InitGlobalCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
        }

        private static void OnUpdate()
        {
            if (!isInitialized) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (tableFileChangedList.Count > 0)
            {


                 var changedFiles = GetMainExcelFiles(GameDataType.DataTable, appConfigs.DataTables, tableFileChangedList);
               
                  GameDataGenerator.RefreshAllDataTable(changedFiles);
                 if (changedFiles.Contains(ConstEditor.UITableExcelFullPath))
                 {
                     GameDataGenerator.GenerateUIFormNamesScript();
                 }
                 if (changedFiles.Contains(ConstEditor.EntityGroupTableExcelFullPath) ||
                         changedFiles.Contains(ConstEditor.SoundGroupTableExcelFullPath) ||
                         changedFiles.Contains(ConstEditor.UIGroupTableExcelFullPath) ||
                         changedFiles.Contains(ConstEditor.EntityGroupTableExcelFullPath))
                 {
                     GameDataGenerator.GenerateGroupEnumScript();
                 }
                 foreach (var item in changedFiles)
                 {
                     GFBuiltin.Log($"-----------------自动刷新DataTable:{item}-----------------");
                 }
                tableFileChangedList.Clear();
            }
            if (configFileChangedList.Count > 0)
            {
                var changedFiles = GetMainExcelFiles(GameDataType.Config, appConfigs.Configs, configFileChangedList);
                GameDataGenerator.RefreshAllConfig(changedFiles);
                foreach (var item in changedFiles)
                {
                    GFBuiltin.Log($"-----------------自动刷新Config:{item}-----------------");
                }
                configFileChangedList.Clear();
            }
            if (languageFileChangedList.Count > 0)
            {
                var changedFiles = GetMainExcelFiles(GameDataType.Language, appConfigs.Languages, languageFileChangedList);
                GameDataGenerator.RefreshAllLanguage(changedFiles);
                foreach (var item in changedFiles)
                {
                    GFBuiltin.Log($"-----------------自动刷新Language:{item}-----------------");
                }
                languageFileChangedList.Clear();
            }
        }
        /// <summary>
        /// 根据改变的Excel列表获取所有对应的主文件列表
        /// </summary>
        /// <param name="tp"></param>
        /// <param name="relativeMainFiles"></param>
        /// <param name="changedFiles"></param>
        /// <returns></returns>
        private static IList<string> GetMainExcelFiles(GameDataType tp, IList<string> relativeMainFiles, IList<string> changedFiles)
        {
            IList<string> result = new List<string>();
            foreach (var changedFile in changedFiles)
            {
                var relativePathNoExt = GameDataGenerator.GetGameDataExcelRelativePath(tp, changedFile);
                
                foreach (var mainName in relativeMainFiles)
                {
                   // Debug.Log(mainName+"  "+relativePathNoExt);
                    if (relativePathNoExt.CompareTo(mainName) == 0 || relativePathNoExt.StartsWith(mainName + ConstBuiltin.AB_TEST_TAG))
                    {
                        
                        var mainExcelFullPath = GameDataGenerator.GameDataExcelRelative2FullPath(tp, mainName);
                        if (!result.Contains(mainExcelFullPath))
                        {
                            result.Add(mainExcelFullPath);
                        }
                    }
                }
            }
            return result;
        }

        private static void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            var fName = Path.GetFileNameWithoutExtension(e.Name);
            if (!fName.StartsWith("~$"))
            {
                configFileChangedList.Add(e.FullPath);
            }
        }
        private static void OnDataTableChanged(object sender, FileSystemEventArgs e)
        {
            var fName = Path.GetFileNameWithoutExtension(e.Name);
            if (!fName.StartsWith("~$"))
            {
                tableFileChangedList.Add(e.FullPath);
            }
        }

        private static void OnLanguageChanged(object sender, FileSystemEventArgs e)
        {
            var fName = Path.GetFileNameWithoutExtension(e.Name);
            if (!fName.StartsWith("~$"))
            {
                languageFileChangedList.Add(e.FullPath);
            }
        }
    }

}
#endif
