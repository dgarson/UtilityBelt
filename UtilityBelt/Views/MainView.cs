﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UtilityBelt.Lib;
using UtilityBelt.Lib.Settings;
using VirindiViewService;
using VirindiViewService.Controls;
using VirindiViewService.XMLParsers;

namespace UtilityBelt.Views {
    public class MainView : BaseView {

        private HudList SettingsList;
        private HudFixedLayout SettingEditLayout;
        private int selectedIndex = -1;
        private SettingsForm currentForm = null;
        private HudStaticText SummaryText = null;
        private HudFixedLayout FormLayout = null;
        private HudButton CheckForUpdate;

        private List<HudButton> toggleButtons = new List<HudButton>();

        private const int descriptionHeight = 40;

        private readonly Dictionary<string, string> buttons = new Dictionary<string, string>() {
                    { "AutoVendorEnable", "AutoVendor.Enabled" },
                    { "AutoVendorTestMode", "AutoVendor.TestMode" },
                    { "AutoTradeEnable", "AutoTrade.Enabled" },
                    { "AutoTradeTestMode", "AutoTrade.TestMode" },
                    { "DungeonMapsEnabled", "DungeonMaps.Enabled" },
                    { "VideoPatchEnabled", "Plugin.VideoPatch" },
                    { "VisualNavEnabled", "VisualNav.Enabled" },
                    { "VitalSharingEnabled", "VTank.VitalSharing" },
                    { "Debug", "Plugin.Debug" }
                };

