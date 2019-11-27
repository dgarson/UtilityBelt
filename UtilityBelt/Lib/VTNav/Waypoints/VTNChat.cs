﻿using Decal.Adapter.Wrappers;
using Decal.Filters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace UtilityBelt.Lib.VTNav.Waypoints {
    class VTNChat : VTNPoint {
        public string Message = "";

        public VTNChat(StreamReader reader, VTNavRoute parentRoute, int index) : base(reader, parentRoute, index) {
            Type = eWaypointType.Pause;
        }

        new public bool Parse() {
            if (!base.Parse()) return false;

            Message = base.sr.ReadLine();

            return true;
        }

        public override void Draw() {
            if (!UtilityBeltPlugin.Instance.VisualNav.Display.ChatText.Enabled) return;

            var rp = GetPreviousPoint();
            rp = rp == null ? GetNextPoint() : rp;
            rp = rp == null ? this : rp;
            DrawText($"Chat: {Message}", rp, 0, Color.FromArgb(UtilityBeltPlugin.Instance.VisualNav.Display.ChatText.Color));
        }
    }
}
