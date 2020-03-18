﻿using Decal.Adapter.Wrappers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UBHelper;
using uTank2.LootPlugins;
using UtilityBelt.Lib;
using VTClassic;

namespace UtilityBelt.Tools {
    [Name("InventoryManager")]
    [Summary("Provides a command-line interface to inventory management.")]
    [FullDescription(@"
Provides a command-line interface to inventory management.

### Example Profiles
* [aaset.utl](/utl/aaset.utl)
  - Hands exactly one set of Ancient Armor
  - example: `/ub ig aaset.utl to Zero Cool`
* [Kreavon.utl](/utl/Kreavon.utl)
  - Hands all Gem type salvage in your inventory, for the [Town Founder](https://asheron.fandom.com/wiki/Town_Founder) Quest.
  - example: `/ub ig Kreavon.utl to Kreavon`
* [Caelis Renning.utl](/utl/Caelis Renning.utl)
  - Hands all metal/wood type salvage in your inventory, for the [Town Founder](https://asheron.fandom.com/wiki/Town_Founder) Quest.
  - example: `/ub ig Caelis Renning.utl to Caelis Renning`
* [Aun Teverea.utl](/utl/Aun Teverea.utl)
  - Hands all cloth type salvage in your inventory, for the [Town Founder](https://asheron.fandom.com/wiki/Town_Founder) Quest.
  - example: `ub ig Aun Teverea.utl to Aun Teverea`

    ")]
    public class InventoryManager : ToolBase {

        private bool disposed = false;

        private static readonly Dictionary<int, int> giveObjects = new Dictionary<int, int>();
        private List<int> idItems = new List<int>();
        private static DateTime lastIdSpam = DateTime.MinValue, bailTimer = DateTime.MinValue, reloadLootProfileTS = DateTime.MinValue, lastAction = DateTime.MinValue;
        public bool IGRunning = false;
        private static bool givePartialItem, isRegex = false;
        private static int currentItem, retryCount, destinationId, failedItems, totalFailures, maxGive, itemsGiven, lastIdCount, pendingGiveCount, giveDelay;
        private LootCore lootProfile = null;
        private static string utlProfile = "", targetPlayer="", profilePath = "";
        private static readonly Dictionary<string, int> poorlyNamedKeepUptoDictionary = new Dictionary<string, int>();
        private static readonly List<int> keptObjects = new List<int>();
        private Stopwatch giveTimer;
        private Assessor.Job assessor = null;
        private static FileSystemWatcher profilesWatcher = null;

        public event EventHandler Started;
        public event EventHandler Finished;

        #region Config
        [Summary("Automatically cram items into side packs")]
        [DefaultValue(false)]
        public bool AutoCram {
            get { return (bool)GetSetting("AutoCram"); }
            set { UpdateSetting("AutoCram", value); }
        }

        [Summary("Automatically combine stacked items")]
        [DefaultValue(false)]
        public bool AutoStack {
            get { return (bool)GetSetting("AutoStack"); }
            set { UpdateSetting("AutoStack", value); }
        }

        [Summary("Think to yourself when ItemGiver Finishes")]
        [DefaultValue(false)]
        public bool IGThink {
            get { return (bool)GetSetting("IGThink"); }
            set { UpdateSetting("IGThink", value); }
        }
        [Summary("Item Failure Count to fail ItemGiver")]
        [DefaultValue(3)]
        public int IGFailure {
            get { return (int)GetSetting("IGFailure"); }
            set { UpdateSetting("IGFailure", value); }
        }
        [Summary("Busy Count to fail ItemGiver give")]
        [DefaultValue(10)]
        public int IGBusyCount {
            get { return (int)GetSetting("IGBusyCount"); }
            set { UpdateSetting("IGBusyCount", value); }
        }
        [Summary("Maximum Range for ItemGiver commands")]
        [DefaultValue(15f)]
        public float IGRange {
            get { return (float)GetSetting("IGRange"); }
            set { UpdateSetting("IGRange", value); }
        }
        [Summary("Enable the ItemGiver UI window")]
        [DefaultValue(true)]
        public bool IGUIEnabled {
            get { return (bool)GetSetting("IGUIEnabled"); }
            set { UpdateSetting("IGUIEnabled", value); }
        }
        [Summary("ItemGiver window X position")]
        [DefaultValue(250)]
        public int IGWindowX {
            get { return (int)GetSetting("IGWindowX"); }
            set { UpdateSetting("IGWindowX", value); }
        }
        [Summary("ItemGiver window Y position")]
        [DefaultValue(250)]
        public int IGWindowY {
            get { return (int)GetSetting("IGWindowY"); }
            set { UpdateSetting("IGWindowY", value); }
        }
        [Summary("Treat stacks as single item")]
        [DefaultValue(true)]
        public bool TreatStackAsSingleItem {
            get { return (bool)GetSetting("TreatStackAsSingleItem"); }
            set { UpdateSetting("TreatStackAsSingleItem", value); }
        }
        [Summary("Watch VTank Loot Profile for changes, and reload")]
        [DefaultValue(false)]
        public bool WatchLootProfile {
            get { return (bool)GetSetting("WatchLootProfile"); }
            set {
                UpdateSetting("WatchLootProfile", value);
                WatchLootProfile_Changed(value);
            }
        }
        #endregion

        #region Commands
        #region /ub give
        [Summary("Gives items matching the provided name to a player.")]
        [Usage("/ub give[p{P|r}] [itemCount] <itemName> to <target>")]
        [Example("/ub givep 10 Prismatic to Zero Cool", "Gives 10 items partially matching the name \"Prismatic\" to Zero Cool")]
        [Example("/ub giveP 10 Prismatic Tapers to Zero", "Gives 10 Prismatic Tapers to a character with a name partially matching \"Zero\"")]
        [Example("/ub give Hero Token to Zero Cool", "Gives all Hero Tokens to Zero Cool")]
        [Example("/ub giver Hero.* to Zero Cool", "Gives all items matching the regex \"Hero.*\" to Zero Cool")]
        [CommandPattern("give", @"^ *((?<Count>\d+)? ?(?<Item>.+) to (?<Target>.+)|(?<StopCommand>stop|cancel|quit|abort))$", true)]
        public void DoGive(string command, Match args) {
            if (!string.IsNullOrEmpty(args.Groups["StopCommand"].Value)) {
                IGStop();
            }
            else {
                var partialMatchTarget = command.Contains('P');
                var partialMatchItem = command.Contains('P');
                var isRegex = command.Contains("r");
                int count = 0;
                if (!string.IsNullOrEmpty(args.Groups["Count"].Value))
                    Int32.TryParse(args.Groups["Count"].Value, out count);
                GiveItem(args.Groups["Target"].Value, partialMatchTarget, args.Groups["Item"].Value, partialMatchItem, isRegex, count, 0);
            }
        }

        public void GiveItem(string targetName, bool partialMatchTarget, string itemName, bool partialMatchItem, bool isRegex, int count=0, int delay=0 ) {
            if (IGRunning) {
                LogError("Already running.  Please wait until it completes or use /ub give stop to quit previous session");
                return;
            }
            var destination = Util.FindName(targetName, partialMatchTarget, new Decal.Adapter.Wrappers.ObjectClass[] { Decal.Adapter.Wrappers.ObjectClass.Player, Decal.Adapter.Wrappers.ObjectClass.Npc });

            if (destination == null) {
                LogError($"player {targetName} not found");
                return;
            }
            destinationId = destination.Id;
            InventoryManager.isRegex = isRegex;
            InventoryManager.givePartialItem = partialMatchItem;
            InventoryManager.giveDelay = delay;

            if (destinationId == UB.Core.CharacterFilter.Id) {
                LogError("You can't give to yourself");
                return;
            }

            var playerDistance = (float)UB.Core.WorldFilter.Distance(UB.Core.CharacterFilter.Id, destinationId) * 240;
            if (playerDistance > IGRange) {
                LogError($"{targetName} is {playerDistance:n2} meters away, IGRange is set to {IGRange}. bailing.");
                return;
            }
            maxGive = count;
            if (maxGive < 1)
                maxGive = int.MaxValue;

            if (isRegex)
                utlProfile = itemName; //NOT a profile name. just re-purposing this.
            else
                utlProfile = itemName.ToLower(); //NOT a profile name. just re-purposing this.


            LogDebug($"ItemGiver GIVE {(maxGive == int.MaxValue ? "∞" : maxGive.ToString())} {(givePartialItem ? "(partial)" : "")}{utlProfile} to {UB.Core.WorldFilter[destinationId].Name}");
            UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.Navigation, TimeSpan.FromMilliseconds(30000));
            UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.ItemUse, TimeSpan.FromMilliseconds(30000));
            IGRunning = true;
            GetGiveItems();

            lastIdCount = int.MaxValue;
            bailTimer = DateTime.UtcNow;
            giveTimer = Stopwatch.StartNew();
            UB.Core.RenderFrame += Core_RenderFrame_ig;

            Started?.Invoke(this, new EventArgs());
        }

        private void GetGiveItems() {
            try {
                Regex itemre = new Regex(utlProfile);

                List<int> inv = new List<int>();
                UBHelper.InventoryManager.GetInventory(ref inv, UBHelper.InventoryManager.GetInventoryType.AllItems);
                foreach (int item in inv) {
                    if (pendingGiveCount >= maxGive) {
                        Logger.Debug($"Max give ({maxGive}) reached, breaking");
                        break;
                    }

                    if (isRegex) {
                        if (!itemre.IsMatch(Util.GetObjectName(item))) continue;
                    } else if (givePartialItem) {
                        if (!Util.GetObjectName(item).ToLower().Contains(utlProfile)) continue;
                    } else {
                        if (!Util.GetObjectName(item).ToLower().Equals(utlProfile)) continue;
                    }

                    if (TreatStackAsSingleItem) {
                        giveObjects.Add(item, 0);
                        pendingGiveCount++;
                    }
                    else {
                        UBHelper.Weenie w = new UBHelper.Weenie(item);
                        var stackCount = w.StackCount;
                        if (stackCount > maxGive - pendingGiveCount) {
                            giveObjects.Add(item, maxGive - pendingGiveCount);
                            pendingGiveCount = maxGive;
                        }
                        else {
                            giveObjects.Add(item, 0);
                            pendingGiveCount += stackCount == 0 ? 1 : stackCount;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        #endregion
        #region /ub ig
        [Summary("Gives items matching the provided loot profile to a player.")]
        [Usage("/ub ig[p] <lootProfile> to <target>")]
        [Example("/ub ig muledItems.utl to Zero Cool", "Gives all items matching Keep rules in muledItems.utl to Zero Cool")]
        [Example("/ub igp muledItems.utl to Zero", "Gives all items matching Keep rules in muledItems.utl to a character partially matching the name Zero")]
        [CommandPattern("ig", @"^ *(?<utlProfile>.+) to (?<Target>.+)|(?<StopCommand>(cancel|stop|abort|quit))$", true)]
        public void DoItemGiver(string command, Match args) {
            if (!string.IsNullOrEmpty(args.Groups["StopCommand"].Value)) {
                IGStop();
            }
            else {
                UB_ig_Start(args.Groups["Target"].Value, command.Contains("p"), args.Groups["utlProfile"].Value);
            }
        }

        public void UB_ig_Start(string target, bool partialTargetMatch, string utlProfile, int delay=0) {
            if (IGRunning) {
                LogError("Already running.  Please wait until it completes or use /ub ig stop to quit previous session");
                return;
            }

            targetPlayer = target;

            var destination = Util.FindName(targetPlayer, partialTargetMatch, new Decal.Adapter.Wrappers.ObjectClass[] { Decal.Adapter.Wrappers.ObjectClass.Player, Decal.Adapter.Wrappers.ObjectClass.Npc });

            if (destination == null) {
                LogError($"player {targetPlayer} not found");
                return;
            }

            if (destination.Id == UB.Core.CharacterFilter.Id) {
                LogError("You can't give to yourself");
                return;
            }

            var playerDistance = UB.Core.WorldFilter.Distance(UB.Core.CharacterFilter.Id, destination.Id) * 240;
            if (playerDistance > IGRange) {
                LogError($"ItemGiver {targetPlayer} is {playerDistance:n2} meters away. IGRange is set to {IGRange}");
                return;
            }

            if (!File.Exists(Path.Combine(profilePath, utlProfile))) {
                if (File.Exists(Path.Combine(profilePath, utlProfile + ".utl"))) {
                    utlProfile += ".utl";
                }
                else {
                    LogError($"ItemGiver Profile does not exist: {utlProfile} ({profilePath})");
                    return;
                }
            }

            if (lootProfile == null) {
                var hasLootCore = false;
                try {
                    lootProfile = new LootCore();
                    hasLootCore = true;
                }
                catch (Exception ex) { Logger.LogException(ex); }

                if (!hasLootCore) {
                    LogError("ItemGiver unable to load VTClassic, something went wrong.");
                    return;
                }
            }

            lootProfile.LoadProfile(Path.Combine(profilePath, utlProfile), false);

            UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.Navigation, TimeSpan.FromMilliseconds(30000));
            UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.ItemUse, TimeSpan.FromMilliseconds(30000));
            lastIdCount = int.MaxValue;
            IGRunning = true;
            GetIGItems();

            bailTimer = DateTime.UtcNow;
            giveTimer = Stopwatch.StartNew();
            destinationId = destination.Id;
            InventoryManager.utlProfile = utlProfile;
            giveDelay = delay;
            UB.Core.RenderFrame += Core_RenderFrame_ig;

            Started?.Invoke(this, new EventArgs());
        }

        private void GetIGItems() {
            idItems.Clear();
            try {
                List<int> inv = new List<int>();
                UBHelper.InventoryManager.GetInventory(ref inv, UBHelper.InventoryManager.GetInventoryType.AllItems);
                foreach (int item in inv) {
                    if (giveObjects.ContainsKey(item)) // already in the list
                        continue;

                    if (keptObjects.Contains(item)) // item was already given (partial stack)
                        continue;
                    if (UB.Core.WorldFilter[item] == null) // Decal's World Filter doesn't know about this item
                        continue;

                    GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(item);
                    if (itemInfo == null) // This happens all the time for aetheria that has been converted
                        continue;

                    if (!UB.Core.WorldFilter[item].HasIdData && lootProfile.DoesPotentialItemNeedID(itemInfo)) {
                        idItems.Add(item);
                        continue;
                    }

                    TestIGItem(item);
                }

                assessor = new Assessor.Job(UB.Assessor, ref idItems, TestIGItem, () => { assessor = null; }, false);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void TestIGItem(int item) {
            bailTimer = DateTime.UtcNow;
            GameItemInfo itemInfo = uTank2.PluginCore.PC.FWorldTracker_GetWithID(item);
            LootAction result = lootProfile.GetLootDecision(itemInfo);
            if (!result.IsKeep && !result.IsKeepUpTo) {
                return;
            }

            if (result.IsKeepUpTo) {
                if (!poorlyNamedKeepUptoDictionary.ContainsKey(result.RuleName)) {
                    poorlyNamedKeepUptoDictionary.Add(result.RuleName, 0);
                }

                var stackCount = TreatStackAsSingleItem ? 1 : UB.Core.WorldFilter[item].Values(LongValueKey.StackCount, 1);
                if (result.Data1 < 0) { // Keep this many
                                        // Keep matches until we have kept the KeepUpTo #
                    int alreadyKept = poorlyNamedKeepUptoDictionary[result.RuleName];
                    int totalNumberToKeep = Math.Abs(result.Data1);
                    if (totalNumberToKeep >= alreadyKept) {
                        if (!TreatStackAsSingleItem && stackCount > totalNumberToKeep - alreadyKept) {
                            int splitCount = stackCount - (totalNumberToKeep - alreadyKept);
                            giveObjects.Add(item, splitCount);
                            poorlyNamedKeepUptoDictionary[result.RuleName] += totalNumberToKeep - alreadyKept;
                        }
                        else {
                            poorlyNamedKeepUptoDictionary[result.RuleName] += stackCount;
                        }

                        return;
                    }
                }
                else { // Give this many
                       // Keep if already given KeepUpTo #
                    int alreadyGiven = poorlyNamedKeepUptoDictionary[result.RuleName];
                    int numberToGive = result.Data1;
                    if (alreadyGiven >= numberToGive) return;
                    if (!TreatStackAsSingleItem && stackCount > numberToGive - alreadyGiven) {
                        int neededCount = numberToGive - alreadyGiven;
                        giveObjects.Add(item, neededCount);
                        poorlyNamedKeepUptoDictionary[result.RuleName] += neededCount;

                        return;
                    }
                    else
                        poorlyNamedKeepUptoDictionary[result.RuleName] += stackCount;
                }
            }

            giveObjects.Add(item, 0);
        }
        #endregion
        #region /ub autostack
        [Summary("Auto Stack your inventory")]
        [Usage("/ub autostack")]
        [CommandPattern("autostack", @"^$")]
        public void DoAutoStack(string _, Match _1) {
            if (UBHelper.InventoryManager.AutoStack()) {
                UBHelper.ActionQueue.InventoryEvent += ActionQueue_InventoryEvent_AutoStack;
                Util.WriteToChat("AutoStack running");
            }
            else {
                Util.WriteToChat("AutoStack - nothing to do");
            }
        }

        private void ActionQueue_InventoryEvent_AutoStack(object sender, EventArgs e) {
            try {
                Util.WriteToChat("AutoStack complete.");
                UBHelper.ActionQueue.InventoryEvent -= ActionQueue_InventoryEvent_AutoStack;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        #endregion
        #region /ub autocram
        [Summary("Auto Cram into side packs")]
        [Usage("/ub autocram")]
        [CommandPattern("autocram", @"^$")]
        public void DoAutoCram(string _, Match _1) {
            if (UBHelper.InventoryManager.AutoCram()) {
                UBHelper.ActionQueue.InventoryEvent += ActionQueue_InventoryEvent_AutoCram;
                Util.WriteToChat("AutoCram running");
            }
            else {
                Util.WriteToChat("AutoCram - nothing to do");
            }
        }

        private void ActionQueue_InventoryEvent_AutoCram(object sender, EventArgs e) {
            try {
                Util.WriteToChat("AutoCram complete.");
                UBHelper.ActionQueue.InventoryEvent -= ActionQueue_InventoryEvent_AutoCram;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        #endregion

        #region /ub clearbugged
        [Summary("ID everything in your inventory, and remove bugged items")]
        [Usage("/ub clearbugged")]
        [CommandPattern("clearbugged", @"^$")]
        public void DoClearBugged(string _, Match _1) {
            List<int> inv = new List<int>();
            UBHelper.InventoryManager.GetInventory(ref inv, UBHelper.InventoryManager.GetInventoryType.AllItems, UBHelper.Weenie.INVENTORY_LOC.ALL_LOC);
            WriteToChat($"ClearBugged: Identifying {inv.Count} items, to check for bugged items...");
            new Assessor.Job(UB.Assessor, ref inv, null, () => { WriteToChat($"ClearBugged: Done!"); }, false);
        }
        #endregion
        #endregion

        #region Expressions
        #region getitemcountininventorybyname[string name]
        [ExpressionMethod("getitemcountininventorybyname")]
        [ExpressionParameter(0, typeof(string), "name", "Exact item name to match")]
        [ExpressionReturn(typeof(double), "Returns a count of the number of items found. stack size is counted")]
        [Summary("Counts how many items you have in your inventory exactly matching `name`. Stack sizes are counted")]
        [Example("getitemcountininventorybyname[Prismatic Taper]", "Returns total count of prismatic tapers in your inventory")]
        public object Getitemcountininventorybyname(string name) {
            double count = 0;
            List<int> weenies = new List<int>();
            UBHelper.InventoryManager.GetInventory(ref weenies, UBHelper.InventoryManager.GetInventoryType.AllItems);

            foreach (var id in weenies) {
                var weenie = new Weenie(id);
                if (weenie.Name.ToLower().Equals(name.ToLower()))
                    count += (weenie.StackCount >= 1) ? weenie.StackCount : 1;
            }

            return count;
        }
        #endregion //getitemcountininventorybyname[string namerx]
        #region getitemcountininventorybynamerx[string name]
        [ExpressionMethod("getitemcountininventorybynamerx")]
        [ExpressionParameter(0, typeof(string), "namerx", "Regex item name to match")]
        [ExpressionReturn(typeof(double), "Returns a count of the number of items found. stack size is counted")]
        [Summary("Counts how many items you have in your inventory matching regex `namerx`. Stack sizes are counted")]
        [Example("getitemcountininventorybynamerx[Scarab]", "Returns total count of scarabs in your inventory")]
        public object Getitemcountininventorybynamerx(string namerx) {
            Regex re = new Regex(namerx, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            double count = 0;
            List<int> weenies = new List<int>();
            UBHelper.InventoryManager.GetInventory(ref weenies, UBHelper.InventoryManager.GetInventoryType.AllItems);
            foreach (var id in weenies) {
                var weenie = new Weenie(id);
                if (re.IsMatch(weenie.Name)) {
                    count += (weenie.StackCount >= 1) ? weenie.StackCount : 1;
                }
            }

            return count;
        }
        #endregion //getitemcountininventorybynamerx[string name]
        #region actiontrygiveprofile[string lootprofile, string target]
        [ExpressionMethod("actiontrygiveprofile")]
        [ExpressionParameter(0, typeof(string), "lootprofile", "loot profile to give")]
        [ExpressionParameter(0, typeof(string), "target", "target npc/player name to give to")]
        [ExpressionReturn(typeof(double), "Returns 1 if an attempt was made, 0 if it was not (busy)")]
        [Summary("Gives all items matching the specified loot profile to a character or npc")]
        [Example(@"actiontrygiveprofile[salvage\.utl,My Salvage Mule]", "Gives all items matching loot profile salvage.utl to a character named My Salvage Mule")]
        public object Actiontrygiveprofile(string lootprofile, string target) {
            if (IGRunning) {
                LogError("Already running.  Please wait until it completes or use /ub ig stop to quit previous session");
                return 0;
            }

            var destination = Util.FindName(target, false, new Decal.Adapter.Wrappers.ObjectClass[] { Decal.Adapter.Wrappers.ObjectClass.Player, Decal.Adapter.Wrappers.ObjectClass.Npc });

            if (destination == null) {
                LogError($"actiontrygiveprofile[] {target} not found");
                return 0;
            }

            UB_ig_Start(destination.Name, false, lootprofile);
            return 1;
        }
        #endregion //actiontrygiveprofile[string lootprofile, string target]
        #endregion //Expressions

        // TODO: support AutoPack profiles when cramming
        public InventoryManager(UtilityBeltPlugin ub, string name) : base(ub, name) {
            profilePath = Path.Combine(Util.GetPluginDirectory(), "itemgiver");
            Directory.CreateDirectory(profilePath);

            UB.Core.WorldFilter.ChangeObject += WorldFilter_ChangeObject;
        }

        public override void Init() {
            base.Init();

            if (UB.Core.CharacterFilter.LoginStatus != 0) {
                WatchLootProfile_Changed(WatchLootProfile);
            }
            else {
                UB.Core.CharacterFilter.LoginComplete += CharacterFilter_LoginComplete;
            }
        }

        private void CharacterFilter_LoginComplete(object sender, EventArgs e) {
            try {
                UB.Core.CharacterFilter.LoginComplete -= CharacterFilter_LoginComplete;
                WatchLootProfile_Changed(WatchLootProfile);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        #region Loot Profile Watcher
        private bool PC_LootProfileChanged_Registered = false;
        // Handle settings changes while running
        public void WatchLootProfile_Changed(bool enabled) {
            if (UB.Core.CharacterFilter.LoginStatus == 0)
                return;

            if (profilesWatcher != null)
                profilesWatcher.Dispose();
            if (enabled) {
                if (!PC_LootProfileChanged_Registered) {
                    uTank2.PluginCore.PC.LootProfileChanged += PC_LootProfileChanged;
                    PC_LootProfileChanged_Registered = true;
                }
                string profilePath = Util.GetVTankProfilesDirectory();
                if (!Directory.Exists(profilePath)) {
                    LogError($"WatchLootProfile_Changed(true) Error: {profilePath} does not exist!");
                    WatchLootProfile = false;
                    return;
                }
                string loadedProfile = UBHelper.vTank.Instance?.GetLootProfile();
                if (string.IsNullOrEmpty(loadedProfile)) return;

                profilesWatcher = new FileSystemWatcher {
                    NotifyFilter = NotifyFilters.LastWrite,
                    Filter = loadedProfile,
                    Path = profilePath,
                    EnableRaisingEvents = true
                };
                profilesWatcher.Changed += LootProfile_Changed;
            } else {
                if (PC_LootProfileChanged_Registered) {
                    uTank2.PluginCore.PC.LootProfileChanged -= PC_LootProfileChanged;
                    PC_LootProfileChanged_Registered = false;
                }
            }
        }

        private void PC_LootProfileChanged() {
            try {
                string loadedProfile = UBHelper.vTank.Instance?.GetLootProfile();
                if (!string.IsNullOrEmpty(loadedProfile))
                    WatchLootProfile_Changed(WatchLootProfile);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void LootProfile_Changed(object sender, FileSystemEventArgs e) {
            UB.Core.RenderFrame += Core_RenderFrame_ReloadProfile;
            reloadLootProfileTS = DateTime.UtcNow;
        }
        private void Core_RenderFrame_ReloadProfile(object sender, EventArgs e) {
            try {
                if (DateTime.UtcNow - reloadLootProfileTS > TimeSpan.FromSeconds(2)) {
                    if (profilesWatcher != null && !string.IsNullOrEmpty(profilesWatcher.Filter)) {
                        LogDebug($"/vt loot load {profilesWatcher.Filter}");
                        Util.Decal_DispatchOnChatCommand($"/vt loot load {profilesWatcher.Filter}");
                        UB.Core.RenderFrame -= Core_RenderFrame_ReloadProfile;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        #endregion
        private void WorldFilter_ChangeObject(object sender, ChangeObjectEventArgs e) {
            try{
                if (IGRunning && e.Changed.Id == currentItem && (e.Change == WorldChangeType.SizeChange || e.Changed.Container == -1)) {
                    // Partial stack give - keep the rest
                    if (e.Change == WorldChangeType.SizeChange)
                        keptObjects.Add(e.Changed.Id);

                    giveObjects.Remove(e.Changed.Id);
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }


        //TODO: Split this up, and disable when not in use
        public void Core_RenderFrame_ig(object sender, EventArgs e) {
            try {
                if (!IGRunning) {
                    UB.Core.RenderFrame -= Core_RenderFrame_ig;
                    LogError("InventoryManager: Core_RenderFrame_ig called with igRunning==false");
                    return;
                }

                if (UBHelper.vTank.locks[uTank2.ActionLockType.Navigation] < DateTime.UtcNow + TimeSpan.FromSeconds(1)) { //if itemgiver is running, and nav block has less than a second remaining, refresh it
                    UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.Navigation, TimeSpan.FromMilliseconds(30000));
                    UBHelper.vTank.Decision_Lock(uTank2.ActionLockType.ItemUse, TimeSpan.FromMilliseconds(30000));
                }

                if (DateTime.UtcNow - lastAction < TimeSpan.FromMilliseconds(giveDelay))
                    return;

                if (UB.Core.Actions.BusyState == 0) {
                    if (UB.Core.WorldFilter[destinationId] == null) {
                        LogDebug($"ItemGiver {targetPlayer} vanished!");
                        IGStop();
                        return;
                    }

                    var invalidItems = giveObjects.Where(x => (UB.Core.WorldFilter[x.Key] == null) || (UB.Core.WorldFilter[x.Key].Container == -1)).ToArray();
                    foreach (var item in invalidItems)
                    {
                        itemsGiven++;
                        giveObjects.Remove(item.Key);
                    }
                    if (giveObjects.Count > 0) {
                        KeyValuePair<int, int> item = giveObjects.First();
                        if (item.Key != currentItem) {
                            retryCount = 0;
                            bailTimer = DateTime.UtcNow;
                        }
                        currentItem = item.Key;

                        retryCount++;
                        totalFailures++;
                        // Logger.Debug($"Attempting to give {Util.GetObjectName(item.Key)} <{item.Key}> * {item.Value}");
                        if (item.Value > 0)
                        {
                            UB.Core.Actions.SelectItem(item.Key);
                            UB.Core.Actions.SelectedStackCount = item.Value;
                        }
                        UB.Core.Actions.GiveItem(item.Key, destinationId);
                        if (retryCount > IGBusyCount) {
                            giveObjects.Remove(item.Key);
                            LogDebug($"unable to give {Util.GetObjectName(item.Key)}");
                            failedItems++;
                        }

                        lastAction = DateTime.UtcNow;

                        if (failedItems > IGFailure) IGStop();

                        return;
                    }

                    if (giveObjects.Count == 0 && (assessor == null || assessor.ids == null || assessor.ids.Count == 0)) {
                        IGStop();
                    }
                }
                if (DateTime.UtcNow - bailTimer > TimeSpan.FromSeconds(10)) {
                    LogError("ItemGiver bail, Timeout expired");
                    IGStop();
                }
            } catch (Exception ex) { Logger.LogException(ex); }
        }

        public void IGStop() {
            if (!IGRunning) {
                Util.WriteToChat("ItemGiver is not running.");
                return;
            }

            Util.ThinkOrWrite($"ItemGiver finished: {utlProfile} to {targetPlayer}. took {Util.GetFriendlyTimeDifference(giveTimer.Elapsed)} to give {itemsGiven} item(s). {totalFailures - itemsGiven}", IGThink);
            
            UB.Core.RenderFrame -= Core_RenderFrame_ig;
            UBHelper.vTank.Decision_UnLock(uTank2.ActionLockType.Navigation);
            UBHelper.vTank.Decision_UnLock(uTank2.ActionLockType.ItemUse);
            itemsGiven = totalFailures = failedItems = pendingGiveCount = 0;
            IGRunning = isRegex = false;
            lootProfile = null;
            poorlyNamedKeepUptoDictionary.Clear();
            giveObjects.Clear();
            keptObjects.Clear();
            assessor = null;
            Finished?.Invoke(this, new EventArgs());
        }

        protected override void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    if (UB.Core != null) {
                        if (UB.Core.WorldFilter != null) {
                            UB.Core.WorldFilter.ChangeObject -= WorldFilter_ChangeObject;
                        }
                    }
                    if (profilesWatcher != null) {
                        profilesWatcher.Dispose();
                        if (uTank2.PluginCore.PC != null) {
                            uTank2.PluginCore.PC.LootProfileChanged -= PC_LootProfileChanged;
                        }
                    }

                    UB.Core.RenderFrame -= Core_RenderFrame_ReloadProfile;
                    UB.Core.RenderFrame -= Core_RenderFrame_ig;
                    base.Dispose(disposing);
                }
                disposed = true;
            }
        }
    }
}
