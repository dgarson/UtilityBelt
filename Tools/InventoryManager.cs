﻿using Decal.Adapter;
using Decal.Adapter.Wrappers;
using Mag.Shared.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VirindiViewService.Controls;

namespace UtilityBelt.Tools {
    public class InventoryManager : IDisposable {
        private const int THINK_INTERVAL = 300;
        private const int ITEM_BLACKLIST_TIMEOUT = 60; // in seconds
        private const int CONTAINER_BLACKLIST_TIMEOUT = 60; // in seconds

        private bool disposed = false;
        private bool isRunning = false;
        private bool isPaused = false;
        private bool isForced = false;
        private DateTime lastThought = DateTime.MinValue;
        private int movingObjectId = 0;
        private int tryCount = 0;
        private Dictionary<int, DateTime> blacklistedItems = new Dictionary<int, DateTime>();
        private Dictionary<int,DateTime> blacklistedContainers = new Dictionary<int, DateTime>();

        HudCheckBox UIInventoryManagerAutoCram { get; set; }
        HudCheckBox UIInventoryManagerAutoStack { get; set; }
        HudCheckBox UIInventoryManagerDebug { get; set; }
        HudButton UIInventoryManagerTest { get; set; }

        // TODO: support AutoPack profiles when cramming
        public InventoryManager() {
            Globals.Core.CommandLineText += Current_CommandLineText;
            Globals.Core.WorldFilter.ChangeObject += WorldFilter_ChangeObject;
            Globals.Core.WorldFilter.CreateObject += WorldFilter_CreateObject;

            UIInventoryManagerTest = Globals.MainView.view != null ? (HudButton)Globals.MainView.view["InventoryManagerTest"] : new HudButton();
            UIInventoryManagerTest.Hit += UIInventoryManagerTest_Hit;

            UIInventoryManagerDebug = Globals.MainView.view != null ? (HudCheckBox)Globals.MainView.view["InventoryManagerDebug"] : new HudCheckBox();
            UIInventoryManagerDebug.Checked = Globals.Config.InventoryManager.Debug.Value;
            UIInventoryManagerDebug.Change += UIInventoryManagerDebug_Change;
            Globals.Config.InventoryManager.Debug.Changed += Config_InventoryManager_Debug_Changed;

            UIInventoryManagerAutoCram = Globals.MainView.view != null ? (HudCheckBox)Globals.MainView.view["InventoryManagerAutoCram"] : new HudCheckBox();
            UIInventoryManagerAutoCram.Checked = Globals.Config.InventoryManager.AutoCram.Value;
            UIInventoryManagerAutoCram.Change += UIInventoryManagerAutoCram_Change;
            Globals.Config.InventoryManager.AutoCram.Changed += Config_InventoryManager_AutoCram_Changed;

            UIInventoryManagerAutoStack = Globals.MainView.view != null ? (HudCheckBox)Globals.MainView.view["InventoryManagerAutoStack"] : new HudCheckBox();
            UIInventoryManagerAutoStack.Checked = Globals.Config.InventoryManager.AutoStack.Value;
            UIInventoryManagerAutoStack.Change += UIInventoryManagerAutoStack_Change;
            Globals.Config.InventoryManager.AutoStack.Changed += Config_InventoryManager_AutoStack_Changed;

            // temporary until ui gets added back in
            Globals.Config.InventoryManager.AutoCram.Value = true;
            Globals.Config.InventoryManager.AutoStack.Value = true;
            Globals.Config.InventoryManager.Debug.Value = true;

            if (Globals.Config.InventoryManager.AutoCram.Value || Globals.Config.InventoryManager.AutoStack.Value) {
                //Start();
            }
        }

        private void UIInventoryManagerTest_Hit(object sender, EventArgs e) {
            Start();
        }

        private void UIInventoryManagerDebug_Change(object sender, EventArgs e) {
            Globals.Config.InventoryManager.Debug.Value = UIInventoryManagerDebug.Checked;
        }

        private void Config_InventoryManager_Debug_Changed(Setting<bool> obj) {
            UIInventoryManagerDebug.Checked = Globals.Config.InventoryManager.Debug.Value;
        }

        private void UIInventoryManagerAutoCram_Change(object sender, EventArgs e) {
            Globals.Config.InventoryManager.AutoCram.Value = UIInventoryManagerAutoCram.Checked;
        }

        private void Config_InventoryManager_AutoCram_Changed(Setting<bool> obj) {
            UIInventoryManagerAutoCram.Checked = Globals.Config.InventoryManager.AutoCram.Value;
        }

        private void UIInventoryManagerAutoStack_Change(object sender, EventArgs e) {
            Globals.Config.InventoryManager.AutoStack.Value = UIInventoryManagerAutoStack.Checked;
        }

