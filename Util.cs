﻿using Decal.Adapter.Wrappers;
using Decal.Filters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UtilityBelt
{
	public static class Util
	{
        public static string GetPluginDirectory() {
            return Path.Combine(GetDecalPluginsDirectory(), Globals.PluginName);
        }

        private static string GetDecalPluginsDirectory() {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Decal Plugins");
        }

        public static string GetCharacterDirectory() {
            String path = Path.Combine(GetPluginDirectory(), Globals.Core.CharacterFilter.Server);
            path = Path.Combine(path, Globals.Core.CharacterFilter.Name);
            return path;
        }

        private static string GetLogDirectory() {
            return Path.Combine(GetCharacterDirectory(), "logs");
        }

        public static void CreateDataDirectories() {
            System.IO.Directory.CreateDirectory(GetPluginDirectory());
            System.IO.Directory.CreateDirectory(GetCharacterDirectory());
            System.IO.Directory.CreateDirectory(GetLogDirectory());
        }

        public static void WriteToDebugLog(string message) {
            WriteToLogFile("debug", message, true);
        }

        public static void WriteToLogFile(string logName, string message, bool addTimestamp = false) {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var logFileName = String.Format("{0}.{1}.txt", logName, today);

            if (addTimestamp) {
                message = String.Format("{0} {1}", DateTime.Now.ToString("yy/MM/dd H:mm:ss"), message);
            }

            File.AppendAllText(Path.Combine(Util.GetLogDirectory(), logFileName), message + Environment.NewLine);
        }

        public static void WriteToChat(string message)
		{
			try
			{
				Globals.Host.Actions.AddChatText("[" + Globals.PluginName + "] " + message, 5);
                WriteToDebugLog(message);
			}
			catch (Exception ex) { Logger.LogException(ex); }
		}

        public static int GetFreeMainPackSpace() {
            WorldObject mainPack = Globals.Core.WorldFilter[Globals.Core.CharacterFilter.Id];

            return GetFreePackSpace(mainPack);
        }

        public static int GetFreePackSpace(WorldObject container) {
            int packSlots = container.Values(LongValueKey.ItemSlots, 0);

            // side pack count
            if (container.Id != Globals.Core.CharacterFilter.Id) {
                return packSlots - Globals.Core.WorldFilter.GetByContainer(container.Id).Count;
            }

            // main pack count
            foreach (var wo in Globals.Core.WorldFilter.GetByContainer(container.Id)) {
                if (wo != null) {
                    // skip packs
                    if (wo.ObjectClass == ObjectClass.Container) continue;

                    // skip foci
                    if (wo.ObjectClass == ObjectClass.Foci) continue;

                    // skip equipped
                    if (wo.Values(LongValueKey.EquippedSlots, 0) > 0) continue;

                    // skip wielded
                    if (wo.Values(LongValueKey.Slot, -1) == -1) continue;

                    --packSlots;
                }
            }

            return packSlots;
        }
        
        internal static void StackItem(WorldObject stackThis) {
            // try to stack in side pack
            foreach (var container in Globals.Core.WorldFilter.GetInventory()) {
                if (container.ObjectClass == ObjectClass.Container && container.Values(LongValueKey.Slot, -1) >= 0) {
                    foreach (var wo in Globals.Core.WorldFilter.GetByContainer(container.Id)) {
                        if (wo.Name == stackThis.Name && wo.Id != stackThis.Id) {
                            if (wo.Values(LongValueKey.StackCount, 1) + stackThis.Values(LongValueKey.StackCount, 1) <= wo.Values(LongValueKey.StackMax)) {
                                Globals.Core.Actions.SelectItem(stackThis.Id);
                                Globals.Core.Actions.MoveItem(stackThis.Id, container.Id, container.Values(LongValueKey.Slot), true);
                                return;
                            }
                        }
                    }
                }
            }

            // try to stack in main pack
            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (wo.Container == Globals.Core.CharacterFilter.Id) {
                    if (wo.Name == stackThis.Name && wo.Id != stackThis.Id) {
                        if (wo.Values(LongValueKey.StackCount, 1) + stackThis.Values(LongValueKey.StackCount, 1) <= wo.Values(LongValueKey.StackMax)) {
                            Globals.Core.Actions.SelectItem(stackThis.Id);
                            Globals.Core.Actions.MoveItem(stackThis.Id, Globals.Core.CharacterFilter.Id, 0, true);
                            return;
                        }
                    }
                }
            }
        }

        internal static int GetItemCountInInventoryByName(string name) {
            int count = 0;

            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (wo.Name == name) {
                    if (wo.Values(LongValueKey.StackCount, 0) > 0) {
                        count += wo.Values(LongValueKey.StackCount);
                    }
                    else {
                        ++count;
                    }
                }
            }

            return count;
        }

        internal static bool ItemIsSafeToGetRidOf(WorldObject wo) {
            if (wo == null) return false;

            // skip packs
            if (wo.ObjectClass == ObjectClass.Container) return false;

            // skip foci
            if (wo.ObjectClass == ObjectClass.Foci) return false;

            // skip equipped
            if (wo.Values(LongValueKey.EquippedSlots, 0) > 0) return false;

            // skip wielded
            if (wo.Values(LongValueKey.Slot, -1) == -1) return false;

            // skip tinkered
            if (wo.Values(LongValueKey.NumberTimesTinkered, 0) > 1) return false;

            // skip imbued
            if (wo.Values(LongValueKey.Imbued, 0) > 1) return false;

            return true;
        }

        internal static int PyrealCount() {
            int total = 0;

            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (wo.Values(LongValueKey.Type, 0) == 273/* pyreals */) {
                    total += wo.Values(LongValueKey.StackCount, 1);
                }
            }

            return total;
        }


        [DllImport("Decal.dll")]
        static extern int DispatchOnChatCommand(ref IntPtr str, [MarshalAs(UnmanagedType.U4)] int target);

        public static bool Decal_DispatchOnChatCommand(string cmd) {
            IntPtr bstr = Marshal.StringToBSTR(cmd);

            try {
                bool eaten = (DispatchOnChatCommand(ref bstr, 1) & 0x1) > 0;

                return eaten;
            }
            finally {
                Marshal.FreeBSTR(bstr);
            }
        }

        /// <summary>
        /// This will first attempt to send the messages to all plugins. If no plugins set e.Eat to true on the message, it will then simply call InvokeChatParser.
        /// </summary>
        /// <param name="cmd"></param>
        public static void DispatchChatToBoxWithPluginIntercept(string cmd) {
            if (!Decal_DispatchOnChatCommand(cmd))
                Globals.Core.Actions.InvokeChatParser(cmd);
        }

        internal static string GetTilePath() {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            return Path.Combine(Path.Combine(assemblyFolder, "Resources"), "tiles");
        }

        internal static void Think(string message) {
            try {
                DispatchChatToBoxWithPluginIntercept(string.Format("/tell {0}, {1}", Globals.Core.CharacterFilter.Name, message));
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public static string GetObjectName(int id) {
            if (!Globals.Core.Actions.IsValidObject(id)) {
                return string.Format("<{0}>", id);
            }
            var wo = Globals.Core.WorldFilter[id];

            if (wo == null) return string.Format("<{0}>", id);

            if (wo.Values(LongValueKey.Material, 0) > 0) {
                FileService service = Globals.Core.Filter<FileService>();
                return string.Format("{0} {1}", service.MaterialTable.GetById(wo.Values(LongValueKey.Material, 0)), wo.Name); 
            }
            else {
                return string.Format("{0}", wo.Name);
            }
        }

        public static Point RotatePoint(Point pointToRotate, Point centerPoint, double angleInDegrees) {
            double angleInRadians = angleInDegrees * (Math.PI / 180);
            double cosTheta = Math.Cos(angleInRadians);
            double sinTheta = Math.Sin(angleInRadians);
            return new Point {
                X =
                    (int)
                    (cosTheta * (pointToRotate.X - centerPoint.X) -
                    sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X),
                Y =
                    (int)
                    (sinTheta * (pointToRotate.X - centerPoint.X) +
                    cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)
            };
        }
    }
}
