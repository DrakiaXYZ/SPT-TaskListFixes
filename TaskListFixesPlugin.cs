using BepInEx;
using Comfort.Common;
using DrakiaXYZ.TaskListFixes.Comparers;
using DrakiaXYZ.TaskListFixes.VersionChecker;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;

namespace DrakiaXYZ.TaskListFixes
{
    [BepInPlugin("xyz.drakia.tasklistfixes", "DrakiaXYZ-TaskListFixes", "1.8.0")]
    [BepInDependency("com.SPT.core", "4.1.0")]
    public class TaskListFixesPlugin : BaseUnityPlugin
    {
        // Note: We use a cached quest progress dictionary because fetching quest progress actually
        //       triggers a calculation any time it's read
        public static readonly Dictionary<Quest, double> QuestProgressCache = new Dictionary<Quest, double>();

        public void Awake()
        {
            if (!TarkovVersion.CheckEftVersion(Logger, Info, Config))
            {
                throw new Exception($"Invalid EFT Version");
            }

            Settings.Init(Config);

            new TasksScreenShowPatch().Enable();
            new QuestProgressViewPatch().Enable();
            new QuestsSortPanelSortPatch().Enable();
            new QuestsSortPanelShowRestoreSortPatch().Enable();
            new TasksPanelSortPatch().Enable();
        }

        public static bool HandleNullOrEqualQuestCompare(Quest quest1, Quest quest2, out int result)
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
    }

    // Allow restoring the sort order to the last used ordering
    class QuestsSortPanelShowRestoreSortPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestsSortPanel), nameof(QuestsSortPanel.Show), new[] { typeof(EQuestsSortType), typeof(bool) });
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
            return AccessTools.Method(typeof(TasksPanel), nameof(TasksPanel.Sort));
        }

        [PatchPrefix]
        public static bool PatchPrefix(TasksPanel __instance, EQuestsSortType sortType, bool sortDirection, 
            ref EQuestsSortType ____sortType, ref bool ____sortAscend, ViewList<Quest, NotesTask> ____questsViewList,
            Dictionary<Quest, bool> ____questsAvailability)
        {
            // If we're remembering sort value, store it now
            if (Settings.RememberSorting.Value)
            {

                Settings._LastSortBy.Value = (int)sortType;
                Settings._LastSortAscend.Value = sortDirection;
            }

            // Re-implement base sorting behaviour using our own comparers
            ____sortType = sortType;
            ____sortAscend = sortDirection;
            IComparer<Quest> comparer;
            switch (sortType)
            {
                case EQuestsSortType.Trader:
                    comparer = new Comparers.QuestTraderComparer();
                    break;
                case EQuestsSortType.Type:
                    comparer = new Comparers.QuestTypeComparer();
                    break;
                case EQuestsSortType.Task:
                    comparer = new Comparers.QuestNameComparer();
                    break;
                case EQuestsSortType.Location:
                    string locationId = (IsInRaid()) ? Singleton<AbstractGame>.Instance.LocationObjectId : null;
                    comparer = new Comparers.QuestLocationComparer(locationId);
                    break;
                case EQuestsSortType.Status:
                    comparer = new Comparers.QuestStatusComparer();
                    break;
                case EQuestsSortType.Progress:
                    comparer = new Comparers.QuestProgressComparer();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            List<Quest> list = ____questsViewList.Keys;
            list.Sort(comparer);
            if (____sortAscend)
            {
                list.Reverse();
            }
            list = list.OrderByDescending(quest => __instance.CG_Sort(quest)).ThenBy(quest => !____questsAvailability[quest]).ToList<Quest>();
            ____questsViewList.UpdateOrder(list);
            __instance.ValidateFavoriteQuestSeparator();

            return false;
        }

        private static bool IsInRaid()
        {
            return Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;
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
        public static void PatchPostfix(Quest quest, TextMeshProUGUI ____percentages)
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
        private static FieldInfo _sortButtonField;
        protected override MethodBase GetTargetMethod()
        {
            Type targetType = typeof(QuestsSortPanel).BaseType;
            _sortDescendField = AccessTools.Field(targetType, "_ascending");
            _sortButtonField = AccessTools.Field(targetType, "_currentSortButton");

            return AccessTools.Method(targetType, "OnButtonClick");
        }

        [PatchPrefix]
        public static void PatchPrefix(QuestsSortPanel __instance, EQuestsSortType sortType, FilterButton button)
        {
            // If we're restoring the sort order, and we're sorting by the same column as our stored one, don't change the default sort order here
            if (Settings.RememberSorting.Value && Settings._LastSortBy.Value == (int)sortType)
            {
                return;
            }

            FilterButton activeFilterButton = _sortButtonField.GetValue(__instance) as FilterButton;

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
