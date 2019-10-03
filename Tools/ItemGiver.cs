﻿using System;
using System.Collections.Generic;
using System.IO;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using System.Text.RegularExpressions;
using System.Linq;
using UtilityBelt.Constants;

namespace UtilityBelt.Tools {
    class ItemGiver : IDisposable {


        List<WorldObject> inventoryItems = new List<WorldObject>();
        List<WorldObject> giveObjects = new List<WorldObject>();
        List<WorldObject> givenItems = new List<WorldObject>();
        List<WorldObject> idItems = new List<WorldObject>();
        List<WorldObject> buggedItems = new List<WorldObject>();
        List<WorldObject> droppedItems = new List<WorldObject>();
        List<int> blacklistedItems = new List<int>();

        private WorldObject currentItem = null;

        private DateTime lastThought = DateTime.MinValue;
        private DateTime lastScanUpdate = DateTime.MinValue;
        private DateTime lastAction = DateTime.MinValue;
        private DateTime lastDrop = DateTime.MinValue;
        private DateTime stopGive;
        private DateTime startGive;

        private bool isRunning = false;
        private bool disposed = false;
        private bool needsGiving = false;
        private bool gaveItem = false;
        private bool droppedItem = false;
        private bool isDroppingBuggedItems = false;

        private int retryCount;
        private int destinationId;
        private int giveSpeed = 300;
        private int failedItems = 0;
        private int currentContainer;
        private int newContainer;

        private object lootProfile;

        private double playerDistance;

        private string targetPlayer = "";
        private string utlProfile = "";


