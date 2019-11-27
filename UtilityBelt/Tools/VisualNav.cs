﻿using Decal.Adapter;
using System;
using Decal.Adapter.Wrappers;
using System.IO;
using UtilityBelt.Lib.VTNav;
using System.Drawing;
using System.Collections.Generic;
using VirindiViewService.Controls;
using VirindiViewService;
using UtilityBelt.Views;
using UtilityBelt.Lib;
using UtilityBelt.Lib.Settings;
using System.ComponentModel;
using Newtonsoft.Json;

namespace UtilityBelt.Tools {
    #region VisualNav Display Config
    [Section("VisualNav display options")]
    public class VisualNavDisplayOptions : DisplaySectionBase {
        [JsonIgnore]
        public List<string> ValidSettings = new List<string>() {
                "Lines",
                "ChatText",
                "CurrentWaypoint",
                "JumpText",
                "JumpArrow",
                "OpenVendor",
                "Pause",
                "Portal",
                "Recall",
                "UseNPC",
                "FollowArrow"
            };

        [Summary("Point to point lines")]
        [DefaultEnabled(true)]
        [DefaultColor(-65281)]
        public ColorToggleOption Lines {
            get { return (ColorToggleOption)GetSetting("Lines"); }
            private set { UpdateSetting("Lines", value); }
        }

