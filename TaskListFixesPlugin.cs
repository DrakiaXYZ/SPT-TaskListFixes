using Aki.Reflection.Patching;
using BepInEx;
using DrakiaXYZ.TaskListFixes.VersionChecker;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using static RawQuestClass;

namespace DrakiaXYZ.TaskListFixes
{
    [BepInPlugin("xyz.drakia.tasklistfixes", "DrakiaXYZ-TaskListFixes", "1.1.0")]
    public class TaskListFixesPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
			if (!TarkovVersion.CheckEftVersion(Logger, Info, Config))
			{
				throw new Exception($"Invalid EFT Version");
			}

			Settings.Init(Config);

			new TasksScreenShowQuestsPatch().Enable();
			new TasksScreenShowPatch().Enable();
			new QuestProgressViewPatch().Enable();
			new QuestsFilterPanelSortPatch().Enable();
		}
    }

	class TasksScreenShowQuestsPatch : ModulePatch
	{
		private static MethodInfo _filterInGameMethod;
		private static MethodInfo _notesTaskShowMethod;
		private static Dictionary<QuestClass, float> _questProgress = new Dictionary<QuestClass, float>();

		protected override MethodBase GetTargetMethod()
		{
			Type tasksScreenType = typeof(TasksScreen);

			// Fetch the FilterInGame method from TasksScreen
			_filterInGameMethod = AccessTools.Method(tasksScreenType, "FilterInGame");

			// Fetch the Show method from NotesTask
			_notesTaskShowMethod = AccessTools.Method(typeof(NotesTask), "Show");

			return AccessTools.Method(tasksScreenType, "ShowQuests");
		}

		[PatchPrefix]
		public static bool PatchPrefix(
			TasksScreen __instance, QuestControllerClass questController, ISession session, Func<QuestClass, bool> ____questsAdditionalFilter,
			string ____currentLocationId, Profile ____activeProfile, Dictionary<QuestClass, bool> ____questsAvailability, GameObject ____noActiveTasksObject,
			NotesTask ____notesTaskTemplate, RectTransform ____notesTaskContent, GameObject ____notesTaskDescriptionTemplate, GameObject ____notesTaskDescription,
			InventoryControllerClass ____inventoryController)
		{
			// Dynamically get the UI, and the required methods, so we can avoid referencing a GClass
			FieldInfo uiFieldInfo = AccessTools.Field(typeof(TasksScreen), "UI");
			object uiField = uiFieldInfo.GetValue(__instance);
			MethodInfo uiDisposeMethod = AccessTools.Method(uiFieldInfo.FieldType, "Dispose");
			MethodInfo uiAddViewListMethod = AccessTools.Method(uiFieldInfo.FieldType, "AddViewList").MakeGenericMethod(new Type[] { typeof(QuestClass), typeof(NotesTask) });

			// Clear the existing UI
			uiDisposeMethod.Invoke(uiField, new object[] { });

			// Fetch all the active quests
			var questList = (
				from quest in (
					from x in questController.Quests
					where x.Template != null && (x.QuestStatus == EQuestStatus.Started || x.QuestStatus == EQuestStatus.AvailableForFinish || x.QuestStatus == EQuestStatus.MarkedAsFailed)
					select x).Where(____questsAdditionalFilter)
				where FilterInGame(__instance, questController, quest)
				select quest);

			// If there are no quests, show no tasks screen, otherwise hide it
			if (!questList.Any())
			{
				____noActiveTasksObject.SetActive(true);
				return false;
			}
			____noActiveTasksObject.SetActive(false);

			// Sort by the selected column
			IComparer<QuestClass> comparer;
			switch (__instance.SortType)
			{
				case EQuestsSortType.Trader:
					comparer = new QuestTraderComparer();
					break;
				case EQuestsSortType.Type:
					comparer = new QuestTypeComparer();
					break;
				case EQuestsSortType.Task:
					comparer = new QuestTaskNameComparer();
					break;
				case EQuestsSortType.Location:
					comparer = new QuestLocationComparer(____currentLocationId);
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

			questList = questList.OrderBy(quest => quest, comparer);
			if (__instance.SortAscend)
			{
				questList = questList.Reverse();
			}

			// Loop through all the quests and flag whether they're active (The player is in a location, and the quest is set to this location, or any)
			foreach (QuestClass questClass in questList)
			{
				bool questActive = string.IsNullOrEmpty(____currentLocationId) || ((questClass.Template.LocationId == "any" || questClass.Template.LocationId == ____currentLocationId) && questClass.Template.PlayerGroup == ____activeProfile.Side.ToPlayerGroup());
				if (!____questsAvailability.ContainsKey(questClass))
				{
					____questsAvailability.Add(questClass, questActive);
				}
			}

			// Sort the quest list to have all currently active quests at the top
			questList = questList.OrderBy(quest => quest, Comparer<QuestClass>.Create((QuestClass questX, QuestClass questY) => ____questsAvailability[questY].CompareTo(____questsAvailability[questX])));

			// Create the notes object, no idea if this is right, the decompiled code doesn't actually work here
			NotesTaskDescriptionShort description = ____notesTaskDescription.InstantiatePrefab<NotesTaskDescriptionShort>(____notesTaskDescriptionTemplate);

			// Build the list, using a delegate so we can avoid a direct reference to a GClass
			Type delegateType = uiAddViewListMethod.GetParameters()[3].ParameterType;
			Action<QuestClass, NotesTask> delegateInstance = (QuestClass quest, NotesTask view) => 
			{
				_notesTaskShowMethod.Invoke(view, new object[] { quest, session, ____inventoryController, questController, description, ____questsAvailability[quest] });
			};
			uiAddViewListMethod.Invoke(uiField, new object[] { questList, ____notesTaskTemplate, ____notesTaskContent, delegateInstance });

			// Stop the original method from running
			return false;
		}

		private static bool FilterInGame(TasksScreen taskScreen, QuestControllerClass questController, QuestClass quest)
		{
			return (bool)_filterInGameMethod.Invoke(taskScreen, new object[] { questController, quest });
		}

		private class QuestLocationComparer : IComparer<QuestClass>
		{
			private string _locationId;

			public QuestLocationComparer(string locationId)
			{
				this._locationId = locationId;
			}

			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
                {
					return result;
                }

				string locationId1 = quest1.Template.LocationId;
				string locationId2 = quest2.Template.LocationId;

				// For tasks on the same map, if grouping same map by trader,
				// sort by trader if trader is different.
				// Otherwise sort by start time (Original logic), or task name (New logic)
				if (locationId1 == locationId2)
				{
					string traderId1 = quest1.Template.TraderId;
					string traderId2 = quest2.Template.TraderId;
					if (Settings.GroupLocByTrader.Value && traderId1 != traderId2)
					{
						string traderName1 = (traderId1 + " Nickname").Localized();
						string traderName2 = (traderId2 + " Nickname").Localized();
						return string.CompareOrdinal(traderName1, traderName2);
					}

					if (Settings.SubSortByName.Value)
					{
						return string.CompareOrdinal(quest1.Template.Name, quest2.Template.Name);
					}

					return quest1.StartTime.CompareTo(quest2.StartTime);
				}

				// Sort quests on the same location as the player to the top of the list
				if (locationId2 == this._locationId)
				{
					return 1;
				}
				if (locationId1 == this._locationId)
				{
					return -1;
				}

				// Handle quests that can be done on any map
				if (locationId2 == "any")
				{
					return 1;
				}
				if (locationId1 == "any")
				{
					return -1;
				}

				// Finally sort by the actual quest location name
				string locationName1 = (locationId1 + " Name").Localized();
				string locationName2 = (locationId2 + " Name").Localized();
				return string.CompareOrdinal(locationName1, locationName2);
			}
		}

		private class QuestTraderComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
				{
					return result;
				}

				// If the trader IDs are the same, sort by the start time
				string traderId1 = quest1.Template.TraderId;
				string traderId2 = quest2.Template.TraderId;

				// For tasks from the same trader, if grouping traders by map,
				// sort by map if map is different. Otherwise sort by start time (Original logic), or 
				// task name (New logic)
				if (traderId1 == traderId2)
				{
					string locationId1 = quest1.Template.LocationId;
					string locationId2 = quest2.Template.LocationId;

					if (Settings.GroupTraderByLoc.Value && locationId1 != locationId2)
					{
						// Sort "any" above other locations
						if (locationId2 == "any")
						{
							return 1;
						}
						if (locationId1 == "any")
						{
							return -1;
						}

						// Otherwise sort by the map name
						string locationName = (locationId1 + " Name").Localized();
						string locationName2 = (locationId2 + " Name").Localized();
						return string.CompareOrdinal(locationName, locationName2);
					}

					if (Settings.SubSortByName.Value)
					{
						return string.CompareOrdinal(quest1.Template.Name, quest2.Template.Name);
					}

					return quest1.StartTime.CompareTo(quest2.StartTime);
				}

				// Otherwise compare the trader's nicknames
				string traderName1 = (traderId1 + " Nickname").Localized();
				string traderName2 = (traderId2 + " Nickname").Localized();
				return string.CompareOrdinal(traderName1, traderName2);
			}
		}

		private class QuestTypeComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
				{
					return result;
				}

				// For non-matching types, sort by their name
				string type1 = Enum.GetName(typeof(EQuestType), quest1.Template.QuestType);
				string type2 = Enum.GetName(typeof(EQuestType), quest2.Template.QuestType);
				if (type1 != type2)
                {
					return string.CompareOrdinal(type1, type2);
				}

				if (Settings.SubSortByName.Value)
				{
					return string.CompareOrdinal(quest1.Template.Name, quest2.Template.Name);
				}

				return quest1.StartTime.CompareTo(quest2.StartTime);
			}
		}

		private class QuestTaskNameComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
				{
					return result;
				}

				string questName1 = quest1.Template.Name;
				string questName2 = quest2.Template.Name;
				if (questName1 != questName2)
                {
					return string.CompareOrdinal(questName1, questName2);
				}

				return quest1.StartTime.CompareTo(quest2.StartTime);
			}
		}

		private class QuestStatusComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
				{
					return result;
				}

				// If the queset status is the same, sort by either the name or the start time
				EQuestStatus questStatus1 = quest1.QuestStatus;
				EQuestStatus questStatus2 = quest2.QuestStatus;
				if (questStatus1 == questStatus2)
				{
					if (Settings.SubSortByName.Value)
					{
						// We do this opposite of other sorting, because status defaults to descending
						return string.CompareOrdinal(quest2.Template.Name, quest1.Template.Name);
					}

					// We do this opposite of other sorting, because status defaults to descending
					return quest2.StartTime.CompareTo(quest1.StartTime);
				}

				// This is the original logic, but with sorting by name for "matched" things added
				if (questStatus2 != EQuestStatus.MarkedAsFailed)
				{
					if (questStatus1 != EQuestStatus.AvailableForFinish)
					{
						if (questStatus2 != EQuestStatus.AvailableForFinish)
						{
							if (questStatus1 != EQuestStatus.MarkedAsFailed)
							{
								if (Settings.SubSortByName.Value)
								{
									// We do this opposite of other sorting, because status defaults to descending
									return string.CompareOrdinal(quest2.Template.Name, quest1.Template.Name);
								}

								// We do this opposite of other sorting, because status defaults to descending
								return quest2.StartTime.CompareTo(quest1.StartTime);
							}
						}
						return -1;
					}
				}

				return 1;
			}
		}

		private class QuestProgressComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				if (HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
				{
					return result;
				}

				// Fetch the quest progress from our dictionary if it exists, otherwise cache it
				float quest1Progress, quest2Progress;
				if (!_questProgress.TryGetValue(quest1, out quest1Progress))
				{
					quest1Progress = quest1.Progress.Item2 / quest1.Progress.Item1;
					_questProgress.Add(quest1, quest1Progress);
				}
				if (!_questProgress.TryGetValue(quest2, out quest2Progress))
				{
					quest2Progress = quest2.Progress.Item2 / quest2.Progress.Item1;
					_questProgress.Add(quest2, quest2Progress);
				}

				// Sort by the progress number if they aren't equal
				if (!quest1Progress.ApproxEquals(quest2Progress))
				{
					return quest1Progress.CompareTo(quest2Progress);
				}

				// Sort by name as the fallback is option is enabled
				if (Settings.SubSortByName.Value)
				{
					// We do this opposite of other sorting, because progress defaults to descending
					return string.CompareOrdinal(quest2.Template.Name, quest1.Template.Name);
				}

				// Otherwise use the default behaviour of sorting by start time
				// We do this opposite of other sorting, because progress defaults to descending
				return quest2.StartTime.CompareTo(quest1.StartTime);
			}
		}

		private static bool HandleNullOrEqualQuestCompare(QuestClass quest1, QuestClass quest2, out int result)
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

		public static void ClearQuestProgress()
		{
			_questProgress.Clear();
		}

		public static void SetQuestProgress(QuestClass quest, int progress)
		{
			if (!_questProgress.ContainsKey(quest))
			{
				_questProgress.Add(quest, progress);
			}
			else
			{
				_questProgress[quest] = progress;
			}
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
			TasksScreenShowQuestsPatch.ClearQuestProgress();
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
			if (Int32.TryParse(____percentages.text, out int progress))
			{
				TasksScreenShowQuestsPatch.SetQuestProgress(quest, progress);
			}
		}
	}

	// Patch used to change the default ordering when sorting by a new column
	class QuestsFilterPanelSortPatch : ModulePatch
    {
		private static PropertyInfo _boolean_0;
		protected override MethodBase GetTargetMethod()
		{
			_boolean_0 = AccessTools.Property(typeof(QuestsFilterPanel), "Boolean_0");

			return AccessTools.Method(typeof(QuestsFilterPanel), "method_1");
		}

		[PatchPrefix]
		public static void PatchPrefix(QuestsFilterPanel __instance, EQuestsSortType sortType, FilterButton button, FilterButton ___filterButton_0)
		{
			// If the button is different than the stored filterButton_0, it means we're sorting by a new column.
			if (Settings.NewDefaultOrder.Value && button != ___filterButton_0)
            {
				switch (sortType)
                {
					// Sort these default ascending
					case EQuestsSortType.Task:
					case EQuestsSortType.Trader:
					case EQuestsSortType.Location:
						_boolean_0.SetValue(__instance, false);
						break;

					// Sort these default descending
					case EQuestsSortType.Progress:
					case EQuestsSortType.Status:
						_boolean_0.SetValue(__instance, true);
						break;
                }
            }
		}
	}

}
