using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using BepInEx;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using DrakiaXYZ.TaskListFixes.Comparers;
using EFT;
using Comfort.Common;
using System.Collections;

namespace DrakiaXYZ.TaskListFixes
{
    [BepInPlugin("xyz.drakia.tasklistfixes", "DrakiaXYZ-TaskListFixes", "1.6.0")]
    [BepInDependency("com.SPT.core", "3.11.0")]
    public class TaskListFixesPlugin : BaseUnityPlugin
    {
        // Note: We use a cached quest progress dictionary because fetching quest progress actually
        //       triggers a calculation any time it's read
        public static readonly Dictionary<QuestClass, double> QuestProgressCache = new Dictionary<QuestClass, double>();

        private static MethodInfo _stringLocalizedMethod;

        private void Awake()
        {
            Settings.Init(Config);

            Type[] localizedParams = new Type[] { typeof(string), typeof(string) };
            Type stringLocalizeClass = PatchConstants.EftTypes.First(x => x.GetMethod("Localized", localizedParams) != null);
            _stringLocalizedMethod = AccessTools.Method(stringLocalizeClass, "Localized", localizedParams);

            new TasksScreenShowPatch().Enable();
            new QuestProgressViewPatch().Enable();
            new QuestsSortPanelSortPatch().Enable();
            new QuestsSortPanelShowRestoreSortPatch().Enable();
            new TasksPanelSortPatch().Enable();
        }

        public static bool HandleNullOrEqualQuestCompare(QuestClass quest1, QuestClass quest2, out int result)
        {
            if (quest1 == quest2)
            {
                result = 0;
                return true;
            }

            if (quest1 == null)
            {
                result = -1;
                return true;
            }

            if (quest2 == null)
            {
                result = 1;
                return true;
            }

            result = 0;
            return false;
        }

        public static string Localized(string input)
        {
            return (string)_stringLocalizedMethod.Invoke(null, new object[] { input, null });
        }
    }

    // Allow restoring the sort order to the last used ordering
    class QuestsSortPanelShowRestoreSortPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(QuestsSortPanel)).Single(x => x.Name == "Show");
        }

        [PatchPrefix]
        public static void PatchPrefix(ref EQuestsSortType defaultSortingType, ref bool defaultAscending)
        {
            // If we're not remembering sorting, do nothing
            if (!Settings.RememberSorting.Value) { return; }

            // Only restore these if we have a stored value
            if (Settings._LastSortBy.Value >= 0)
            {
                defaultSortingType = (EQuestsSortType)Settings._LastSortBy.Value;
                defaultAscending = Settings._LastSortAscend.Value;
            }
        }
    }

    // Handle the sort call, storing the sort value and using our own comparers
    class TasksPanelSortPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TasksPanel), "method_1");
        }

        [PatchPrefix]
        public static bool PatchPrefix(EQuestsSortType sortType, bool sortDirection, 
            ref EQuestsSortType ___equestsSortType_0, ref bool ___bool_0, GClass3535<QuestClass, NotesTask> ___gclass3535_0,
            Dictionary<QuestClass, bool> ___dictionary_0)
        {
            // If we're remembering sort value, store it now
            if (Settings.RememberSorting.Value)
            {

                Settings._LastSortBy.Value = (int)sortType;
                Settings._LastSortAscend.Value = sortDirection;
            }

            // Re-implement base sorting behaviour using our own comparers
            ___equestsSortType_0 = sortType;
            ___bool_0 = sortDirection;
            IComparer<QuestClass> comparer;
            switch (sortType)
            {
                case EQuestsSortType.Trader:
                    comparer = new QuestTraderComparer();
                    break;
                case EQuestsSortType.Type:
                    comparer = new QuestTypeComparer();
                    break;
                case EQuestsSortType.Task:
                    comparer = new QuestNameComparer();
                    break;
                case EQuestsSortType.Location:
                    string locationId = (IsInRaid()) ? Singleton<AbstractGame>.Instance.LocationObjectId : null;
                    comparer = new QuestLocationComparer(locationId);
                    break;
                case EQuestsSortType.Status:
                    comparer = new QuestStatusComparer();
                    break;
                case EQuestsSortType.Progress:
                    comparer = new QuestProgressComparer();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            List<QuestClass> list = ___gclass3535_0.Keys;
            list.Sort(comparer);
            if (___bool_0)
            {
                list.Reverse();
            }
            list = list.OrderBy(quest => !___dictionary_0[quest]).ToList<QuestClass>();
            ___gclass3535_0.UpdateOrder(list);

            return false;
        }

        private static bool IsInRaid()
        {
            AbstractGame instance = Singleton<AbstractGame>.Instance;
            return instance is Interface10 || instance is LocalGame;
        }
    }

    // Patch used for clearing our cached quest progress data
    class TasksScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TasksScreen), "Show");
        }

        [PatchPrefix]
        public static void PatchPrefix()
        {
            TaskListFixesPlugin.QuestProgressCache.Clear();
        }
    }

    // Patch used to cache quest progress any time a QuestProgressView is shown
    class QuestProgressViewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestProgressView), "Show");
        }

        [PatchPostfix]
        public static void PatchPostfix(QuestClass quest, TextMeshProUGUI ____percentages)
        {
            // Luckily we can just go based on the text in the _percentages textmesh, because it's the progress as a percentage
            if (Double.TryParse(____percentages.text, out double progress))
            {
                TaskListFixesPlugin.QuestProgressCache[quest] = progress;
            }
        }
    }

    // Patch used to change the default ordering when sorting by a new column
    class QuestsSortPanelSortPatch : ModulePatch
    {
        private static FieldInfo _sortDescendField;
        private static FieldInfo _filterButtonField;
        protected override MethodBase GetTargetMethod()
        {
            Type targetType = typeof(QuestsSortPanel).BaseType;
            _sortDescendField = AccessTools.GetDeclaredFields(targetType).First(x => x.FieldType == typeof(bool));
            _filterButtonField = AccessTools.GetDeclaredFields(targetType).First(x => x.FieldType == typeof(FilterButton));

            return AccessTools.Method(targetType, "method_1");
        }

        [PatchPrefix]
        public static void PatchPrefix(QuestsSortPanel __instance, EQuestsSortType sortType, FilterButton button)
        {
            // If we're restoring the sort order, and we're sorting by the same column as our stored one, don't change the default sort order here
            if (Settings.RememberSorting.Value && Settings._LastSortBy.Value == (int)sortType)
            {
                return;
            }

            FilterButton activeFilterButton = _filterButtonField.GetValue(__instance) as FilterButton;

            // If the button is different than the stored filterButton_0, it means we're sorting by a new column.
            if (Settings.NewDefaultOrder.Value && button != activeFilterButton)
            {
                switch (sortType)
                {
                    // Sort these default ascending
                    case EQuestsSortType.Task:
                    case EQuestsSortType.Trader:
                    case EQuestsSortType.Location:
                        _sortDescendField.SetValue(__instance, false);
                        break;

                    // Sort these default descending
                    case EQuestsSortType.Progress:
                    case EQuestsSortType.Status:
                        _sortDescendField.SetValue(__instance, true);
                        break;
                }
            }
        }
    }

}