        private void Config_InventoryManager_AutoStack_Changed(Setting<bool> obj) {
            UIInventoryManagerAutoStack.Checked = Globals.Config.InventoryManager.AutoStack.Value;
        }

        private void Current_CommandLineText(object sender, ChatParserInterceptEventArgs e) {
            try {
                if (e.Text.StartsWith("/ub autoinventory")) {
                    bool force = e.Text.Contains("force");
                    e.Eat = true;

                    Start(force);

                    return;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void WorldFilter_ChangeObject(object sender, ChangeObjectEventArgs e) {
            try {
                if (e.Change != WorldChangeType.StorageChange) return;

                if (movingObjectId == e.Changed.Id) {
                    tryCount = 0;
                    movingObjectId = 0;
                }
                else if (e.Changed.Container == Globals.Core.CharacterFilter.Id && !IsRunning()) {
                    //Start();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void WorldFilter_CreateObject(object sender, CreateObjectEventArgs e) {
            try {
                // created in main backpack?
                if (e.New.Container == Globals.Core.CharacterFilter.Id && !IsRunning()) {
                    //Start();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void Start(bool force=false) {
            isRunning = true;
            isPaused = false;
            isForced = force;
            movingObjectId = 0;
            tryCount = 0;
            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager Started");
            }

            CleanupBlacklists();
        }

        public void Stop() {
            isForced = false;
            isRunning = false;
            movingObjectId = 0;
            tryCount = 0;

            Util.Think("AutoInventory finished.");

            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager Finished");
            }
        }

        public void Pause() {
            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager Paused");
            }
            isPaused = true;
        }

        public void Resume() {
            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager Resumed");
            }
            isPaused = false;
        }

        private void CleanupBlacklists() {
            var containerKeys = blacklistedContainers.Keys.ToArray();
            var itemKeys = blacklistedItems.Keys.ToArray();

            // containers
            foreach (var key in containerKeys) {
                if (blacklistedContainers.ContainsKey(key) && DateTime.UtcNow - blacklistedContainers[key] >= TimeSpan.FromSeconds(CONTAINER_BLACKLIST_TIMEOUT)) {
                    blacklistedContainers.Remove(key);
                }
            }

            // items
            foreach (var key in itemKeys) {
                if (blacklistedItems.ContainsKey(key) && DateTime.UtcNow - blacklistedItems[key] >= TimeSpan.FromSeconds(ITEM_BLACKLIST_TIMEOUT)) {
                    blacklistedItems.Remove(key);
                }
            }
        }

        public bool AutoCram(List<int> excludeList = null, bool excludeMoney=true) {
            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager::AutoCram started");
            }
            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (excludeMoney && (wo.Values(LongValueKey.Type, 0) == 273/* pyreals */ || wo.ObjectClass == ObjectClass.TradeNote)) continue;
                if (excludeList != null && excludeList.Contains(wo.Id)) continue;
                if (blacklistedItems.ContainsKey(wo.Id)) continue;

                if (ShouldCramItem(wo) && wo.Values(LongValueKey.Container) == Globals.Core.CharacterFilter.Id) {
                    if (TryCramItem(wo)) return true;
                }
            }

            return false;
        }

        public bool AutoStack(List<int> excludeList = null) {
            if (Globals.Config.InventoryManager.Debug.Value == true) {
                Util.WriteToChat("InventoryManager::AutoStack started");
            }
            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (excludeList != null && excludeList.Contains(wo.Id)) continue;

                if (wo != null && wo.Values(LongValueKey.StackMax, 1) > 1) {
                    if (TryStackItem(wo)) return true;
                }
            }

            return false;
        }

        public bool IsRunning() {
            return isRunning;
        }

        internal static bool ShouldCramItem(WorldObject wo) {
            if (wo == null) return false;

            // skip packs
            if (wo.ObjectClass == ObjectClass.Container) return false;

            // skip foci
            if (wo.ObjectClass == ObjectClass.Foci) return false;

            // skip equipped
            if (wo.Values(LongValueKey.EquippedSlots, 0) > 0) return false;

            // skip wielded
            if (wo.Values(LongValueKey.Slot, -1) == -1) return false;

            return true;
        }

        public void Think(bool force=false) {
            if (force || DateTime.UtcNow - lastThought > TimeSpan.FromMilliseconds(THINK_INTERVAL)) {
                lastThought = DateTime.UtcNow;

                // dont run while vendoring
                if (Globals.Core.Actions.VendorId != 0) return;

                if ((!isRunning || isPaused) && !isForced) return;

                if (Globals.Config.InventoryManager.AutoCram.Value == true && AutoCram()) return;
                if (Globals.Config.InventoryManager.AutoStack.Value == true && AutoStack()) return;

                Stop();
            }
        }

