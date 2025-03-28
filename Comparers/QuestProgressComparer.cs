using System;
using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestProgressComparer : IComparer<QuestClass>
    {
        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            // Use a quest progress cache to avoid re-calculating quest progress constantly
            double quest1Progress, quest2Progress;
            if (!TaskListFixesPlugin.QuestProgressCache.TryGetValue(quest1, out quest1Progress))
            {
                quest1Progress = quest1.Progress.Item2 / quest1.Progress.Item1;
                TaskListFixesPlugin.QuestProgressCache[quest1] = quest1Progress;
            }
            if (!TaskListFixesPlugin.QuestProgressCache.TryGetValue(quest2, out quest2Progress))
            {
                quest2Progress = quest2.Progress.Item2 / quest2.Progress.Item1;
                TaskListFixesPlugin.QuestProgressCache[quest2] = quest2Progress;
            }

            // Sort by the progress number if they aren't equal
            if (quest1Progress != quest2Progress)
            {
                return quest1Progress.CompareTo(quest2Progress);
            }

            // Sort by name as the fallback is option is enabled
            if (Settings.SubSortByName.Value)
            {
                // We do this opposite of other sorting, because progress defaults to descending
                return new QuestNameComparer().Compare(quest2, quest1);
            }

            // Otherwise use the default behaviour of sorting by start time
            // We do this opposite of other sorting, because progress defaults to descending
            return quest2.StartTime.CompareTo(quest1.StartTime);
        }
    }
}
