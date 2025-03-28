using System;
using System.Collections.Generic;
using static RawQuestClass;

namespace DrakiaXYZ.TaskListFixes.Comparers
{
    class QuestTypeComparer : IComparer<QuestClass>
    {
        public int Compare(QuestClass quest1, QuestClass quest2)
        {
            if (TaskListFixesPlugin.HandleNullOrEqualQuestCompare(quest1, quest2, out int result))
            {
                return result;
            }

            string type1 = Enum.GetName(typeof(EQuestType), quest1.Template.QuestType);
            string type2 = Enum.GetName(typeof(EQuestType), quest2.Template.QuestType);
            if (type1 != type2)
            {
                return string.CompareOrdinal(type1, type2);
            }

            if (Settings.SubSortByName.Value)
            {
                return new QuestNameComparer().Compare(quest1, quest2);
            }

            return quest1.StartTime.CompareTo(quest2.StartTime);
        }
    }
}
