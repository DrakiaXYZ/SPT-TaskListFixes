using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestTraderComparer : IComparer<QuestClass>
    {
        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            // If the trader IDs are the same, sort by the start time
            string traderId1 = quest1.Template.TraderId;
            string traderId2 = quest2.Template.TraderId;

            // If the trader names aren't the same, compare the trader's nicknames
            if (traderId1 != traderId2)
            {
                string traderName1 = TaskListFixesPlugin.Localized(traderId1 + " Nickname");
                string traderName2 = TaskListFixesPlugin.Localized(traderId2 + " Nickname");
                return string.CompareOrdinal(traderName1, traderName2);
            }

            // For tasks from the same trader, if grouping traders by map,
            // sort by map if map is different. Otherwise sort by start time (Original logic), or 
            // task name (New logic)
            string locationId1 = quest1.Template.LocationId;
            string locationId2 = quest2.Template.LocationId;

            // Sort by the map name
            if (Settings.GroupTraderByLoc.Value && locationId1 != locationId2)
            {
                return new QuestLocationComparer(null).Compare(quest1, quest2);
            }

            // Sort by quest name
            if (Settings.SubSortByName.Value)
            {
                return new QuestNameComparer().Compare(quest1, quest2);
            }

            // Sort by quest start time
            return quest1.StartTime.CompareTo(quest2.StartTime);
        }
    }
}