        public ItemGiver() {
            try {

                Directory.CreateDirectory(Path.Combine(Util.GetPluginDirectory(), "itemgiver"));

                CoreManager.Current.CommandLineText += new EventHandler<ChatParserInterceptEventArgs>(Current_CommandLineText);
                Globals.Core.ChatBoxMessage += new EventHandler<ChatTextInterceptEventArgs>(Current_ChatBoxMessage);
                Globals.Core.CharacterFilter.ChangePortalMode += new EventHandler<ChangePortalModeEventArgs>(WorldFilter_PortalChange);
                CoreManager.Current.WorldFilter.ChangeObject += new EventHandler<ChangeObjectEventArgs>(Current_TargetObjectChange);
                lastThought = DateTime.Now;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private Dictionary<string, int> givenItemsCount = new Dictionary<string, int>();

        public void Think() {
            try {
                //if itemgiver is running, and nav block has less than a second remaining, refresh it
                if (needsGiving && VTankControl.navBlockedUntil < DateTime.UtcNow + TimeSpan.FromSeconds(1)) {
                    VTankControl.Nav_Block(30000, false);
                }

                if (DateTime.Now - lastScanUpdate > TimeSpan.FromSeconds(10) && idItems.Count > 0) {
                    lastScanUpdate = DateTime.Now;
                    Util.WriteToChat("Items remaining to ID: " + idItems.Count());
                    if (idItems.Count == 0 && isRunning) {
                        Util.WriteToChat("Finished IDing Items");
                    }
                }

                if (DateTime.Now - lastAction > TimeSpan.FromSeconds(30)) {
                    lastAction = DateTime.Now;
                }

                if (DateTime.Now - lastDrop > TimeSpan.FromMilliseconds(2000) && isDroppingBuggedItems) {
                    currentContainer = 0;
                    newContainer = 0;
                    lastDrop = DateTime.Now;

                    if (isDroppingBuggedItems) {
                        buggedItems = GetDropItems();

                        foreach (WorldObject item in buggedItems) {
                            currentItem = item;
                            currentContainer = item.Values(LongValueKey.Container);
                            Util.WriteToChat("Dropping items: " + Util.GetObjectName(item.Id) + " ------- Container: " + currentContainer.ToString() + " ------- ID: " + item.Id);

                            if (!string.IsNullOrEmpty(item.Name) && CoreManager.Current.Actions.BusyState == 0 && !droppedItem && item.Values(LongValueKey.Container) != 0) {
                                CoreManager.Current.Actions.DropItem(item.Id);
                            }
                            return;
                        }
                        if (buggedItems.Count == 0 && idItems.Count == 0) {
                            Stop();
                        }
                        if (buggedItems.Count > 0) {
                            return;
                        }
                    }
                }

                if (DateTime.Now - lastThought > TimeSpan.FromMilliseconds(giveSpeed) && needsGiving && !isDroppingBuggedItems) {
                    lastThought = DateTime.Now;

                    if (isRunning) {
                        giveObjects = GetGiveItems();
                        playerDistance = CoreManager.Current.WorldFilter.Distance(CoreManager.Current.CharacterFilter.Id, destinationId) * 240;

                        foreach (WorldObject item in giveObjects) {
                            currentItem = item;
                            if (playerDistance > 5 || !needsGiving) {
                                Stop();
                                Util.WriteToChat("player is too far away");
                                return;
                            }

                            if (!string.IsNullOrEmpty(item.Name) && !gaveItem) {
                                retryCount++;
                                CoreManager.Current.Actions.GiveItem(item.Id, destinationId);
                                if (retryCount > 10) {
                                    giveObjects.Remove(item);
                                    Util.WriteToChat("unable to give " + Util.GetObjectName(item.Id));
                                    failedItems++;
                                    retryCount = 0;
                                }

                                if (failedItems > 3) Stop();

                                return;
                            }

                            return;
                        }

                        if (giveObjects.Count == 0 && idItems.Count == 0) {
                            Stop();
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }


        public static WorldObject GetLandscapeObject(string objectName) {
            WorldObject landscapeObject = null;
            try {

                foreach (WorldObject wo in CoreManager.Current.WorldFilter.GetLandscape()) {
                    if (wo.Name == objectName) {
                        landscapeObject = wo;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            return landscapeObject;
        }

        int FindPlayerID(string name) {
            // Exact match attempt first
            WorldObject playerObject = GetLandscapeObject(name);

            if (playerObject != null)
                return playerObject.Id;

            return -1;
        }

        private static readonly Regex giveRegex = new Regex(@"^\/ub ig (?<giveSpeed>\d+)?(?<utlProfile>.*) to (?<targetPlayer>.*)");
        private void Current_CommandLineText(object sender, ChatParserInterceptEventArgs e) {
            try {
                Match giveMatch = giveRegex.Match(e.Text);

                if (e.Text.StartsWith("/ub player ")) {
                    targetPlayer = e.Text.Replace("/ub player ", "").Trim();
                    destinationId = FindPlayerID(targetPlayer);
                    playerDistance = CoreManager.Current.WorldFilter.Distance(CoreManager.Current.CharacterFilter.Id, destinationId) * 240;

                    e.Eat = true;

                    if (destinationId == -1) {
                        Util.WriteToChat(targetPlayer + " " + destinationId.ToString());
                        return;
                    }
                    Util.WriteToChat(targetPlayer + " " + destinationId.ToString());
                    Util.WriteToChat(targetPlayer + " is " + playerDistance + " away");
                }
                if (e.Text.StartsWith("/ub ig abort") || e.Text.StartsWith("/ub ig stop")) {
                    e.Eat = true;
                    Stop();
                }
                if (e.Text.StartsWith("/ub scan inventory")) {
                    GetInventory();
                    e.Eat = true;
                }
                if (e.Text.StartsWith("/ub drop ")) {
                    CoreManager.Current.Actions.DropItem(CoreManager.Current.Actions.CurrentSelection);
                    e.Eat = true;
                }
                if (e.Text.StartsWith("/ub getstate")) {
                    Util.WriteToChat("Current State: " + CoreManager.Current.Actions.BusyState);
                    e.Eat = true;
                }
                if (e.Text.StartsWith("/ub dropbuggeditems")) {
                    e.Eat = true;
                    isDroppingBuggedItems = true;
                    Start();
                }
                if (giveMatch.Success && !isRunning) {
                    VTankControl.Nav_Block(1000, false); // quick block to keep vtank from truckin' off before the profile loads, but short enough to not matter if it errors out and doesn't unlock
                    utlProfile = giveMatch.Groups["utlProfile"].Value.Trim();
                    targetPlayer = giveMatch.Groups["targetPlayer"].Value.Trim();
                    destinationId = FindPlayerID(targetPlayer);
                    playerDistance = CoreManager.Current.WorldFilter.Distance(CoreManager.Current.CharacterFilter.Id, destinationId) * 240;
                    e.Eat = true;


                    if (targetPlayer == CoreManager.Current.CharacterFilter.Name) {
                        Util.WriteToChat("You can't give to yourself dumbass");
                        return;
                    }

                    string giveSpeedStr = giveMatch.Groups["giveSpeed"].Value.Trim();

                    if (Int32.TryParse(giveSpeedStr, out int result) && !(string.IsNullOrEmpty(giveSpeedStr))) {
                        giveSpeed = int.Parse(giveSpeedStr);
                    }


                    if (destinationId == -1) {
                        Util.WriteToChat(targetPlayer + " " + destinationId.ToString());
                        return;
                    }

                    Util.WriteToChat(utlProfile);

                    var pluginPath = Path.Combine(Util.GetPluginDirectory(), @"itemgiver");
                    var profilePath = Path.Combine(pluginPath, utlProfile);
                    Util.WriteToChat(profilePath.ToString());

                    if (!File.Exists(profilePath)) {
                        Util.WriteToChat("utl path: " + profilePath);
                        Util.WriteToChat("Profile does not exist: " + utlProfile.ToString());
                        return;
                    }

                    //C:\Users\Caleb\Documents\Decal Plugins\UtilityBelt\itemgiver\Electric Weapons.utl

                    var hasLootCore = false;
                    if (lootProfile == null) {
                        try {
                            lootProfile = new VTClassic.LootCore();
                            hasLootCore = true;
                        }
                        catch (Exception ex) { Logger.LogException(ex); }

                        if (!hasLootCore) {
                            Util.WriteToChat("Unable to load VTClassic, something went wrong.");
                            return;
                        }
                    }



                    ((VTClassic.LootCore)lootProfile).LoadProfile(profilePath, false);

                    Start();
                }
                else if (giveMatch.Success && isRunning) {
                    e.Eat = true;
                    Util.WriteToChat("ItemGiver is already running.  Please wait until it completes or use /ub ig stop to quit previous session");
                }

            }
            catch (Exception ex) { Logger.LogException(ex); }
        }


        public void Current_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                if (e.Text.StartsWith("You give ") && needsGiving && !gaveItem) {
                    lastAction = DateTime.Now;
                    gaveItem = true;
                    givenItems.Add(currentItem);
                    giveObjects.Remove(currentItem);
                    idItems.Remove(currentItem);
                    gaveItem = false;
                    retryCount = 0;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        void WorldFilter_PortalChange(object sender, ChangePortalModeEventArgs e) {
            try {
                // Util.WriteToChat("portal changed");
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        void Current_TargetObjectChange(object sender, ChangeObjectEventArgs e) {
            try {
                WorldObject wo = e.Changed;
                //Util.WriteToChat("TargetChange: " + e.Changed.Id);
                //Util.WriteToChat("TargetItem: " + currentItem.Id);
                newContainer = wo.Values(LongValueKey.Container);
                if (wo != null && currentItem != null && wo.Id == currentItem.Id) {
                    if (currentItem.Values(LongValueKey.Container) == 0 && isDroppingBuggedItems) {
                        droppedItem = true;
                        buggedItems.Remove(currentItem);
                        droppedItems.Add(currentItem);
                        if (idItems.Contains(currentItem)) {
                            //Util.WriteToChat("GATHERED INFO FOR " + item.Name);
                            idItems.Remove(currentItem);
                        }
                        Util.WriteToChat("dropped " + Util.GetObjectName(currentItem.Id) + " successfully" + " ------- Container: " + newContainer.ToString());
                        lastAction = DateTime.Now;
                        droppedItem = false;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void Start() {
            try {
                VTankControl.Nav_Block(30000.0, false); //need global debug

                startGive = DateTime.Now;
                if (!isDroppingBuggedItems) {
                    isRunning = true;
                    giveObjects = GetGiveItems();
                    needsGiving = true;
                }
                else if (isDroppingBuggedItems) {
                    lastDrop = DateTime.Now;
                    buggedItems = GetDropItems();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            //WriteItemsToChat(giveObjects);
        }

        private void WriteItemsToChat(List<WorldObject> list) {
            try {
                foreach (WorldObject item in list) {
                    Util.WriteToChat("Name: " + item.Name + "ID: " + item.Id + "Attuned: " + item.Values(LongValueKey.Attuned));
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void Stop() {
            try {
                if (isRunning) {
                    Util.Think("ItemGiver finished: " + utlProfile + " to " + targetPlayer);
                }
                else if (isDroppingBuggedItems) {
                    Util.Think("UtilityBelt: Dropped bugged items complete");
                }
                else {
                    Util.WriteToChat("ItemGiver complete.");
                }

                stopGive = DateTime.Now;
                TimeSpan duration = stopGive - startGive;
                //Util.WriteToChat(stopGive.ToString());
                //Util.WriteToChat(startGive.ToString());
                Util.WriteToChat("ItemGiver took " + Util.GetFriendlyTimeDifference(duration) + " to complete");
                VTankControl.Nav_UnBlock();
                Reset();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }


        public void Reset() {
            try {
                lastThought = DateTime.MinValue;
                lastScanUpdate = DateTime.MinValue;
                lastAction = DateTime.MinValue;
                lastDrop = DateTime.MinValue;
                stopGive = DateTime.MinValue;
                startGive = DateTime.MinValue;

                needsGiving = false;
                isRunning = false;
                isDroppingBuggedItems = false;
                gaveItem = false;
                droppedItem = false;

                currentItem = null;
                lootProfile = null;

                currentContainer = 0;
                newContainer = 0;
                targetPlayer = "";
                playerDistance = 0;
                utlProfile = "";
                destinationId = 0;
                retryCount = 0;
                failedItems = 0;
                giveSpeed = 300;

                inventoryItems.Clear();
                giveObjects.Clear();
                givenItems.Clear();
                idItems.Clear();
                buggedItems.Clear();
                droppedItems.Clear();
                givenItemsCount.Clear();
                blacklistedItems.Clear();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private bool NeedsID(int id) {
            return uTank2.PluginCore.PC.FLootPluginQueryNeedsID(id);
        }

        //this only runs when using /ub scan inventory
        private void GetInventory() {
            try {
                //inventoryItems.Clear();
                //if (!idsRequested) {
                foreach (WorldObject item in CoreManager.Current.WorldFilter.GetInventory()) {

                    // If the item is equipped or wielded, don't process it.
                    if (item.Values(LongValueKey.EquippedSlots, 0) > 0 || item.Values(LongValueKey.Slot, -1) == -1)
                        continue;

                    // If the item is equipped or wielded, don't process it.
                    if (item.Values(LongValueKey.Attuned) > 0)
                        continue;

                    if (inventoryItems.Contains(item)) continue;

                    // Convert the item into a VT GameItemInfo object
                    uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(item.Id);

                    if (itemInfo == null) {
                        // This happens all the time for aetheria that has been converted
                        Util.WriteToChat(item.Name + " has no data");
                        continue;
                    }

                    if (((VTClassic.LootCore)lootProfile).DoesPotentialItemNeedID(itemInfo)) {
                        CoreManager.Current.Actions.RequestId(item.Id);
                        Util.WriteToChat("VTANK - gathering info for " + item.Name);
                        
                        inventoryItems.Add(item);
                    }

                    Util.WriteToChat("scanned items: " + inventoryItems.Count());
                }

            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private List<WorldObject> GetDropItems() {
            try {
                foreach (WorldObject item in CoreManager.Current.WorldFilter.GetInventory()) {
                    // If the item is equipped or wielded, don't process it.
                    if (item.Values(LongValueKey.EquippedSlots, 0) > 0 || item.Values(LongValueKey.Slot, -1) == -1)
                        continue;

                    if (!NeedsID(item.Id) && idItems.Contains(item)) {
                        //Util.WriteToChat("GATHERED INFO FOR " + item.Name);
                        idItems.Remove(item);
                    }

                    if (NeedsID(item.Id)) {
                        CoreManager.Current.Actions.RequestId(item.Id);
                        //Util.WriteToChat("VTANK - gathering info for " + item.Name);
                        if (!idItems.Contains(item)) {
                            //Util.WriteToChat(item.Name.ToString());
                            idItems.Add(item);
                        }
                        continue;
                    }

                    // If the item is attuned, don't process it.
                    if (item.Values(LongValueKey.Attuned) > 0)
                        continue;

                    //If this items is bugged up on ACE skip that shit.
                    if (buggedItems.Contains(item))
                        continue;

                    if (droppedItems.Contains(item))
                        continue;

                    if (item.Values(LongValueKey.Container) == 0)
                        continue;

                    if ((item.Values(LongValueKey.Burden, 0) == 0 && item.Values(LongValueKey.Value, 0) <= 0) || (item.Name.StartsWith("Salvaged ") &&
                        item.Values(LongValueKey.Burden, 0) == 100 && item.Values(LongValueKey.Value, 0) == 10)) {

                        Util.WriteToChat("Bugged Item: " + Util.GetObjectName(item.Id));
                        buggedItems.Add(item);
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            return buggedItems;
        }

        private List<WorldObject> GetGiveItems() {
            try {
                foreach (WorldObject item in CoreManager.Current.WorldFilter.GetInventory()) {
                    // If the item is equipped or wielded, don't process it.
                    if (item.Values(LongValueKey.EquippedSlots, 0) > 0 || item.Values(LongValueKey.Slot, -1) == -1)
                        continue;

                    uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(item.Id);

                    if (itemInfo == null) {
                        // This happens all the time for aetheria that has been converted
                        continue;
                    }

                    if (givenItems.Contains(item))
                        continue;

                    if (giveObjects.Contains(item))
                        continue;

                    if (!((VTClassic.LootCore)lootProfile).DoesPotentialItemNeedID(itemInfo) && idItems.Contains(item)) {
                        idItems.Remove(item);
                    }

                    if (((VTClassic.LootCore)lootProfile).DoesPotentialItemNeedID(itemInfo)) {
                        CoreManager.Current.Actions.RequestId(item.Id);
                        if (!idItems.Contains(item)) {
                            idItems.Add(item);
                        }
                        continue;
                    }

                    uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);
                    if (!result.IsKeep && !result.IsKeepUpTo) {
                        continue;
                    }

                    if (result.IsKeepUpTo) {
                        if (!givenItemsCount.ContainsKey(result.RuleName)) {
                            givenItemsCount[result.RuleName] = 1;
                            Util.WriteToChat("Rule: " + result.RuleName + " ----------- Keep Count: " + result.Data1);
                        }
                        else if (givenItemsCount[result.RuleName] > 0 && givenItemsCount[result.RuleName] < result.Data1) {
                            givenItemsCount[result.RuleName]++;
                        }
                        else {
                            continue;
                        }
                    }

                    giveObjects.Add(item);
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            return giveObjects;
        }


        public void Dispose() {
            try {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    try {
                        Globals.Core.CommandLineText -= Current_CommandLineText;
                        CoreManager.Current.CommandLineText -= new EventHandler<ChatParserInterceptEventArgs>(Current_CommandLineText);
                        Globals.Core.ChatBoxMessage -= new EventHandler<ChatTextInterceptEventArgs>(Current_ChatBoxMessage);
                        Globals.Core.CharacterFilter.ChangePortalMode -= new EventHandler<ChangePortalModeEventArgs>(WorldFilter_PortalChange);
                        CoreManager.Current.WorldFilter.ChangeObject -= new EventHandler<ChangeObjectEventArgs>(Current_TargetObjectChange);
                    }
                    catch (Exception ex) { Logger.LogException(ex); }
                }
                disposed = true;
            }
        }
    }
}