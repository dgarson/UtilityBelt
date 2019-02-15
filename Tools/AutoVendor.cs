﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Decal.Adapter.Wrappers;
using Mag.Shared.Settings;
using UtilityBelt.Views;
using VirindiViewService.Controls;

namespace UtilityBelt.Tools {
    public static class VendorCache {
        public static Dictionary<int, double> SellRates = new Dictionary<int, double>();
        public static Dictionary<int, double> BuyRates = new Dictionary<int, double>();
        public static Dictionary<int, int> MaxValues = new Dictionary<int, int>();
        public static Dictionary<int, int> Categories = new Dictionary<int, int>();

        public static void AddVendor(Vendor vendor) {
            if (SellRates.ContainsKey(vendor.MerchantId)) {
                SellRates[vendor.MerchantId] = vendor.SellRate;
            }
            else {
                SellRates.Add(vendor.MerchantId, vendor.SellRate);
            }

            if (BuyRates.ContainsKey(vendor.MerchantId)) {
                BuyRates[vendor.MerchantId] = vendor.BuyRate;
            }
            else {
                BuyRates.Add(vendor.MerchantId, vendor.BuyRate);
            }

            if (MaxValues.ContainsKey(vendor.MerchantId)) {
                MaxValues[vendor.MerchantId] = vendor.MaxValue;
            }
            else {
                MaxValues.Add(vendor.MerchantId, vendor.MaxValue);
            }

            if (Categories.ContainsKey(vendor.MerchantId)) {
                Categories[vendor.MerchantId] = vendor.Categories;
            }
            else {
                Categories.Add(vendor.MerchantId, vendor.Categories);
            }
        }

        public static double GetSellRate(int vendorId) {
            if (SellRates.ContainsKey(vendorId)) {
                return SellRates[vendorId];
            }

            return 1;
        }

        public static double GetBuyRate(int vendorId) {
            if (BuyRates.ContainsKey(vendorId)) {
                return BuyRates[vendorId];
            }

            return 1;
        }

        public static int GetMaxValue(int vendorId) {
            if (MaxValues.ContainsKey(vendorId)) {
                return MaxValues[vendorId];
            }

            return 1;
        }

        public static int GetCategories(int vendorId) {
            if (Categories.ContainsKey(vendorId)) {
                return Categories[vendorId];
            }

            return 0;
        }
    }


    class AutoVendor : IDisposable {
        private const int MAX_VENDOR_BUY_COUNT = 5000;
        private const double PYREAL_STACK_SIZE = 25000.0;
        private DateTime firstThought = DateTime.MinValue;
        private DateTime lastThought = DateTime.MinValue;
        private DateTime startedVendoring = DateTime.MinValue;
        
        private bool disposed;
        private int AutoVendorTimeout = 60; // in seconds

        private bool needsVendoring = false;
        private object lootProfile;
        private bool needsToBuy = false;
        private bool needsToSell = false;
        private int stackItem = 0;
        private bool shouldStack = false;
        private string vendorName = "";
        private bool waitingForIds = false;
        private DateTime lastIdSpam = DateTime.MinValue;
        private bool needsToUse = false;
        HudCheckBox UIAutoVendorEnable { get; set; }
        HudCheckBox UIAutoVendorTestMode { get; set; }
        HudCheckBox UIAutoVendorDebug { get; set; }
        HudCheckBox UIAutoVendorShowMerchantInfo { get; set; }
        HudCheckBox UIAutoVendorThink { get; set; }
        HudHSlider UIAutoVendorSpeed { get; set; }
        HudStaticText UIAutoVendorSpeedText { get; set; }