        public bool TryCramItem(WorldObject stackThis) {
            // try to cram in side pack
            foreach (var container in Globals.Core.WorldFilter.GetInventory()) {
                int slot = container.Values(LongValueKey.Slot, -1);
                if (container.ObjectClass == ObjectClass.Container && slot >= 0 && !blacklistedContainers.ContainsKey(container.Id)) {
                    int freePackSpace = Util.GetFreePackSpace(container);

                    if (freePackSpace <= 0) continue;

                    if (Globals.Config.InventoryManager.Debug.Value == true) {
                        Util.WriteToChat(string.Format("AutoCram: trying to move {0} to {1}({2}) because it has {3} slots open",
                            Util.GetObjectName(stackThis.Id), container.Name, slot, freePackSpace));
                    }

                    // blacklist this container
                    if (tryCount > 10) {
                        tryCount = 0;
                        blacklistedContainers.Add(container.Id, DateTime.UtcNow);
                        continue;
                    }

                    movingObjectId = stackThis.Id;
                    tryCount++;

                    Globals.Core.Actions.MoveItem(stackThis.Id, container.Id, slot, false);
                    return true;
                }
            }

            return false;
        }

        public bool TryStackItem(WorldObject stackThis) {
            int stackThisSize = stackThis.Values(LongValueKey.StackCount, 1);

            // try to stack in side pack
            foreach (var container in Globals.Core.WorldFilter.GetInventory()) {
                if (container.ObjectClass == ObjectClass.Container && container.Values(LongValueKey.Slot, -1) >= 0) {
                    foreach (var wo in Globals.Core.WorldFilter.GetByContainer(container.Id)) {
                        if (blacklistedContainers.ContainsKey(container.Id)) continue;
                        if (TryStackItemTo(wo, stackThis, container.Values(LongValueKey.Slot))) return true;
                    }
                }
            }

            // try to stack in main pack
            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                if (TryStackItemTo(wo, stackThis, 0)) return true;
            }

            return false;
        }

        public bool TryStackItemTo(WorldObject wo, WorldObject stackThis, int slot=0) {
            int woStackCount = wo.Values(LongValueKey.StackCount, 1);
            int woStackMax = wo.Values(LongValueKey.StackMax, 1);
            int stackThisCount = stackThis.Values(LongValueKey.StackCount, 1);

            // not stackable?
            if (woStackMax <= 1 || stackThis.Values(LongValueKey.StackMax, 1) <= 1) return false;

            if (wo.Name == stackThis.Name && wo.Id != stackThis.Id && stackThisCount < woStackMax) {
                // blacklist this item
                if (tryCount > 10) {
                    tryCount = 0;
                    blacklistedItems.Add(stackThis.Id, DateTime.UtcNow);
                    return false;
                }

                if (woStackCount + stackThisCount <= woStackMax) {
                    if (Globals.Config.InventoryManager.Debug.Value == true) {
                        Util.WriteToChat(string.Format("InventoryManager::AutoStack stack {0}({1}) on {2}({3})",
                            Util.GetObjectName(stackThis.Id),
                            stackThisCount,
                            Util.GetObjectName(wo.Id),
                            woStackCount));
                    }
                    Globals.Core.Actions.SelectItem(stackThis.Id);
                    Globals.Core.Actions.MoveItem(stackThis.Id, wo.Container, slot, true);
                }
                else if (woStackMax - woStackCount == 0) {
                    return false;
                }
                else {
                    if (Globals.Config.InventoryManager.Debug.Value == true) {
                        Util.WriteToChat(string.Format("InventoryManager::AutoStack stack {0}({1}/{2}) on {3}({4})",
                            Util.GetObjectName(stackThis.Id),
                            woStackMax - woStackCount,
                            stackThisCount,
                            Util.GetObjectName(wo.Id),
                            woStackCount));
                    }
                    Globals.Core.Actions.SelectItem(stackThis.Id);
                    Globals.Core.Actions.SelectedStackCount = woStackMax - woStackCount;
                    Globals.Core.Actions.MoveItem(stackThis.Id, wo.Container, slot, true);
                }

                tryCount++;
                movingObjectId = stackThis.Id;
                return true;
            }

            return false;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    Globals.Core.CommandLineText -= Current_CommandLineText;
                    Globals.Core.WorldFilter.ChangeObject -= WorldFilter_ChangeObject;
                    Globals.Core.WorldFilter.CreateObject -= WorldFilter_CreateObject;
                }
                disposed = true;
            }
        }
    }
}