        [Summary("Chat commands")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption ChatText {
            get { return (ColorToggleOption)GetSetting("ChatText"); }
            private set { UpdateSetting("ChatText", value); }
        }

        [Summary("Jump commands")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption JumpText {
            get { return (ColorToggleOption)GetSetting("JumpText"); }
            private set { UpdateSetting("JumpText", value); }
        }

        [Summary("Jump heading arrow")]
        [DefaultEnabled(true)]
        [DefaultColor(-256)]
        public ColorToggleOption JumpArrow {
            get { return (ColorToggleOption)GetSetting("JumpArrow"); }
            private set { UpdateSetting("JumpArrow", value); }
        }

        [Summary("Open vendor")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption OpenVendor {
            get { return (ColorToggleOption)GetSetting("OpenVendor"); }
            private set { UpdateSetting("OpenVendor", value); }
        }

        [Summary("Pause commands")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption Pause {
            get { return (ColorToggleOption)GetSetting("Pause"); }
            private set { UpdateSetting("Pause", value); }
        }

        [Summary("Portal commands")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption Portal {
            get { return (ColorToggleOption)GetSetting("Portal"); }
            private set { UpdateSetting("Portal", value); }
        }

        [Summary("Recall spells")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption Recall {
            get { return (ColorToggleOption)GetSetting("Recall"); }
            private set { UpdateSetting("Recall", value); }
        }

        [Summary("Use NPC commands")]
        [DefaultEnabled(true)]
        [DefaultColor(-1)]
        public ColorToggleOption UseNPC {
            get { return (ColorToggleOption)GetSetting("UseNPC"); }
            private set { UpdateSetting("UseNPC", value); }
        }

        [Summary("Follow character arrow")]
        [DefaultEnabled(true)]
        [DefaultColor(-23296)]
        public ColorToggleOption FollowArrow {
            get { return (ColorToggleOption)GetSetting("FollowArrow"); }
            private set { UpdateSetting("FollowArrow", value); }
        }

        [Summary("Current waypoint ring")]
        [DefaultEnabled(true)]
        [DefaultColor(-7722014)]
        public ColorToggleOption CurrentWaypoint {
            get { return (ColorToggleOption)GetSetting("CurrentWaypoint"); }
            private set { UpdateSetting("CurrentWaypoint", value); }
        }

        public VisualNavDisplayOptions(SectionBase parent) : base(parent) {
            Name = "Display";
        }
    }
    #endregion

    [Name("VisualNav")]

    public class VisualNav : ToolBase {
        private string currentRoutePath = "";
        public VTNavRoute currentRoute = null;
        private bool forceUpdate = false;
        public bool needsDraw = false;

        FileSystemWatcher navFileWatcher = null;
        FileSystemWatcher profilesWatcher = null;
        private DateTime lastNavChange = DateTime.MinValue;

        private List<D3DObj> shapes = new List<D3DObj>();

        #region Config
        [Summary("Enabled")]
        [DefaultValue(true)]
        public bool Enabled {
            get { return (bool)GetSetting("Enabled"); }
            set { UpdateSetting("Enabled", value); }
        }

        [Summary("ScaleCurrentWaypoint")]
        [DefaultValue(true)]
        public bool ScaleCurrentWaypoint {
            get { return (bool)GetSetting("ScaleCurrentWaypoint"); }
            set { UpdateSetting("ScaleCurrentWaypoint", value); }
        }

        [Summary("Line offset from the ground, in meters")]
        [DefaultValue(0.05f)]
        public float LineOffset {
            get { return (float)GetSetting("LineOffset"); }
            set { UpdateSetting("LineOffset", value); }
        }

        [Summary("Automatically save [None] routes. Enabling this allows embedded routes to be drawn.")]
        [DefaultValue(false)]
        public bool SaveNoneRoutes {
            get { return (bool)GetSetting("SaveNoneRoutes"); }
            set { UpdateSetting("SaveNoneRoutes", value); }
        }

        [Summary("VisualNav display options")]
        public VisualNavDisplayOptions Display { get; set; } = null;
        #endregion

        public VisualNav(UtilityBeltPlugin ub, string name) : base(ub, name) {
            Display = new VisualNavDisplayOptions(this);
            UB.Core.CharacterFilter.ChangePortalMode += CharacterFilter_ChangePortalMode;

            var server = UB.Core.CharacterFilter.Server;
            var character = UB.Core.CharacterFilter.Name;
            if (Directory.Exists(Util.GetVTankProfilesDirectory())) {
                profilesWatcher = new FileSystemWatcher();
                profilesWatcher.Path = Util.GetVTankProfilesDirectory();
                profilesWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite;
                profilesWatcher.Filter = $"{server}_{character}.cdf";
                profilesWatcher.Changed += Profiles_Changed;
                profilesWatcher.EnableRaisingEvents = true;
            } else {
                LogError($"{Util.GetVTankProfilesDirectory()} does not exist!");
                return;
            }

            Display.PropertyChanged += (s, e) => { needsDraw = true; };
            DrawCurrentRoute();

            uTank2.PluginCore.PC.NavRouteChanged += PC_NavRouteChanged;

            PropertyChanged += (s, e) => {
                if (e.PropertyName == "Enabled") {
                    forceUpdate = true;
                    DrawCurrentRoute();
                }
            };
        }

        private void PC_NavRouteChanged() {
            try {
                if (!SaveNoneRoutes || !Enabled) return;

                needsDraw = true;

                var routePath = VTNavRoute.GetLoadedNavigationProfile();
                var vTank = VTankControl.vTankInstance;

                if (vTank == null || vTank.NavNumPoints <= 0) return;

                // the route has changed, but we are currently in a [None] route, so we will save it
                // to a new route called " [None].nav" so we can parse and draw it.
                if (string.IsNullOrEmpty(vTank.GetNavProfile())) {
                    Util.DispatchChatToBoxWithPluginIntercept($"/vt nav save {VTNavRoute.NoneNavName}");
                    needsDraw = true;
                }

                // the route has changed, and we are on our custon [None].nav, so we force a redraw
                if (vTank.GetNavProfile().StartsWith(VTNavRoute.NoneNavName)) {
                    needsDraw = true;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private ACImage GetSettingIcon(ColorToggleOption option) {
            var bmp = new Bitmap(32, 32);
            using (Graphics gfx = Graphics.FromImage(bmp)) {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(option.Color))) {
                    gfx.FillRectangle(brush, 0, 0, 32, 32);
                }
            }

            return new ACImage(bmp);
        }

        private void Profiles_Changed(object sender, FileSystemEventArgs e) {
            try {
                needsDraw = true;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void CharacterFilter_ChangePortalMode(object sender, ChangePortalModeEventArgs e) {
            try {
                if (e.Type == PortalEventType.ExitPortal) {
                    needsDraw = true;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void DrawCurrentRoute() {
            var vTank = VTankControl.vTankInstance;

            if (!Enabled || UB.Plugin.VideoPatch || string.IsNullOrEmpty(vTank.GetNavProfile())) {
                ClearCurrentRoute();
                return;
            }

            var routePath = Path.Combine(Util.GetVTankProfilesDirectory(), vTank.GetNavProfile());
            if (routePath == currentRoutePath && !forceUpdate) return;

            forceUpdate = false;
            ClearCurrentRoute();
            currentRoutePath = routePath;

            if (string.IsNullOrEmpty(currentRoutePath) || !File.Exists(currentRoutePath)) return;

            currentRoute = new VTNavRoute(routePath, UB);
            currentRoute.Parse();

            currentRoute.Draw();

            if (navFileWatcher != null) {
                navFileWatcher.EnableRaisingEvents = false;
                navFileWatcher.Dispose();
            }

            if (!vTank.GetNavProfile().StartsWith(VTNavRoute.NoneNavName)) {
                WatchRouteFiles();
            }
        }

        private void WatchRouteFiles() {
            if (!Directory.Exists(Util.GetVTankProfilesDirectory())) {
                LogDebug($"WatchRouteFiles() Error: {Util.GetVTankProfilesDirectory()} does not exist!");
                return;
            }
            navFileWatcher = new FileSystemWatcher();
            navFileWatcher.Path = Util.GetVTankProfilesDirectory();
            navFileWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite;
            navFileWatcher.Filter = "*.nav";
            navFileWatcher.Changed += NavFile_Changed;

            navFileWatcher.EnableRaisingEvents = true;
        }

        private void NavFile_Changed(object sender, FileSystemEventArgs e) {
            try {
                if (e.FullPath != currentRoutePath || DateTime.UtcNow - lastNavChange < TimeSpan.FromMilliseconds(50)) return;
                lastNavChange = DateTime.UtcNow;
                needsDraw = true;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void ClearCurrentRoute() {
            if (currentRoute == null) return;

            currentRoute.Dispose();

            currentRoute = null;
        }

        public void Think() {
            if (needsDraw) {
                needsDraw = false;
                forceUpdate = true;
                DrawCurrentRoute();
            }
        }

        protected override void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    try {
                        UB.Core.CharacterFilter.ChangePortalMode -= CharacterFilter_ChangePortalMode;
                        uTank2.PluginCore.PC.NavRouteChanged -= PC_NavRouteChanged;
                    }
                    catch { }

                    if (profilesWatcher != null) profilesWatcher.Dispose();
                    if (navFileWatcher != null) navFileWatcher.Dispose();
                    if (currentRoute != null) currentRoute.Dispose();

                    base.Dispose(disposing);
                }
                disposedValue = true;
            }
        }
    }
}
