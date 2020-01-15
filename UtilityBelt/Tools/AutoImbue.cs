﻿using System;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using UtilityBelt.Lib.Salvage;
using UtilityBelt.Lib.Constants;
using VirindiViewService.Controls;
using System.Data;
using Decal.Filters;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UtilityBelt.Lib;

namespace UtilityBelt.Tools {
    [Name("AutoImbue")]
    public class AutoImbue : ToolBase {
        readonly HudList AutoImbueList;
        readonly HudButton AutoImbueStartButton;
        readonly HudButton AutoImbueStopButton;
        readonly HudCombo AutoImbueSalvageCombo;
        readonly HudCombo AutoImbueDmgTypeCombo;
        readonly HudButton AutoImbueRefreshListButton;
        internal DataTable tinkerDT = new DataTable();

        private bool waitingForIds = false;
        private DateTime lastIdSpam = DateTime.MinValue;
        private DateTime startTime = DateTime.MinValue;
        private DateTime endTime = DateTime.MinValue;
        private int lastIdCount;
        private bool tinking = false;
        private readonly List<int> itemsToId = new List<int>();
        int targetSalvage;
        double currentSalvageWK;
        string currentSalvage;
        string currentItemName;
        int currentItemID;
        double imbueChance;
        int targetItem;
        int imbueCount;
        readonly TinkerCalc tinkerCalc = new TinkerCalc();
        readonly Dictionary<string, int> SalvageList = new Dictionary<string, int>();
        readonly List<string> dmgList = new List<string>();
        readonly Dictionary<int, double> PotentialSalvageList = new Dictionary<int, double>();
        readonly Dictionary<int, double> PotentialWeaponList = new Dictionary<int, double>();
        readonly Dictionary<string, int> DefaultImbueList = new Dictionary<string, int>();

