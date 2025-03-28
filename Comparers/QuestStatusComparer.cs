using EFT.Quests;
using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestStatusComparer : IComparer<QuestClass>
    {
        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            // If the quest status is the same, sort by either the name or the start time
            EQuestStatus questStatus1 = quest1.QuestStatus;
            EQuestStatus questStatus2 = quest2.QuestStatus;
            if (questStatus1 == questStatus2)
            {
                if (Settings.SubSortByName.Value)
                {
                    // We do this opposite of other sorting, because status defaults to descending
                    return new QuestNameComparer().Compare(quest2, quest1);
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
                                return new QuestNameComparer().Compare(quest2, quest1);
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
}