        public AutoVendor() {
            try {
                Directory.CreateDirectory(Util.GetPluginDirectory() + @"autovendor\");
                Directory.CreateDirectory(Util.GetCharacterDirectory() + @"autovendor\");

                UIAutoVendorSpeedText = Globals.View.view != null ? (HudStaticText)Globals.View.view["AutoVendorSpeedText"] : new HudStaticText();
                UIAutoVendorSpeedText.Text = Globals.Config.AutoVendor.Speed.Value.ToString();

                UIAutoVendorEnable = Globals.View.view != null ? (HudCheckBox)Globals.View.view["AutoVendorEnabled"] : new HudCheckBox();
                UIAutoVendorEnable.Checked = Globals.Config.AutoVendor.Enabled.Value;
                UIAutoVendorEnable.Change += UIAutoVendorEnable_Change;
                Globals.Config.AutoVendor.Enabled.Changed += Config_AutoVendor_Enabled_Changed;

                UIAutoVendorTestMode = Globals.View.view != null ? (HudCheckBox)Globals.View.view["AutoVendorTestMode"] : new HudCheckBox();
                UIAutoVendorTestMode.Checked = Globals.Config.AutoVendor.TestMode.Value;
                UIAutoVendorTestMode.Change += UIAutoVendorTestMode_Change;
                Globals.Config.AutoVendor.TestMode.Changed += Config_AutoVendor_TestMode_Changed;

                UIAutoVendorDebug = Globals.View.view != null ? (HudCheckBox)Globals.View.view["AutoVendorDebug"] : new HudCheckBox();
                UIAutoVendorDebug.Checked = Globals.Config.AutoVendor.Debug.Value;
                UIAutoVendorDebug.Change += UIAutoVendorDebug_Change;
                Globals.Config.AutoVendor.Debug.Changed += Config_AutoVendor_Debug_Changed;

                UIAutoVendorShowMerchantInfo = Globals.View.view != null ? (HudCheckBox)Globals.View.view["AutoVendorShowMerchantInfo"] : new HudCheckBox();
                UIAutoVendorShowMerchantInfo.Checked = Globals.Config.AutoVendor.ShowMerchantInfo.Value;
                UIAutoVendorShowMerchantInfo.Change += UIAutoVendorShowMerchantInfo_Change;
                Globals.Config.AutoVendor.ShowMerchantInfo.Changed += Config_AutoVendor_ShowMerchantInfo_Changed;

                UIAutoVendorThink = Globals.View.view != null ? (HudCheckBox)Globals.View.view["AutoVendorThink"] : new HudCheckBox();
                UIAutoVendorThink.Checked = Globals.Config.AutoVendor.Think.Value;
                UIAutoVendorThink.Change += UIAutoVendorThink_Change;
                Globals.Config.AutoVendor.Think.Changed += Config_AutoVendor_Think_Changed;

                UIAutoVendorSpeed = Globals.View.view != null ? (HudHSlider)Globals.View.view["AutoVendorSpeed"] : new HudHSlider();
                UIAutoVendorSpeed.Position = (Globals.Config.AutoVendor.Speed.Value / 100) - 3;
                UIAutoVendorSpeed.Changed += UIAutoVendorSpeed_Changed;
                Globals.Config.AutoVendor.Speed.Changed += Config_AutoVendor_Speed_Changed;

                if (lootProfile == null) {
                    lootProfile = new VTClassic.LootCore();
                }

                Globals.Core.WorldFilter.ApproachVendor += WorldFilter_ApproachVendor;
            }
            catch (Exception ex) { Util.LogException(ex); }
        }

        private void UIAutoVendorEnable_Change(object sender, EventArgs e) {
            Globals.Config.AutoVendor.Enabled.Value = UIAutoVendorEnable.Checked;
        }

        private void Config_AutoVendor_Enabled_Changed(Setting<bool> obj) {
            UIAutoVendorEnable.Checked = Globals.Config.AutoVendor.Enabled.Value;
        }

        private void UIAutoVendorTestMode_Change(object sender, EventArgs e) {
            Globals.Config.AutoVendor.TestMode.Value = UIAutoVendorTestMode.Checked;
        }

        private void Config_AutoVendor_TestMode_Changed(Setting<bool> obj) {
            UIAutoVendorTestMode.Checked = Globals.Config.AutoVendor.TestMode.Value;
        }

        private void UIAutoVendorDebug_Change(object sender, EventArgs e) {
            Globals.Config.AutoVendor.Debug.Value = UIAutoVendorDebug.Checked;
        }

        private void Config_AutoVendor_Debug_Changed(Setting<bool> obj) {
            UIAutoVendorDebug.Checked = Globals.Config.AutoVendor.Debug.Value;
        }

        private void UIAutoVendorShowMerchantInfo_Change(object sender, EventArgs e) {
            Globals.Config.AutoVendor.ShowMerchantInfo.Value = UIAutoVendorShowMerchantInfo.Checked;
        }

        private void Config_AutoVendor_ShowMerchantInfo_Changed(Setting<bool> obj) {
            UIAutoVendorShowMerchantInfo.Checked = Globals.Config.AutoVendor.ShowMerchantInfo.Value;
        }

        private void UIAutoVendorThink_Change(object sender, EventArgs e) {
            Globals.Config.AutoVendor.Think.Value = UIAutoVendorThink.Checked;
        }

        private void Config_AutoVendor_Think_Changed(Setting<bool> obj) {
            UIAutoVendorThink.Checked = Globals.Config.AutoVendor.Think.Value;
        }

        private void UIAutoVendorSpeed_Changed(int min, int max, int pos) {
            var v = (pos * 100) + 300;
            if (v != Globals.Config.AutoVendor.Speed.Value) {
                Globals.Config.AutoVendor.Speed.Value = v;
                UIAutoVendorSpeedText.Text = v.ToString();
            }
        }

        private void Config_AutoVendor_Speed_Changed(Setting<int> obj) {
            //UIAutoVendorSpeed.Position = (Globals.Config.AutoVendor.Speed.Value / 100) - 300;
        }

        private void WorldFilter_ApproachVendor(object sender, ApproachVendorEventArgs e) {
            try {
                if (!Globals.Config.AutoVendor.Enabled.Value || !Globals.Core.Actions.IsValidObject(e.MerchantId)) {
                    Stop();
                    return;
                }

                if (needsVendoring) return;

                VendorCache.AddVendor(e.Vendor);

                var merchant = Globals.Core.WorldFilter[e.MerchantId];
                var profilePath = GetMerchantProfilePath(merchant);

                vendorName = merchant.Name;

                if (!File.Exists(profilePath)) {
                    Util.WriteToChat("No vendor profile exists: " + profilePath);
                    return;
                }

                if (Globals.Config.AutoVendor.ShowMerchantInfo.Value == true) {
                    Util.WriteToChat(merchant.Name);
                    Util.WriteToChat(string.Format("BuyRate: {0}% SellRate: {1}% MaxValue: {2:n0}", e.Vendor.BuyRate*100, e.Vendor.SellRate*100, e.Vendor.MaxValue));
                }

                /*
                if (Assessor.NeedsInventoryData()) {
                    Assessor.RequestAll();
                    waitingForIds = true;
                    lastIdSpam = DateTime.UtcNow;
                }
                */

                // Load our loot profile
                ((VTClassic.LootCore)lootProfile).LoadProfile(profilePath, false);
                
                needsVendoring = true;
                needsToBuy = false;
                needsToSell = false;
                startedVendoring = DateTime.UtcNow;

                Globals.Core.WorldFilter.CreateObject += WorldFilter_CreateObject;
            }
            catch (Exception ex) { Util.LogException(ex); }
        }

        private void WorldFilter_CreateObject(object sender, CreateObjectEventArgs e) {
            try {
                if (shouldStack && e.New.Values(LongValueKey.StackMax, 1) > 1) {
                    // TODO: multipass stacking
                    stackItem = e.New.Id;

                    lastThought = DateTime.UtcNow;
                }
            }
            catch (Exception ex) { Util.LogException(ex); }
        }
        
        private string GetMerchantProfilePath(WorldObject merchant) {
            // TODO: support more than utl?
            if (File.Exists(Util.GetCharacterDirectory() + @"autovendor\" + merchant.Name + ".utl")) {
                return Util.GetCharacterDirectory() + @"autovendor\" + merchant.Name + ".utl";
            }
            else if (File.Exists(Util.GetPluginDirectory() + @"autovendor\" + merchant.Name + ".utl")) {
                return Util.GetPluginDirectory() + @"autovendor\" + merchant.Name + ".utl";
            }
            else if (File.Exists(Util.GetCharacterDirectory() + @"autovendor\default.utl")) {
                return Util.GetCharacterDirectory() + @"autovendor\default.utl";
            }
            else if (File.Exists(Util.GetPluginDirectory() + @"autovendor\default.utl")) {
                return Util.GetPluginDirectory() + @"autovendor\default.utl";
            }

            return Util.GetPluginDirectory() + @"autovendor\" + merchant.Name + ".utl";
        }

        public void Stop() {
            if (Globals.Config.AutoVendor.Debug.Value == true) {
                Util.WriteToChat("AutoVendor:Stop");
            }

            needsVendoring = false;
            needsToBuy = false;
            needsToSell = false;
            vendorName = "";

            Globals.Core.WorldFilter.CreateObject += WorldFilter_CreateObject;

            if (lootProfile != null) ((VTClassic.LootCore)lootProfile).UnloadProfile();
        }

        public void Think() {
            try {
                var thinkInterval = TimeSpan.FromMilliseconds(Globals.Config.AutoVendor.Speed.Value);

                if (Globals.Config.AutoVendor.Enabled.Value == false) return;

                if (DateTime.UtcNow - lastThought >= thinkInterval && DateTime.UtcNow - startedVendoring >= thinkInterval) {
                    lastThought = DateTime.UtcNow;
                    
                    if (needsVendoring && waitingForIds) {
                        if (Assessor.NeedsInventoryData()) {
                            if (DateTime.UtcNow - lastIdSpam > TimeSpan.FromSeconds(10)) {
                                lastIdSpam = DateTime.UtcNow;
                                startedVendoring = DateTime.UtcNow;

                                if (Globals.Config.AutoVendor.Debug.Value == true) {
                                    Util.WriteToChat(string.Format("AutoVendor Waiting to id {0} items, this will take approximately {0} seconds.", Assessor.GetNeededIdCount()));
                                }
                            }

                            // waiting
                            return;
                        }
                        else {
                            waitingForIds = false;
                        }
                    }

                    if (needsVendoring && Globals.Config.AutoVendor.TestMode.Value == true) {
                        DoTestMode();
                        Stop();
                        return;
                    }

                    if (stackItem != 0 && Globals.Core.Actions.IsValidObject(stackItem)) {
                        Util.StackItem(Globals.Core.WorldFilter[stackItem]);
                        stackItem = 0;
                        return;
                    }

                    if (needsToUse) {
                        if (Globals.Core.Actions.VendorId != 0) {
                            Globals.Core.Actions.UseItem(Globals.Core.Actions.VendorId, 0);
                        }

                        needsToUse = false;
                        return;
                    }

                    if (needsVendoring == true && HasVendorOpen()) {
                        if (DateTime.UtcNow - startedVendoring > TimeSpan.FromSeconds(AutoVendorTimeout)) {
                            Util.WriteToChat(string.Format("AutoVendor timed out after {0} seconds.", AutoVendorTimeout));
                            Stop();
                            return;
                        }

                        if (needsToBuy) {
                            needsToBuy = false;
                            shouldStack = true;
                            Globals.Core.Actions.VendorBuyAll();
                        }
                        else if (needsToSell) {
                            needsToSell = false;
                            shouldStack = false;
                            Globals.Core.Actions.VendorSellAll();
                            needsToUse = true;
                        }
                        else {
                            DoVendoring();
                        }
                    }
                    // vendor closed?
                    else if (needsVendoring == true) {
                        Stop();
                    }
                }
            }
            catch (Exception ex) { Util.LogException(ex); }
        }

        private void DoVendoring() {
            try {
                int amount = 0;
                int nextBuyItemId = GetNextBuyItem(out amount);
                List<WorldObject> sellItems = GetSellItems();

                if (!HasVendorOpen()) {
                    if (Globals.Config.AutoVendor.Debug.Value == true) {
                        Util.WriteToChat("AutoVendor vendor was closed, stopping!");
                    }
                    Stop();
                    return;
                }

                if (Globals.Config.AutoVendor.Debug.Value == true) {
                    Util.WriteToChat("AutoVendor:DoVendoring");
                }

                using (var nextBuyItem = Globals.Core.WorldFilter.OpenVendor[nextBuyItemId]) {
                    Globals.Core.Actions.VendorClearBuyList();
                    Globals.Core.Actions.VendorClearSellList();

                    if (nextBuyItem != null && CanBuy(nextBuyItem)) {
                        int buyCount = 1;

                        // TODO check stack size of incoming item to make sure we have enough space...

                        while (buyCount < amount && GetVendorSellPrice(nextBuyItem) * (buyCount + 1) <= Util.PyrealCount()) {
                            ++buyCount;
                        }

                        if (Globals.Config.AutoVendor.Debug.Value == true) {
                            Util.WriteToChat(string.Format("AutoVendor Buying {0} {1}", buyCount, nextBuyItem.Name));
                        }

                        Globals.Core.Actions.VendorAddBuyList(nextBuyItem.Id, buyCount);
                        needsToBuy = true;
                        return;
                    }

                    int totalSellValue = 0;
                    int sellItemCount = 0;

                    while (sellItemCount < sellItems.Count && sellItemCount < Util.GetFreeMainPackSpace()) {
                        var item = sellItems[sellItemCount];
                        var value = GetVendorBuyPrice(item);
                        var stackSize = item.Values(LongValueKey.StackCount, 1);
                        var stackCount = 0;

                        // dont sell notes if we are trying to buy notes...
                        if (((nextBuyItem != null && nextBuyItem.ObjectClass == ObjectClass.TradeNote) || nextBuyItem == null) && item.ObjectClass == ObjectClass.TradeNote) {

                            if (Globals.Config.AutoVendor.Debug.Value == true) {
                                Util.WriteToChat(string.Format("AutoVendor bail: buyItem: {0} sellItem: {1}", nextBuyItem == null ? "null" : nextBuyItem.Name, item.Name));
                            }
                            break;
                        }

                        // if we are selling notes to buy something, sell the minimum amount...
                        if (nextBuyItem != null && !CanBuy(nextBuyItem) && item.ObjectClass == ObjectClass.TradeNote) {
                            if (!PyrealsWillFitInMainPack(GetVendorBuyPrice(item))) {
                                Util.WriteToChat("AutoVendor No inventory room to sell... " + item.Name);
                                Stop();
                                return;
                            }

                            foreach (var wo in Globals.Core.WorldFilter.GetInventory()) {
                                if (wo.Name == item.Name && wo.Values(LongValueKey.StackCount, 0) == 1) {
                                    if (Globals.Config.AutoVendor.Debug.Value == true) {
                                        Util.WriteToChat("AutoVendor Selling single " + wo.Name + " so we can afford to buy: " + nextBuyItem.Name);
                                    }
                                    Globals.Core.Actions.VendorAddSellList(wo.Id);
                                    needsToSell = true;
                                    return;
                                }
                            }

                            Globals.Core.Actions.SelectItem(item.Id);
                            Globals.Core.Actions.SelectedStackCount = 1;
                            Globals.Core.Actions.MoveItem(item.Id, Globals.Core.CharacterFilter.Id, 0, false);

                            if (Globals.Config.AutoVendor.Debug.Value == true) {
                                Util.WriteToChat(string.Format("AutoVendor Splitting {0}. old: {1} new: {2}", item.Name, item.Values(LongValueKey.StackCount), 1));
                            }

                            shouldStack = false;

                            return;
                        }

                        if (item.Values(LongValueKey.StackMax, 0) > 1) {
                            while (stackCount <= stackSize) {
                                if (!PyrealsWillFitInMainPack(totalSellValue + (value * stackCount))) {
                                    if (item.Values(LongValueKey.StackCount, 1) > 1) {
                                        Globals.Core.Actions.SelectItem(item.Id);
                                        Globals.Core.Actions.SelectedStackCount = stackCount;
                                        Globals.Core.Actions.MoveItem(item.Id, Globals.Core.CharacterFilter.Id, 0, false);
                                        if (Globals.Config.AutoVendor.Debug.Value == true) {
                                            Util.WriteToChat(string.Format("AutoVendor Splitting {0}. old: {1} new: {2}", item.Name, item.Values(LongValueKey.StackCount), stackCount));
                                        }

                                        shouldStack = false;

                                        return;
                                    }
                                }

                                ++stackCount;
                            }
                        }
                        else {
                            stackCount = 1;
                        }

                        if (!PyrealsWillFitInMainPack(totalSellValue + (value * stackCount))) {
                            break;
                        }

                        if (Globals.Config.AutoVendor.Debug.Value == true) {
                            Util.WriteToChat(string.Format("AutoVendor Adding {0} to sell list", item.Name));
                        }

                        Globals.Core.Actions.VendorAddSellList(item.Id);

                        totalSellValue += value * stackCount;
                        ++sellItemCount;
                    }

                    if (sellItemCount > 0) {
                        needsToSell = true;
                        return;
                    }

                    if (Globals.Config.AutoVendor.Think.Value == true) {
                        Util.Think("AutoVendor " + vendorName + " finished.");
                    }
                    else {
                        Util.WriteToChat("AutoVendor " + vendorName + " finished.");
                    }
                    Stop();
                }
            }
            catch (Exception ex) { Util.LogException(ex); }
        }

        private int GetVendorSellPrice(WorldObject wo) {
            var price = 0;
            int vendorId = Globals.Core.Actions.VendorId;
            var rate = VendorCache.GetSellRate(vendorId);

            try {
                if (vendorId == 0) return 0;
                if (wo.ObjectClass == ObjectClass.TradeNote) rate = 1.15;

                price = (int)Math.Ceiling((wo.Values(LongValueKey.Value, 0) / wo.Values(LongValueKey.StackCount, 1)) * rate);
            }
            catch (Exception ex) { }

            return price;
        }

        private int GetVendorBuyPrice(WorldObject wo) {
            var price = 0;
            int vendorId = Globals.Core.Actions.VendorId;
            var rate = VendorCache.GetBuyRate(vendorId);

            try {
                if (vendorId == 0) return 0;
                if (wo.ObjectClass == ObjectClass.TradeNote) rate = 1;

                price = (int)Math.Floor((wo.Values(LongValueKey.Value, 0) / wo.Values(LongValueKey.StackCount, 1)) * rate);
            }
            catch (Exception ex) { }

            return price;
        }

        private bool CanBuy(WorldObject nextBuyItem) {
            if (nextBuyItem == null) return false;

            return Util.PyrealCount() >= GetVendorSellPrice(nextBuyItem);
        }

        private bool PyrealsWillFitInMainPack(int amount) {
            int packSlotsNeeded = (int)Math.Ceiling(amount / PYREAL_STACK_SIZE);

            return Util.GetFreeMainPackSpace() > packSlotsNeeded;
        }

        private int GetNextBuyItem(out int amount) {
            using (Vendor openVendor = Globals.Core.WorldFilter.OpenVendor) {
                amount = 0;

                if (openVendor == null || openVendor.MerchantId == 0) {
                    Util.WriteToChat("AutoVendor Bad Merchant");
                    return 0;
                }

                // keepUpTo rules first, just like mag-tools
                foreach (WorldObject wo in openVendor) {
                    uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithVendorObjectTemplateID(wo.Id);

                    if (itemInfo == null) continue;

                    uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);

                    if (!result.IsKeepUpTo) continue;

                    amount = result.Data1 - Util.GetItemCountInInventoryByName(wo.Name);

                    if (amount > wo.Values(LongValueKey.StackMax, 1)) {
                        amount = wo.Values(LongValueKey.StackMax, 1);
                    }

                    if (amount > MAX_VENDOR_BUY_COUNT) amount = MAX_VENDOR_BUY_COUNT;

                    if (amount > 0) {
                        if (Globals.Config.AutoVendor.Debug.Value == true) {
                            Util.WriteToChat("Buy " + wo.Name);
                        }
                        return wo.Id;
                    }
                }

                // keep rules next
                foreach (WorldObject wo in openVendor) {
                    uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithVendorObjectTemplateID(wo.Id);

                    if (itemInfo == null) continue;

                    uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);

                    if (!result.IsKeep) continue;

                    amount = MAX_VENDOR_BUY_COUNT;
                    return wo.Id;
                }
            }

            return 0;
        }
        private List<WorldObject> GetSellItems() {
            List<WorldObject> sellObjects = new List<WorldObject>();
            foreach (WorldObject wo in Globals.Core.WorldFilter.GetInventory()) {
                if (!Util.ItemIsSafeToGetRidOf(wo) || !ItemIsSafeToGetRidOf(wo)) continue;

                if (wo.Values(LongValueKey.Value, 0) <= 0) continue;

                uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(wo.Id);

                if (itemInfo == null) continue;

                uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);

                if (!result.IsSell)
                    continue;
                

                // too expensive for this vendor
                if (VendorCache.GetMaxValue(Globals.Core.Actions.VendorId) < wo.Values(LongValueKey.Value, 0)) continue;

                
                // will vendor buy this item?
                if (wo.ObjectClass != ObjectClass.TradeNote && (VendorCache.GetCategories(Globals.Core.Actions.VendorId) & wo.Category) == 0) {
                    continue;
                }

                sellObjects.Add(wo);
            }

            sellObjects.Sort(
                delegate (WorldObject wo1, WorldObject wo2) {
                // tradenotes last
                if (wo1.ObjectClass == ObjectClass.TradeNote && wo2.ObjectClass != ObjectClass.TradeNote) return 1;

                // then cheapest first
                if (wo1.Values(LongValueKey.Value, 0) > wo2.Values(LongValueKey.Value, 0)) return 1;
                    if (wo1.Values(LongValueKey.Value, 0) < wo2.Values(LongValueKey.Value, 0)) return -1;

                // then smallest stack size
                if (wo1.Values(LongValueKey.StackCount, 1) > wo2.Values(LongValueKey.StackCount, 1)) return 1;
                    if (wo1.Values(LongValueKey.StackCount, 1) < wo2.Values(LongValueKey.StackCount, 1)) return -1;

                    return 0;
                }
            );

            return sellObjects;
        }

        private bool ItemIsSafeToGetRidOf(WorldObject wo) {
            if (wo == null) return false;

            // dont sell items with descriptions (quest items)
            // peas have descriptions...
            //if (wo.Values(StringValueKey.FullDescription, "").Length > 1) return false;

            // can be sold?
            //if (wo.Values(BoolValueKey.CanBeSold, false) == false) return false;

            // no attuned
            if (wo.Values(LongValueKey.Attuned, 0) > 1) return false;

            return true;
        }

        private void DoTestMode() {
            Util.WriteToChat("Buy Items:");

            using (Vendor openVendor = Globals.Core.WorldFilter.OpenVendor) {
                foreach (WorldObject vendorObj in openVendor) {
                    uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithVendorObjectTemplateID(vendorObj.Id);

                    if (itemInfo == null) continue;
                    
                    uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);

                    if (result.IsKeepUpTo && result.Data1 - Util.GetItemCountInInventoryByName(vendorObj.Name) > 0) {
                        Util.WriteToChat(string.Format("  {0} * {1} - {2}", vendorObj.Name, result.Data1 - Util.GetItemCountInInventoryByName(vendorObj.Name), result.RuleName));
                    }
                    else if (result.IsKeep) {
                        Util.WriteToChat("  " + vendorObj.Name + " - " + result.RuleName);
                    }
                }
            }

            Util.WriteToChat("Sell Items:");

            foreach (WorldObject wo in GetSellItems()) {
                uTank2.LootPlugins.GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(wo.Id);

                if (itemInfo == null) continue;
                
                uTank2.LootPlugins.LootAction result = ((VTClassic.LootCore)lootProfile).GetLootDecision(itemInfo);

                if (result.IsSell) {
                    Util.WriteToChat("  " + wo.Name + " - " + result.RuleName);
                }
            }
        }

        private bool HasVendorOpen() {
            bool hasVendorOpen = false;

            try {
                if (Globals.Core.Actions.VendorId == 0) return false;

                hasVendorOpen = true;
            }
            catch (Exception ex) { Util.LogException(ex); }

            return hasVendorOpen;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    Globals.Core.WorldFilter.ApproachVendor -= WorldFilter_ApproachVendor;
                }
                disposed = true;
            }
        }
    }
}
