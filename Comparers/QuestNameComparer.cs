using EFT;
using EFT.Quests;
using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestNameComparer : IComparer<Quest>
    {
        public int Compare(Quest quest1, Quest quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            string questName1 = (quest1.Template.Id + " name").Localized();
            string questName2 = (quest2.Template.Id + " name").Localized();
            if (questName1 != questName2)
            {
                return string.CompareOrdinal(questName1, questName2);
            }

            return quest1.StartTime.CompareTo(quest2.StartTime);
        }
    }
}
