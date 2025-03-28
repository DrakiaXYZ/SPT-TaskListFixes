using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestLocationComparer : IComparer<QuestClass>
    {
        private string locationId;
        public QuestLocationComparer(string locationId)
        {
            this.locationId = locationId;
        }

        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
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
                    return new QuestTraderComparer().Compare(quest1, quest2);
                }

                if (Settings.SubSortByName.Value)
                {
                    return new QuestNameComparer().Compare(quest1, quest2);
                }

                return quest1.StartTime.CompareTo(quest2.StartTime);
            }

            // Sort quests on the same location as the player to the top of the list
            if (locationId2 == locationId)
            {
                return 1;
            }
            if (locationId1 == locationId)
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
            string locationName1 = TaskListFixesPlugin.Localized(locationId1 + " Name");
            string locationName2 = TaskListFixesPlugin.Localized(locationId2 + " Name");
            return string.CompareOrdinal(locationName1, locationName2);
        }
    }
}
