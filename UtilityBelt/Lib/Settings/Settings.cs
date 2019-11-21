﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace UtilityBelt.Lib.Settings {
    public class OptionResult {
        public object Object;
        public object Parent;
        public PropertyInfo Property;
        public PropertyInfo ParentProperty;

        public OptionResult(object obj, PropertyInfo propertyInfo, object parent, PropertyInfo parentProperty) {
            Object = obj;
            Parent = parent;
            Property = propertyInfo;
            ParentProperty = parentProperty;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Settings {
        public bool ShouldSave = false;

        public event EventHandler Changed;

        private Dictionary<string, OptionResult> optionResultCache = new Dictionary<string, OptionResult>();

        #region Public Properties
        public bool HasCharacterSettingsLoaded { get; set; } = false;

        // path to global plugin config
        public string DefaultCharacterSettingsFilePath {
            get {
                return Path.Combine(Util.AssemblyDirectory, "settings.default.json");
            }
        }
        #endregion

        #region Settings Sections
        [JsonProperty]
        [Summary("Global plugin Settings")]
        public Sections.Plugin Plugin { get; set; }

        [JsonProperty]
        [Summary("AutoSalvage Settings")]
        public Sections.AutoSalvage AutoSalvage { get; set; }

        [JsonProperty]
        [Summary("AutoVendor Settings")]
        public Sections.AutoVendor AutoVendor { get; set; }

        [JsonProperty]
        [Summary("AutoTrade Settings")]
        public Sections.AutoTrade AutoTrade { get; set; }

        [JsonProperty]
        [Summary("Chat Logger Settings")]
        public Sections.ChatLogger ChatLogger { get; set; }

        [JsonProperty]
        [Summary("DungeonMaps Settings")]
        public Sections.DungeonMaps DungeonMaps { get; set; }

        [JsonProperty]
        [Summary("Equipment Manager Settings")]
        public Sections.EquipmentManager EquipmentManager { get; set; }

        [JsonProperty]
        [Summary("InventoryManager Settings")]
        public Sections.InventoryManager InventoryManager { get; set; }

        [JsonProperty]
        [Summary("Jumper Settings")]
        public Sections.Jumper Jumper { get; set; }

        [JsonProperty]
        [Summary("Nametags Settings")]
        public Sections.Nametags Nametags { get; set; }

        [JsonProperty]
        [Summary("VisualNav Settings")]
        public Sections.VisualNav VisualNav { get; set; }

        [JsonProperty]
        [Summary("VTank Integration Settings")]
        public Sections.VTank VTank { get; set; }
        #endregion

        public Settings() {
            try {
                SetupSection(Plugin = new Sections.Plugin(null));
                SetupSection(AutoSalvage = new Sections.AutoSalvage(null));
                SetupSection(AutoTrade = new Sections.AutoTrade(null));
                SetupSection(AutoVendor = new Sections.AutoVendor(null));
                SetupSection(ChatLogger = new Sections.ChatLogger(null));
                SetupSection(DungeonMaps = new Sections.DungeonMaps(null));
                SetupSection(EquipmentManager = new Sections.EquipmentManager(null));
                SetupSection(InventoryManager = new Sections.InventoryManager(null));
                SetupSection(Jumper = new Sections.Jumper(null));
                SetupSection(Nametags = new Sections.Nametags(null));
                SetupSection(VisualNav = new Sections.VisualNav(null));
                SetupSection(VTank = new Sections.VTank(null));

                Load();

                Logger.Debug("Finished loading settings");
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        internal OptionResult GetOptionProperty(string key) {
            try {
                if (optionResultCache.ContainsKey(key)) return optionResultCache[key];

                var parts = key.Split('.');
                object obj = Globals.Settings;
                PropertyInfo parentProp = null;
                PropertyInfo lastProp = null;
                object lastObj = obj;
                for (var i = 0; i < parts.Length; i++) {
                    if (obj == null) return null;

                    var found = false;
                    foreach (var prop in obj.GetType().GetProperties()) {
                        if (prop.Name.ToLower() == parts[i].ToLower()) {
                            parentProp = lastProp;
                            lastProp = prop;
                            lastObj = obj;
                            obj = prop.GetValue(obj, null);
                            found = true;
                            break;
                        }
                    }

                    if (!found) return null;
                }

                if (lastProp != null) {
                    var d = lastProp.GetCustomAttributes(typeof(DefaultValueAttribute), true);

                    if (d.Length > 0) {
                        optionResultCache[key] = new OptionResult(obj, lastProp, lastObj, parentProp);
                        return optionResultCache[key];
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return null;
        }

        internal object Get(string key) {
            try {
                var prop = GetOptionProperty(key);
                return prop.Property.GetValue(prop.Parent, null);
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return null;
        }

        public void EnableSaving() {
            ShouldSave = true;
        }

        public void DisableSaving() {
            ShouldSave = false;
        }

        // section setup / events
        private void SetupSection(SectionBase section) {
            section.PropertyChanged += HandleSectionChange;
        }

        #region Events / Handlers
        // notify any subcribers that this has changed
        protected void OnChanged() {
            Changed?.Invoke(this, new EventArgs());
        }

        // called when one of the child sections has been changed
        private void HandleSectionChange(object sender, EventArgs e) {
            OnChanged();
            Save();
        }
        #endregion

        #region Saving / Loading
        // load default plugin settings
        private void LoadDefaults() {
            try {
                if (File.Exists(DefaultCharacterSettingsFilePath)) {
                    JsonConvert.PopulateObject(File.ReadAllText(DefaultCharacterSettingsFilePath), this);
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        // load character specific settings
        public void Load() {
            try {
                var path = Path.Combine(Util.GetCharacterDirectory(), "settings.json");

                DisableSaving();

                LoadDefaults();
                LoadOldXML();

                if (File.Exists(path)) {
                    JsonConvert.PopulateObject(File.ReadAllText(path), this);
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            finally {
                // even if it fails to load... this is just for making sure
                // not to try to do stuff until we have settings loaded
                HasCharacterSettingsLoaded = true;
                EnableSaving();
            }
        }

        // save character specific settings
        public void Save(bool force=false) {
            try {
                if (!ShouldSave && !force) return;

                var json = JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
                var path = Path.Combine(Util.GetCharacterDirectory(), "settings.json");

                File.WriteAllText(path, json);
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        #endregion

        #region Old Mag-Tools style config.xml migration
        // load old mag-tools style xml config, this is just for migrating
        // it will be deleted afterwards
        private void LoadOldXML() {
            try {
                var path = Path.Combine(Util.GetCharacterDirectory(), "config.xml");
                if (!File.Exists(path)) return;
                Logger.Debug($"Loading old xml for migration: {path}");

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNode config = doc.DocumentElement.SelectSingleNode("/UtilityBelt/Config");

                
                foreach (XmlNode node in config.ChildNodes) {
                    switch (node.Name) {
                        case "AutoSalvage":
                            AutoSalvage.Think = ParseOldNode(node, "Think", AutoSalvage.Think);
                            AutoSalvage.OnlyFromMainPack = ParseOldNode(node, "OnlyFromMainPack", AutoSalvage.OnlyFromMainPack);
                            break;

                        case "AutoVendor":
                            AutoVendor.Enabled = ParseOldNode(node, "Enabled", AutoVendor.Enabled);
                            AutoVendor.Think = ParseOldNode(node, "Think", AutoVendor.Think);
                            AutoVendor.TestMode = ParseOldNode(node, "TestMode", AutoVendor.TestMode);
                            AutoVendor.ShowMerchantInfo = ParseOldNode(node, "ShowMerchantInfo", AutoVendor.ShowMerchantInfo);
                            AutoVendor.OnlyFromMainPack = ParseOldNode(node, "OnlyFromMainPack", AutoVendor.OnlyFromMainPack);
                            break;

                        case "InventoryManager":
                            InventoryManager.AutoCram = ParseOldNode(node, "AutoCram", InventoryManager.AutoCram);
                            InventoryManager.AutoStack = ParseOldNode(node, "AutoStack", InventoryManager.AutoStack);
                            break;
                            
                        case "VisualNav":
                            VisualNav.SaveNoneRoutes = ParseOldNode(node, "SaveNoneRoutes", VisualNav.SaveNoneRoutes);

                            VisualNav.Display.Lines.Color = ParseOldNode(node, "LineColor", VisualNav.Display.Lines.Color);
                            VisualNav.Display.ChatText.Color = ParseOldNode(node, "ChatTextColor", VisualNav.Display.ChatText.Color);
                            VisualNav.Display.JumpText.Color = ParseOldNode(node, "JumpTextColor", VisualNav.Display.JumpText.Color);
                            VisualNav.Display.JumpArrow.Color = ParseOldNode(node, "JumpArrowColor", VisualNav.Display.JumpArrow.Color);
                            VisualNav.Display.OpenVendor.Color = ParseOldNode(node, "OpenVendorColor", VisualNav.Display.OpenVendor.Color);
                            VisualNav.Display.Pause.Color = ParseOldNode(node, "PauseColor", VisualNav.Display.Pause.Color);
                            VisualNav.Display.Portal.Color = ParseOldNode(node, "PortalColor", VisualNav.Display.Portal.Color);
                            VisualNav.Display.Recall.Color = ParseOldNode(node, "RecallColor", VisualNav.Display.Recall.Color);
                            VisualNav.Display.UseNPC.Color = ParseOldNode(node, "UseNPCColor", VisualNav.Display.UseNPC.Color);
                            VisualNav.Display.FollowArrow.Color = ParseOldNode(node, "FollowArrowColor", VisualNav.Display.FollowArrow.Color);

                            VisualNav.Display.Lines.Enabled = ParseOldNode(node, "ShowLine", VisualNav.Display.Lines.Enabled);
                            VisualNav.Display.ChatText.Enabled = ParseOldNode(node, "ShowChatText", VisualNav.Display.ChatText.Enabled);
                            VisualNav.Display.JumpText.Enabled = ParseOldNode(node, "ShowJumpText", VisualNav.Display.JumpText.Enabled);
                            VisualNav.Display.JumpArrow.Enabled = ParseOldNode(node, "ShowJumpArrow", VisualNav.Display.JumpArrow.Enabled);
                            VisualNav.Display.OpenVendor.Enabled = ParseOldNode(node, "ShowOpenVendor", VisualNav.Display.OpenVendor.Enabled);
                            VisualNav.Display.Pause.Enabled = ParseOldNode(node, "ShowPause", VisualNav.Display.Pause.Enabled);
                            VisualNav.Display.Portal.Enabled = ParseOldNode(node, "ShowPortal", VisualNav.Display.Portal.Enabled);
                            VisualNav.Display.Recall.Enabled = ParseOldNode(node, "ShowRecall", VisualNav.Display.Recall.Enabled);
                            VisualNav.Display.UseNPC.Enabled = ParseOldNode(node, "ShowUseNPC", VisualNav.Display.UseNPC.Enabled);
                            VisualNav.Display.FollowArrow.Enabled = ParseOldNode(node, "ShowFollowArrow", VisualNav.Display.FollowArrow.Enabled);
                            break;
                            
                        case "Main":
                            Plugin.WindowPositionX = ParseOldNode(node, "WindowPositionX", Plugin.WindowPositionX);
                            Plugin.WindowPositionY = ParseOldNode(node, "WindowPositionY", Plugin.WindowPositionY);
                            break;
                    }
                }

                // force a save of our new settings
                Save(true);

                // if we successfully migrated, delete the old xml config
                if (File.Exists(Path.Combine(Util.GetCharacterDirectory(), "settings.json"))) {
                    Logger.Debug($"Deleting old xml after migration: {path}");
                    File.Delete(path);
                }
                else {
                    Logger.Error("Unable to migrate settings, something went wrong");
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private float ParseOldNode(XmlNode parentNode, string childTag, float defaultValue) {
            try {
                XmlNode node = parentNode.SelectSingleNode(childTag);
                if (node != null) {
                    float value = 0;

                    if (float.TryParse(node.InnerText, out value)) {
                        return value;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return defaultValue;
        }

        private int ParseOldNode(XmlNode parentNode, string childTag, int defaultValue) {
            try {
                XmlNode node = parentNode.SelectSingleNode($"{childTag}");
                if (node != null) {
                    int value = 0;

                    if (int.TryParse(node.InnerText, out value)) {
                        return value;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return defaultValue;
        }

        private bool ParseOldNode(XmlNode parentNode, string childTag, bool defaultValue) {
            try {
                XmlNode node = parentNode.SelectSingleNode($"{childTag}");
                if (node != null) {
                    return node.InnerText.ToLower().Trim() == "true";
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return defaultValue;
        }

        internal string DisplayValue(string key, bool expandLists=false, object value=null) {
            try {
                var prop = GetOptionProperty(key);
                value = value ?? Get(key);

                if (value.GetType().IsEnum) {
                    var supportsFlagsAttributes = prop.Property.GetCustomAttributes(typeof(SupportsFlagsAttribute), true);

                    if (supportsFlagsAttributes.Length > 0) {
                        return "0x" + ((uint)value).ToString("X8");
                    }
                    else {
                        return value.ToString();
                    }
                }
                else if (value.GetType() != typeof(string) && value.GetType().GetInterfaces().Contains(typeof(IEnumerable))) {
                    if (expandLists) {
                        var results = new List<string>();

                        foreach (var item in (IEnumerable)value) {
                            results.Add(DisplayValue(key, false, item));
                        }

                        return $"[{string.Join(",", results.ToArray())}]";
                    }
                    else {
                        return "[List]";
                    }
                }
                else if (prop.Property.Name.Contains("Color")) {
                    return "0x" + ((int)value).ToString("X8");
                }
                else {
                    return value.ToString();
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }

            return "null";
        }
        #endregion
    }
}
