using BepInEx.Configuration;

namespace DrakiaXYZ.TaskListFixes
{
    internal class Settings
    {
        private const string GeneralSectionTitle = "General";

        public static ConfigEntry<bool> NewDefaultOrder;
        public static ConfigEntry<bool> SubSortByName;
        public static ConfigEntry<bool> GroupLocByTrader;
        public static ConfigEntry<bool> GroupTraderByLoc;

        public static void Init(ConfigFile Config)
        {
            NewDefaultOrder = Config.Bind(
                GeneralSectionTitle,
                "New Default Order",
                true,
                "Use the new default sort orders when changing sort column"
                );

            SubSortByName = Config.Bind(
                GeneralSectionTitle,
                "Sub Sort By Name",
                true,
                "Use task name for sub sorting instead of task start time");

            GroupLocByTrader = Config.Bind(
                GeneralSectionTitle,
                "Group Locations By Trader",
                true,
                "Sub sort locations by trader name");

            GroupTraderByLoc = Config.Bind(
                GeneralSectionTitle,
                "Group Traders By Location",
                true,
                "Sub sort traders by location name");
        }
    }
}
