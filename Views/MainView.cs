﻿using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using UtilityBelt.Lib;
using VirindiViewService;
using VirindiViewService.Controls;
using VirindiViewService.XMLParsers;

namespace UtilityBelt.Views {
    public class MainView : IDisposable {
        public readonly VirindiViewService.HudView view;

        private ViewProperties properties;
        private ControlGroup controls;

        private HudCheckBox PluginDebug;
        private HudCheckBox PluginCheckForUpdates;
        private HudButton DoUpdateCheck;

        private bool disposed;

        public MainView() {
            try {
                new Decal3XMLParser().ParseFromResource("UtilityBelt.Views.MainView.xml", out properties, out controls);

                properties.Icon = GetIcon();
                properties.Title = string.Format("{0} - v{1}", Globals.PluginName, Util.GetVersion());

                view = new VirindiViewService.HudView(properties, controls);

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

                PluginDebug = (HudCheckBox)view["PluginDebug"];
                PluginCheckForUpdates = (HudCheckBox)view["PluginCheckForUpdates"];
                DoUpdateCheck = (HudButton)view["DoUpdateCheck"];

                DoUpdateCheck.Hit += DoUpdateCheck_Hit;

                PluginDebug.Change += PluginDebug_Change;
                PluginCheckForUpdates.Change += PluginCheckForUpdates_Change;

                UpdateUI();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void PluginCheckForUpdates_Change(object sender, EventArgs e) {
            try {
                Globals.Settings.Plugin.CheckForUpdates = PluginCheckForUpdates.Checked;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void PluginDebug_Change(object sender, EventArgs e) {
            try {
                Globals.Settings.Plugin.Debug = PluginDebug.Checked;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void DoUpdateCheck_Hit(object sender, EventArgs e) {
            try {
                Util.WriteToChat("Checking for update");
                UpdateChecker.CheckForUpdate();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void UpdateUI() {
            PluginDebug.Checked = Globals.Settings.Plugin.Debug;
            PluginCheckForUpdates.Checked = Globals.Settings.Plugin.CheckForUpdates;
        }

        public ACImage GetIcon() {
            ACImage acImage = null;

            try {
                using (Stream manifestResourceStream = typeof(MainView).Assembly.GetManifestResourceStream("UtilityBelt.Resources.icons.utilitybelt.png")) {
                    if (manifestResourceStream != null) {
                        using (Bitmap bitmap = new Bitmap(manifestResourceStream))
                            acImage = new ACImage(bitmap);
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
            return acImage;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    if (view != null) view.Dispose();
                }
                disposed = true;
            }
        }
    }
}
