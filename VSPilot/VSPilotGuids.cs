using System;
using System.Diagnostics;

namespace VSPilot
{
    /// <summary>
    /// Contains all GUIDs used by VSPilot commands
    /// </summary>
    internal static class VSPilotGuids
    {
        static VSPilotGuids()
        {
            Debug.WriteLine($"VSPilotGuids: PackageGuid = {PackageGuidString}");
            Debug.WriteLine($"VSPilotGuids: CommandSet = {CommandSet}");
            Debug.WriteLine($"VSPilotGuids: ImagesGuid = {ImagesGuid}");
        }

        /// <summary>
        /// VSPilotPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "49D5D9FC-73D5-40D8-A55B-65BB5BB32E05";

        /// <summary>
        /// VSPilot Command Set GUID
        /// </summary>
        public static readonly Guid CommandSet = new Guid("DAB1FD00-90FB-48FA-A807-D4E79B582CF3");

        /// <summary>
        /// VSPilot Icons GUID
        /// </summary>
        public static readonly Guid ImagesGuid = new Guid("0FA2DB60-9E32-4B37-9AA5-5BF89A0E3C34");
    }

    /// <summary>
    /// Contains all command IDs used by VSPilot
    /// </summary>
    internal static class VSPilotIds
    {
        static VSPilotIds()
        {
            Debug.WriteLine($"VSPilotIds: ChatWindowCommandId = 0x{ChatWindowCommandId:X4}");
            Debug.WriteLine($"VSPilotIds: SettingsCommandId = 0x{SettingsCommandId:X4}");
            Debug.WriteLine($"VSPilotIds: VSPilotMenuId = 0x{VSPilotMenuId:X4}");
            Debug.WriteLine($"VSPilotIds: VSPilotMenuGroupId = 0x{VSPilotMenuGroupId:X4}");
        }

        /// <summary>
        /// Command ID for Chat Window.
        /// </summary>
        public const int ChatWindowCommandId = 0x0100;

        /// <summary>
        /// Command ID for Settings dialog.
        /// </summary>
        public const int SettingsCommandId = 0x0101;

        /// <summary>
        /// Menu ID for the VSPilot menu.
        /// </summary>
        public const int VSPilotMenuId = 0x2000;

        /// <summary>
        /// Group ID for menu items in the VSPilot menu.
        /// </summary>
        public const int VSPilotMenuGroupId = 0x1050;
    }
}