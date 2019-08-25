﻿using System;

using Decal.Adapter;
using Decal.Adapter.Wrappers;
using MyClasses.MetaViewWrappers;
using UtilityBelt.Tools;
using UtilityBelt.Views;

namespace UtilityBelt
{

    //Attaches events from core
	[WireUpBaseEvents]
    
	[FriendlyName("UtilityBelt")]
	public class PluginCore : PluginBase {
        private AutoSalvage autoSalvage;
        private DungeonMaps dungeonMaps;
        private EmuConfig emuConfig;
        private QuestTracker questTracker;
        private Counter counter;
        private ItemGiver itemGiver;
        private DateTime lastThought = DateTime.MinValue;

        /// <summary>
        /// This is called when the plugin is started up. This happens only once.
        /// </summary>
        protected override void Startup() {
			try {
				Globals.Init("UtilityBelt", Host, Core);
            }
			catch (Exception ex) { Logger.LogException(ex); }
		}

		/// <summary>
		/// This is called when the plugin is shut down. This happens only once.
		/// </summary>
		protected override void Shutdown() {
			try {

			}
			catch (Exception ex) { Logger.LogException(ex); }
		}

		[BaseEvent("LoginComplete", "CharacterFilter")]
		private void CharacterFilter_LoginComplete(object sender, EventArgs e)
		{
			try {
                string configFilePath = System.IO.Path.Combine(Util.GetCharacterDirectory(), "config.xml");

                Mag.Shared.Settings.SettingsFile.Init(configFilePath, Globals.PluginName);

                Util.CreateDataDirectories();
                Logger.Init();

                Globals.Config = new Config();
                Globals.MainView = new MainView();
                Globals.MapView = new MapView();
                Globals.InventoryManager = new InventoryManager();
                Globals.AutoVendor = new AutoVendor();

                autoSalvage = new AutoSalvage();
                dungeonMaps = new DungeonMaps();
                emuConfig = new EmuConfig();
                questTracker = new QuestTracker();
                counter = new Counter();
                itemGiver = new ItemGiver();

                Globals.Core.RenderFrame += Core_RenderFrame;
            }
			catch (Exception ex) { Logger.LogException(ex); }
		}

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (autoSalvage != null) autoSalvage.Think();
                if (Globals.AutoVendor != null) Globals.AutoVendor.Think();
                if (itemGiver != null) itemGiver.Think();
                if (dungeonMaps != null) dungeonMaps.Think();
                if (Globals.InventoryManager != null) Globals.InventoryManager.Think();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        [BaseEvent("Logoff", "CharacterFilter")]
		private void CharacterFilter_Logoff(object sender, Decal.Adapter.Wrappers.LogoffEventArgs e)
		{
			try {
                Globals.Core.RenderFrame -= Core_RenderFrame;

                if (autoSalvage != null) autoSalvage.Dispose();
                if (dungeonMaps != null) dungeonMaps.Dispose();
                if (emuConfig != null) emuConfig.Dispose();
                if (questTracker != null) questTracker.Dispose();
                if (counter != null) counter.Dispose();
                if (itemGiver != null) itemGiver.Dispose();
                if (Globals.AutoVendor != null) Globals.AutoVendor.Dispose();
                if (Globals.InventoryManager != null) Globals.InventoryManager.Dispose();
                if (Globals.MapView != null) Globals.MapView.Dispose();
                if (Globals.MainView != null) Globals.MainView.Dispose();
                if (Globals.Config != null) Globals.Config.Dispose();
            }
			catch (Exception ex) { Logger.LogException(ex); }
		}
	}
}