        public MainView() {
            try {
                CreateFromXMLResource("UtilityBelt.Views.MainView.xml");

                view.Location = new Point(
                    Globals.Settings.Plugin.WindowPositionX,
                    Globals.Settings.Plugin.WindowPositionY
                );

                var timer = new Timer();
                timer.Interval = 2000; // save the window position 2 seconds after it has stopped moving
                timer.Tick += (s, e) => {
                    timer.Stop();
                    Globals.Settings.Plugin.WindowPositionX = view.Location.X;
                    Globals.Settings.Plugin.WindowPositionY = view.Location.Y;
                };

                view.Moved += (s, e) => {
                    if (timer.Enabled) timer.Stop();
                    timer.Start();
                };

                SettingsList = (HudList)view["SettingsList"];
                SettingEditLayout = (HudFixedLayout)view["SettingsForm"];
                CheckForUpdate = (HudButton)view["CheckForUpdate"];

                SettingsList.Click += SettingsList_Click;
                CheckForUpdate.Hit += CheckForUpdate_Hit;
                Globals.Settings.Changed += Settings_Changed;

                foreach (var kv in buttons) {
                    UpdateButton(kv);
                    var hudButton = (HudButton)view[kv.Key];
                    hudButton.Hit += (s, e) => {
                        try {
                            var prop = Globals.Settings.GetOptionProperty(kv.Value);
                            prop.Property.SetValue(prop.Parent, !(bool)Globals.Settings.Get(kv.Value), null);
                            if (!Globals.Settings.Plugin.Debug) {
                                Util.WriteToChat($"{kv.Value} = {Globals.Settings.DisplayValue(kv.Value)}");
                            }
                        }
                        catch (Exception ex) { Logger.LogException(ex); }
                    };
                }

                PopulateSettings(Globals.Settings, "");
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void CheckForUpdate_Hit(object sender, EventArgs e) {
            UpdateChecker.CheckForUpdate();
        }

        private void Settings_Changed(object sender, EventArgs e) {
            foreach (var kv in buttons) {
                UpdateButton(kv);
            }
        }

        private void UpdateButton(KeyValuePair<string, string> kv) {
            var hudButton = (HudButton)view[kv.Key];
            hudButton.OverlayImageRectangle = new Rectangle(3, 4, 16, 16);
            if ((bool)Globals.Settings.Get(kv.Value)) {
                hudButton.OverlayImage = 0x060069A1;
            }
            else {
                hudButton.OverlayImage = 0x060069FA;
            }
        }

        private void SettingsList_Click(object sender, int rowIndex, int colIndex) {
            try {
                var row = ((HudList.HudListRowAccessor)SettingsList[rowIndex]);
                var prop = Globals.Settings.GetOptionProperty(((HudStaticText)row[0]).Text);

                if (selectedIndex >= 0 && SettingsList.RowCount > selectedIndex) {
                    ((HudStaticText)((HudList.HudListRowAccessor)SettingsList[selectedIndex])[0]).TextColor = view.Theme.GetColor("ListText");
                }

                ((HudStaticText)row[0]).TextColor = Color.Red;
                selectedIndex = rowIndex;

                DrawSetting(((HudStaticText)row[0]).Text);

                if (colIndex == 1 && prop.Object.GetType() == typeof(bool)) {
                    prop.Property.SetValue(prop.Parent, !(bool)prop.Property.GetValue(prop.Parent, null), null);
                    DrawSetting(((HudStaticText)row[0]).Text);
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void DrawSetting(string setting) {
            if (currentForm != null) {
                currentForm.Dispose();
                currentForm = null;
            }
            if (SummaryText == null) {
                SummaryText = new HudStaticText();

                SettingEditLayout.AddControl(SummaryText, new Rectangle(5, 5, 390, descriptionHeight));
            }
            if (FormLayout == null) {
                FormLayout = new HudFixedLayout();
                SettingEditLayout.AddControl(FormLayout, new Rectangle(5, descriptionHeight, 390, 25));
            }
            
            var prop = Globals.Settings.GetOptionProperty(setting);
            SummaryText.TextAlignment = WriteTextFormats.WordBreak;

            var d = prop.Property.GetCustomAttributes(typeof(SummaryAttribute), true);
            if (d.Length == 1) {
                SummaryText.Text = ((SummaryAttribute)d[0]).Text;
            }
            else {
                // no summary attribute, so we use the parent's
                d = prop.ParentProperty.GetCustomAttributes(typeof(SummaryAttribute), true);
                if (d.Length == 1) {
                    SummaryText.Text = ((SummaryAttribute)d[0]).Text + " " + prop.Property.Name;
                }
                else {
                    SummaryText.Text = prop.Property.Name;
                }
            }

            SummaryText.Text += " (" + prop.Object.GetType() + ")";

            currentForm = new SettingsForm(setting, FormLayout);
            currentForm.Changed += (s, e) => {
                prop.Property.SetValue(prop.Parent, currentForm.Value, null);
            };
        }

        private void PopulateSettings(object obj, string history) {
            obj = obj ?? Globals.Settings;

            var props = obj.GetType().GetProperties();

            if (obj != Globals.Settings) {
                ((SectionBase)obj).PropertyChanged += (object sender, PropertyChangedEventArgs e) => {
                    var fullName = history + e.PropertyName;

                    for (var i = 0; i < SettingsList.RowCount; i++) {
                        var row = ((HudList.HudListRowAccessor)SettingsList[i]);
                        if (((HudStaticText)row[0]).Text == fullName) {
                            ((HudStaticText)row[1]).Text = Globals.Settings.DisplayValue(fullName);
                            break;
                        }
                    }
                };
            }

            foreach (var prop in props) {
                var summaryAttributes = prop.GetCustomAttributes(typeof(SummaryAttribute), true);
                var defaultValueAttributes = prop.GetCustomAttributes(typeof(DefaultValueAttribute), true);

                if (defaultValueAttributes.Length > 0) {
                    var row = SettingsList.AddRow();
                    ((HudStaticText)row[0]).Text = history + prop.Name;
                    ((HudStaticText)row[1]).Text = Globals.Settings.DisplayValue(history + prop.Name);
                    ((HudStaticText)row[1]).TextAlignment = WriteTextFormats.Right;
                }
                else if (summaryAttributes.Length > 0) {
                    PopulateSettings(prop.GetValue(obj, null), $"{history}{prop.Name}.");
                }
            }
        }

        private void DoUpdateCheck_Hit(object sender, EventArgs e) {
            try {
                Util.WriteToChat("Checking for update");
                UpdateChecker.CheckForUpdate();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        internal override ACImage GetIcon() {
            return GetIcon("UtilityBelt.Resources.icons.utilitybelt.png");
        }
    }
}