        public AutoImbue(UtilityBeltPlugin ub, string name) : base(ub, name) {
            try {
                CoreManager.Current.ChatBoxMessage += Current_ChatBoxMessage;

                AutoImbueList = (HudList)UB.MainView.view["AutoImbueList"];
                AutoImbueList.Click += AutoImbueList_Click;

                AutoImbueSalvageCombo = (HudCombo)UB.MainView.view["AutoImbueSalvageCombo"];
                AutoImbueSalvageCombo.Change += AutoImbueSalvageCombo_Change;

                AutoImbueDmgTypeCombo = (HudCombo)UB.MainView.view["AutoImbueDmgTypeCombo"];
                AutoImbueDmgTypeCombo.Change += AutoImbueDmgTypeCombo_Change;

                AutoImbueStartButton = (HudButton)UB.MainView.view["AutoImbueStartButton"];
                AutoImbueStartButton.Hit += AutoImbueStartButton_Hit;

                AutoImbueStopButton = (HudButton)UB.MainView.view["AutoImbueStopButton"];
                AutoImbueStopButton.Hit += AutoImbueStopButton_Hit;

                AutoImbueRefreshListButton = (HudButton)UB.MainView.view["AutoImbueRefreshListButton"];
                AutoImbueRefreshListButton.Hit += AutoImbueRefreshListButton_Hit;

                PopulateAutoImbueSalvageCombo();
                PopulateAutoImbueDmgTypeCombo();
                BuildDefaultImbueList();

                HudStaticText c = (HudStaticText)(AutoImbueDmgTypeCombo[AutoImbueDmgTypeCombo.Current]);
                SelectDefaultSalvage(c.Text.ToString());

                UB.Core.RenderFrame += Core_RenderFrame;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void Core_RenderFrame(object sender, EventArgs e) {
            try {
                    if (waitingForIds) {
                        if (UB.Assessor.NeedsInventoryData(itemsToId)) {
                            if (DateTime.UtcNow - lastIdSpam > TimeSpan.FromSeconds(15)) {
                                lastIdSpam = DateTime.UtcNow;
                                var thisIdCount = UB.Assessor.GetNeededIdCount(itemsToId);
                                Util.WriteToChat(string.Format("AutoImbue waiting to id {0} items, this will take approximately {0} seconds.", thisIdCount));
                                if (lastIdCount != thisIdCount) { // if count has changed, reset bail timer
                                    lastIdCount = thisIdCount;
                                    //bailTimer = DateTime.UtcNow;
                                }
                            }
                            return;
                        }
                    else {
                        waitingForIds = false;
                        endTime = DateTime.UtcNow;
                        Util.WriteToChat("AutoImbue: took " + Util.GetFriendlyTimeDifference(endTime - startTime) + " to scan");
                        ClearAllTinks();
                        GetPotentialItems();
                    }
                }
            }
            catch (Exception ex) {Logger.LogException(ex);}
        }

        private void NextTink() {
            try {
                CoreManager.Current.Actions.RequestId(currentItemID);
                AutoImbueList.RemoveRow(0);
                tinking = false;
                if (AutoImbueList.RowCount >= 1) {
                    DoTinks();
                }
                else {
                    Util.WriteToChat("There are " + AutoImbueList.RowCount.ToString() + " in the list.");
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

        }

        private void AutoImbueList_Click(object sender, int row, int col) {
            try {
                HudList.HudListRowAccessor imbueListRow = AutoImbueList[row];
                HudStaticText itemID = (HudStaticText)imbueListRow[5];
                int.TryParse(itemID.Text.ToString(), out int item);
                CoreManager.Current.Actions.SelectItem(item);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void Current_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                if (tinking) {
                    string characterName = CoreManager.Current.CharacterFilter.Name;

                    Regex CraftSuccess = new Regex("^" + characterName + @" successfully applies the (?<salvage>[\w\s\-]+) Salvage(\s?\(100\))?\s\(workmanship (?<workmanship>\d+\.\d+)\) to the (?<item>[\w\s\-]+)\.$");
                    Regex CraftFailure = new Regex("^" + characterName + @" fails to apply the (?<salvage>[\w\s\-]+) Salvage(\s?\(100\))?\s\(workmanship (?<workmanship>\d+\.\d+)\) to the (?<item>[\w\s\-]+).\s?The target is destroyed\.$");

                    if (CraftSuccess.IsMatch(e.Text.Trim())) {
                        var match = CraftSuccess.Match(e.Text.Trim());
                        string result = "success";
                        string chatcapSalvage = match.Groups["salvage"].Value;
                        string chatcapWK = match.Groups["workmanship"].Value;
                        string chatcapItem = match.Groups["item"].Value;
                        if (currentItemName == chatcapItem && currentSalvageWK.ToString("0.00") == chatcapWK.ToString() && chatcapSalvage == currentSalvage) {
                            Logger.Debug(result + ": " + chatcapSalvage + " " + chatcapWK + " on " + chatcapItem);
                            NextTink();
                        }
                        else {
                            Logger.Debug("AutoTinker: did not match success" + "chat salvageWK: " + chatcapWK.ToString() + "real salvageWK: " + currentSalvageWK.ToString("0.00") + "chat item name: " + chatcapItem + "real item name: " + currentItemName + "chat salv name: " + chatcapSalvage + "real salv name: " + currentSalvage);
                        }
                    }


                    if (CraftFailure.IsMatch(e.Text.Trim())) {
                        var match = CraftFailure.Match(e.Text.Trim());
                        string result = "failure";
                        string chatcapSalvage = match.Groups["salvage"].Value.Replace(" Salvage", "");
                        string chatcapWK = match.Groups["workmanship"].Value;
                        string chatcapItem = match.Groups["item"].Value;
                        if (currentItemName == chatcapItem && currentSalvageWK.ToString("0.00") == chatcapWK.ToString() && chatcapSalvage == currentSalvage) {
                            Logger.Debug(result + ": " + chatcapSalvage + " " + chatcapWK + " on " + chatcapItem);
                            NextTink();
                        }
                        else {
                            Logger.Debug("AutoTinker: did not match failure" + "chat salvageWK: " + chatcapWK.ToString() + "real salvageWK: " + currentSalvageWK.ToString("0.00") + "chat item name: " + chatcapItem + "real item name: " + currentItemName + "chat salv name: " + chatcapSalvage + "real salv name: " + currentSalvage);
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void BuildDefaultImbueList() {
            DefaultImbueList.Add("Acid", 21);
            DefaultImbueList.Add("Bludgeoning", 47);
            DefaultImbueList.Add("Cold", 13);
            DefaultImbueList.Add("Electric", 27);
            DefaultImbueList.Add("Fire", 35);
            DefaultImbueList.Add("Nether", 16);
            DefaultImbueList.Add("Piercing", 15);
            DefaultImbueList.Add("Slashing", 26);
            DefaultImbueList.Add("SlashPierce", 26);
            DefaultImbueList.Add("Normal", 16);
        }

        public void AutoImbueSalvageCombo_Change(object sender, EventArgs e) {
            try {
                ClearAllTinks();
                GetPotentialItems();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void AutoImbueDmgTypeCombo_Change(object sender, EventArgs e) {
            try {
                HudStaticText c = (HudStaticText)(AutoImbueDmgTypeCombo[AutoImbueDmgTypeCombo.Current]);
                SelectDefaultSalvage(c.Text.ToString());
                ClearAllTinks();
                GetPotentialItems();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void SelectDefaultSalvage(string dmgType) {
            FileService service = CoreManager.Current.Filter<FileService>();

            foreach (KeyValuePair<string, int> row in DefaultImbueList) {
                if (dmgType == row.Key.ToString()) {
                    var selectSalvage = service.MaterialTable[row.Value];
                    for (int r = 0; r < AutoImbueSalvageCombo.Count; r++) {
                        HudStaticText sal = (HudStaticText)(AutoImbueSalvageCombo[r]);
                        if (sal.Text.ToString() == selectSalvage.ToString()) {
                            AutoImbueSalvageCombo.Current = r;
                        }
                    }
                }
            }
        }

        private void AutoImbueRefreshListButton_Hit(object sender, EventArgs e) {
            try {
                ClearAllTinks();
                GetPotentialItems();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void AutoImbueStartButton_Hit(object sender, EventArgs e) {
            try {
                for (int i = 0; i < AutoImbueList.RowCount; i++) {
                    DoTinks();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void AutoImbueStopButton_Hit(object sender, EventArgs e) {
            try {
                ClearAllTinks();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void PopulateAutoImbueSalvageCombo() {
            FileService service = CoreManager.Current.Filter<FileService>();
            for (int i = 0; i < service.MaterialTable.Length; i++) {
                var material = service.MaterialTable[i];
                if (TinkerType.SalvageType(i) == 2) {
                    SalvageList.Add(material.Name, i);
                }
            }
            var SortedSalvageList = SalvageList.Keys.ToList();
            foreach (var item in SalvageList.OrderBy(i => i.Key)) {
                AutoImbueSalvageCombo.AddItem(item.Key.ToString(), null);
            }

            //AutoImbueSalvageCombo.AddItem("Granite/Iron", null);
        }

        private void PopulateAutoImbueDmgTypeCombo() {

            var dmgTypes = Enum.GetValues(typeof(Lib.Constants.DamageTypes));
            foreach (var dmg in dmgTypes) {
                dmgList.Add(dmg.ToString());
            }
            var sortedDmgList = from element in dmgList
                                orderby element
                                select element;
            foreach (var dmg in sortedDmgList) {
                AutoImbueDmgTypeCombo.AddItem(dmg.ToString(), null);
            }
        }

        public void GetPotentialItems() {
            try {
                tinking = false;
                HudStaticText c = (HudStaticText)(AutoImbueSalvageCombo[AutoImbueSalvageCombo.Current]);
                bool skipWands = false;
                if (c.Text.ToString().Contains("Sunstone")) {
                    skipWands = true;
                }

                HudStaticText salvage = (HudStaticText)(AutoImbueSalvageCombo[AutoImbueSalvageCombo.Current]);
                var materialID = SalvageList[salvage.Text.ToString()];
                HudStaticText dmgType = (HudStaticText)(AutoImbueDmgTypeCombo[AutoImbueDmgTypeCombo.Current]);
                var dmgTypeString = dmgType.Text.ToString();
                uint dmgTypeID = 0;
                dmgTypeID = (uint)Enum.Parse(typeof(DamageTypes), dmgTypeString.Trim());


                using (var inv = CoreManager.Current.WorldFilter.GetInventory()) {
                    foreach (var item in inv) {
                        itemsToId.Add(item.Id);
                    }
                }

                if (UB.Assessor.NeedsInventoryData(itemsToId)) {
                    UB.Assessor.RequestAll(itemsToId);
                    waitingForIds = true;
                    lastIdSpam = DateTime.UtcNow;
                    startTime = DateTime.UtcNow;
                }

                using (var inv = CoreManager.Current.WorldFilter.GetInventory()) {
                    foreach (WorldObject wo in inv) {
                        if (wo.Values(LongValueKey.Material) == materialID && wo.Values(LongValueKey.UsesRemaining) == 100) {
                            if (!PotentialSalvageList.ContainsKey(wo.Id)) {
                                PotentialSalvageList.Add(wo.Id, wo.Values(DoubleValueKey.SalvageWorkmanship));
                            }
                        }
                    }

                    foreach (WorldObject wo in inv) {
                        if (!wo.HasIdData) {
                            continue;
                        }

                        if (wo.Values(LongValueKey.Material) <= 0) {
                            //Util.WriteToChat(Util.GetObjectName(wo.Id).ToString() + " ---- item is not loot gen");
                            continue;
                        }

                        if (wo.Values(LongValueKey.Imbued) >= 1) {
                            //Util.WriteToChat(Util.GetObjectName(wo.Id).ToString() + " ---- item is imbued");
                            continue;
                        }

                        if (wo.Values(LongValueKey.NumberTimesTinkered) >= 10) {
                            //Util.WriteToChat(Util.GetObjectName(wo.Id).ToString() + " ---- item is imbued");
                            continue;
                        }

                        if (wo.ObjectClass != ObjectClass.MissileWeapon && wo.ObjectClass != ObjectClass.MeleeWeapon && wo.ObjectClass != ObjectClass.WandStaffOrb) {
                            //Util.WriteToChat(Util.GetObjectName(wo.Id).ToString() + " ---- not the right weapon type");
                            continue;
                        }

                        if (wo.ObjectClass == ObjectClass.MissileWeapon && wo.Values(LongValueKey.DamageType) != dmgTypeID) {
                            continue;
                        }

                        if (wo.ObjectClass == ObjectClass.MeleeWeapon && wo.Values(LongValueKey.DamageType) != dmgTypeID) {
                            continue;
                        }

                        if (wo.ObjectClass == ObjectClass.WandStaffOrb && wo.Values(LongValueKey.WandElemDmgType) != dmgTypeID) {
                            continue;
                        }

                        if (skipWands && wo.ObjectClass == ObjectClass.WandStaffOrb) {
                            continue;
                        }


                        if (!PotentialWeaponList.ContainsKey(wo.Id)) {
                            PotentialWeaponList.Add(wo.Id, wo.Values(LongValueKey.Workmanship));
                        }

                        if (PotentialWeaponList.Count >= 1 && PotentialSalvageList.Count >= 1) {

                            imbueCount++;
                            targetItem = PotentialWeaponList.First().Key;
                            targetSalvage = PotentialSalvageList.First().Key;
                            imbueChance = tinkerCalc.DoCalc(targetSalvage, CoreManager.Current.WorldFilter[targetItem], CoreManager.Current.WorldFilter[targetItem].Values(LongValueKey.NumberTimesTinkered));
                            UpdateImbueList();
                            PotentialWeaponList.Remove(targetItem);
                            PotentialSalvageList.Remove(targetSalvage);
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void UpdateImbueList() {
            HudList.HudListRowAccessor newRow = AutoImbueList.AddRow();
            ((HudStaticText)newRow[0]).Text = imbueCount.ToString();
            ((HudStaticText)newRow[1]).Text = CoreManager.Current.WorldFilter[targetItem].Name.ToString();
            ((HudStaticText)newRow[2]).Text = Util.GetObjectName(targetSalvage) + " w" + Math.Round(CoreManager.Current.WorldFilter[targetSalvage].Values(DoubleValueKey.SalvageWorkmanship), 2, MidpointRounding.AwayFromZero).ToString("0.00");
            ((HudStaticText)newRow[3]).Text = imbueChance.ToString("P");
            ((HudStaticText)newRow[4]).Text = targetSalvage.ToString();
            ((HudStaticText)newRow[5]).Text = targetItem.ToString();
        }

        private void DoTinks() {
            try {
                HudList.HudListRowAccessor tinkerListRow = AutoImbueList[0];
                HudStaticText item = (HudStaticText)tinkerListRow[1];
                HudStaticText sal = (HudStaticText)tinkerListRow[2];
                HudStaticText salID = (HudStaticText)tinkerListRow[4];
                HudStaticText itemID = (HudStaticText)tinkerListRow[5];


                //Logger.Debug("AutoTinker: applying " + sal.Text.ToString() + ": " + salID.Text.ToString() + " to " + item.Text.ToString() + ": " + itemID.Text.ToString());
                if (!int.TryParse(itemID.Text.ToString(), out int intItemId)) {
                    Util.WriteToChat("AutoImbue: Something went wrong, unable to parse item to work with.");
                    return;
                }

                if (!int.TryParse(salID.Text.ToString(), out int intSalvId)) {
                    Util.WriteToChat("AutoImbue: Something went wrong, unable to parse salvage to work with.");
                    return;
                }

                if (intItemId != 0) {
                    currentItemName = Util.GetObjectName(intItemId);
                    currentItemID = intItemId;
                }

                if (intSalvId != 0) {
                    currentSalvageWK = Math.Round(CoreManager.Current.WorldFilter[intSalvId].Values(DoubleValueKey.SalvageWorkmanship), 2, MidpointRounding.AwayFromZero);
                    currentSalvage = Util.GetObjectName(intSalvId).Replace(" Salvage", "").Replace(" (100)", "");
                }

                if (!tinking) {
                    CoreManager.Current.Actions.SelectItem(intSalvId);
                    if (CoreManager.Current.Actions.CurrentSelection == intSalvId) {
                        //CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
                        UBHelper.ConfirmationRequest.ConfirmationRequestEvent += UBHelper_ConfirmationRequest;

                        //CoreManager.Current.WorldFilter.ReleaseObject += Current_ReleaseObject;
                        CoreManager.Current.Actions.ApplyItem(intSalvId, intItemId);
                        tinking = true;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        
        public void ClearAllTinks() {
            UBHelper.ConfirmationRequest.ConfirmationRequestEvent -= UBHelper_ConfirmationRequest;

            CoreManager.Current.Actions.RequestId(currentItemID);

            tinking = false;
            targetSalvage = 0;
            imbueChance = 0;
            targetItem = 0;
            imbueCount = 0;

            AutoImbueList.ClearRows();
            PotentialSalvageList.Clear();
            PotentialWeaponList.Clear();
        }

        private void UBHelper_ConfirmationRequest(object sender, UBHelper.ConfirmationRequest.ConfirmationRequestEventArgs e) {
            try {
                if (e.Confirm == 5) {
                    Util.WriteToChat($"AutoImbue: Clicking Yes on {e.Text}");
                    e.ClickYes = true;
                    UBHelper.ConfirmationRequest.ConfirmationRequestEvent -= UBHelper_ConfirmationRequest;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        protected override void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    UB.Core.RenderFrame -= Core_RenderFrame;
                    CoreManager.Current.ChatBoxMessage -= Current_ChatBoxMessage;
                    base.Dispose(disposing);
                }
                disposedValue = true;
            }
        }
    }
}
