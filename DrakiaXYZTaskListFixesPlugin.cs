using Aki.Reflection.Patching;
using BepInEx;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace DrakiaXYZ_TaskListFixes
{
    [BepInPlugin("xyz.drakia.tasklistfixes", "DrakiaXYZ-TaskListFixes", "0.0.1")]
    public class DrakiaXYZTaskListFixesPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            new TasksScreenShowPatch().Enable();
            new TasksScreenShowQuests().Enable();
            new QuestProgressViewPatch().Enable();
        }
    }

	class TasksScreenShowQuests : ModulePatch
	{
		private static Type _questStringFieldComparer;
		private static Type _questLocationComparer;
		private static Type _questStatusComparer;
		private static Type _questProgressComparer;
		private static MethodInfo _filterInGameMethod;
		private static MethodInfo _notesTaskShowMethod;
		private static Dictionary<QuestClass, float> _questProgress = new Dictionary<QuestClass, float>();

		protected override MethodBase GetTargetMethod()
		{
			Type tasksScreenType = typeof(TasksScreen);

			// Fetch the comparer classes
			_questStringFieldComparer = AccessTools.Inner(tasksScreenType, "QuestStringFieldComparer");
			_questLocationComparer = AccessTools.Inner(tasksScreenType, "QuestLocationComparer");
			_questStatusComparer = AccessTools.Inner(tasksScreenType, "QuestStatusComparer");
			_questProgressComparer = AccessTools.Inner(tasksScreenType, "QuestProgressComparer");

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
			GClass788 ___UI, InventoryControllerClass ____inventoryController)
		{
			Logger.LogDebug("TasksScreen::ShowQuests");
			Stopwatch stopwatch = new Stopwatch();
			// Clear the existing UI
			___UI.Dispose();

			// Fetch all the active quests
			stopwatch.Start();
			List<QuestClass> questList = (
				from quest in (
					from x in questController.Quests
					where x.Template != null && (x.QuestStatus == EQuestStatus.Started || x.QuestStatus == EQuestStatus.AvailableForFinish || x.QuestStatus == EQuestStatus.MarkedAsFailed)
					select x).Where(____questsAdditionalFilter)
				where FilterInGame(__instance, questController, quest)
				select quest).ToList<QuestClass>();
			stopwatch.Stop();
			Logger.LogDebug($"Fetched quests in {stopwatch.ElapsedMilliseconds} ms");

			// If there are no quests, show no tasks screen, otherwise hide it
			if (!questList.Any<QuestClass>())
			{
				____noActiveTasksObject.SetActive(true);
				return false;
			}
			____noActiveTasksObject.SetActive(false);

			// Sort by the selected column
			stopwatch.Restart();
			IComparer<QuestClass> comparer;
			switch (__instance.SortType)
			{
				case EQuestsSortType.Trader:
					comparer = new QuestTraderComparer();
					break;
				case EQuestsSortType.Type:
					comparer = (IComparer<QuestClass>)Activator.CreateInstance(_questStringFieldComparer, new object[] { EQuestsSortType.Type });
					break;
				case EQuestsSortType.Task:
					comparer = (IComparer<QuestClass>)Activator.CreateInstance(_questStringFieldComparer, new object[] { EQuestsSortType.Task });
					break;
				case EQuestsSortType.Location:
					comparer = new QuestLocationComparer(____currentLocationId);
					break;
				case EQuestsSortType.Status:
					comparer = (IComparer<QuestClass>)Activator.CreateInstance(_questStatusComparer);
					break;
				case EQuestsSortType.Progress:
					comparer = new QuestProgressComparer();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
			stopwatch.Stop();
			Logger.LogDebug($"Created comparer in {stopwatch.ElapsedMilliseconds} ms");
			stopwatch.Restart();
			questList.Sort(comparer);
			stopwatch.Stop();
			Logger.LogDebug($"Sorted list in {stopwatch.ElapsedMilliseconds} ms");
			if (__instance.SortAscend)
			{
				questList.Reverse();
			}

			stopwatch.Restart();
			// Loop through all the quests and flag whether they're active (The player is in a location, and the quest is set to this location, or any)
			foreach (QuestClass questClass in questList)
			{
				bool questActive = string.IsNullOrEmpty(____currentLocationId) || ((questClass.Template.LocationId == "any" || questClass.Template.LocationId == ____currentLocationId) && questClass.Template.PlayerGroup == ____activeProfile.Side.ToPlayerGroup());
				if (!____questsAvailability.ContainsKey(questClass))
				{
					____questsAvailability.Add(questClass, questActive);
				}
			}
			stopwatch.Stop();
			Logger.LogDebug($"Created availability dict in {stopwatch.ElapsedMilliseconds} ms");

			// Sort the quest list to have all currently active quests at the top
			stopwatch.Restart();
			questList = questList.OrderBy(quest => quest, Comparer<QuestClass>.Create((QuestClass questX, QuestClass questY) => ____questsAvailability[questY].CompareTo(____questsAvailability[questX]))).ToList();
			stopwatch.Stop();
			Logger.LogDebug($"Sorted active quests to top in {stopwatch.ElapsedMilliseconds} ms");

			// Create the notes object, no idea if this is right, the decompiled code doesn't actually work here
			NotesTaskDescriptionShort description = GClass5.InstantiatePrefab<NotesTaskDescriptionShort>(____notesTaskDescription, ____notesTaskDescriptionTemplate);

			// Build the list
			stopwatch.Restart();
			___UI.AddViewList<QuestClass, NotesTask>(questList, ____notesTaskTemplate, ____notesTaskContent, delegate (QuestClass quest, NotesTask view)
			{
				_notesTaskShowMethod.Invoke(view, new object[] { quest, session, ____inventoryController, questController, description, ____questsAvailability[quest] });
			});
			stopwatch.Stop();
			Logger.LogDebug($"Drew list in {stopwatch.ElapsedMilliseconds} ms");

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
				// Handle identical and null cases
				if (quest1 == quest2)
				{
					return 0;
				}
				if (quest2 == null)
				{
					return 1;
				}
				if (quest1 == null)
				{
					return -1;
				}

				string locationId1 = quest1.Template.LocationId;
				string locationId2 = quest2.Template.LocationId;

				// Handle quests being on the same map by sorting those by start time
				if (string.Equals(locationId1, locationId2))
				{
					return quest1.StartTime.CompareTo(quest2.StartTime);
				}

				// Handle quests being on the same map as the player? Should we keep this?
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
				string locationName = (locationId1 + " Name").Localized();
				string locationName2 = (locationId2 + " Name").Localized();
				int compareResult = string.CompareOrdinal(locationName.Localized(), locationName2.Localized());
				if (compareResult == 0)
				{
					return quest1.StartTime.CompareTo(quest2.StartTime);
				}
				return compareResult;
			}
		}

		private class QuestTraderComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				// Handle identical and null cases
				if (quest1 == quest2)
				{
					return 0;
				}
				if (quest2 == null)
				{
					return 1;
				}
				if (quest1 == null)
				{
					return -1;
				}

				// If the trader IDs are the same, sort by the start time
				string traderId1 = quest1.Template.TraderId;
				string traderId2 = quest2.Template.TraderId;
				if (string.Equals(traderId1, traderId2))
				{
					return quest1.StartTime.CompareTo(quest2.StartTime);
				}

				// Otherwise compare the trader's nicknames
				string traderName1 = (traderId1 + " Nickname").Localized();
				string traderName2 = (traderId2 + " Nickname").Localized();
				return string.CompareOrdinal(traderName1, traderName2);
			}
		}

		private class QuestProgressComparer : IComparer<QuestClass>
		{
			public int Compare(QuestClass quest1, QuestClass quest2)
			{
				// Handle identical and null cases
				if (quest1 == quest2)
				{
					return 0;
				}
				if (quest2 == null)
				{
					return 1;
				}
				if (quest1 == null)
				{
					return -1;
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

				// If they are equal, sort by start time
				return quest1.StartTime.CompareTo(quest2.StartTime);
			}
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
			TasksScreenShowQuests.ClearQuestProgress();
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
				TasksScreenShowQuests.SetQuestProgress(quest, progress);
			}
		}
	}
}
