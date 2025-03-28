using System.Collections.Generic;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestNameComparer : IComparer<QuestClass>
    {
        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            string questName1 = TaskListFixesPlugin.Localized(quest1.Template.Id + " name");
            string questName2 = TaskListFixesPlugin.Localized(quest2.Template.Id + " name");
            if (questName1 != questName2)
            {
                return string.CompareOrdinal(questName1, questName2);
            }

            return quest1.StartTime.CompareTo(quest2.StartTime);
        }
    }
}
