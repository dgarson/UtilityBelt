﻿using Antlr4.Runtime;
using Decal.Adapter.Wrappers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UtilityBelt.Lib;
using UtilityBelt.Lib.Expressions;
using UtilityBelt.Lib.VTNav;

namespace UtilityBelt.Tools {
    [Name("VTank")]
    public class VTankControl : ToolBase {
        public Dictionary<string, object> ExpressionVariables = new Dictionary<string, object>();
        private Random rnd = new Random();

        public class Stopwatch {
            public System.Diagnostics.Stopwatch Watch { get; }

            public Stopwatch() {
                Watch = new System.Diagnostics.Stopwatch();
            }

            public void Start() {
                Watch.Start();
            }

            public void Stop() {
                Watch.Stop();
            }

            public double Elapsed() {
                return (double)(Watch.ElapsedMilliseconds / 1000.0);
            }

            public override string ToString() {
                return "[STOPWATCH]";
            }
        }

        public class Wobject {
            public WorldObject Wo { get; set; }

            public Wobject(int id) {
                Wo = UtilityBeltPlugin.Instance.Core.WorldFilter[id];
            }

            public override string ToString() {
                return $"{Wo.Id:X8}: {Wo.Name}, {Wo.ObjectClass}";
            }
        }

        public class Coordinates {
            public double NS { get; set; } = 0;
            public double EW { get; set; } = 0;
            public double Z { get; set; } = 0;
            private static Regex CoordSearchRegex = new Regex("(?<NSval>(\\d{1,3}(\\.\\d{1,4})?)|(\\.\\d{1,4}))\\s*(?<NSchr>[ns])[;/,\\s]{0,4}\\s*(?<EWval>(\\d{1,3}(\\.\\d{1,4})?)|(\\.\\d{1,4}))\\s*(?<EWchr>[ew])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            public static Coordinates FromString(string coordsToParse) {
                var coords = new Coordinates();

                if (CoordSearchRegex.IsMatch(coordsToParse)) {
                    var m = CoordSearchRegex.Match(coordsToParse);
                    coords.NS = double.Parse(m.Groups["NSval"].Value, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);
                    coords.NS *= m.Groups["NSChar"].Value.ToLower().Equals("n") ? 1 : -1;
                    coords.EW = double.Parse(m.Groups["EWval"].Value, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);
                    coords.EW *= m.Groups["EWChar"].Value.ToLower().Equals("e") ? 1 : -1;
                }

                return coords;
            }

            public double DistanceTo(Coordinates other) {
                var nsdiff = (((NS * 10) + 1019.5) * 24) - (((other.NS * 10) + 1019.5) * 24);
                var ewdiff = (((EW * 10) + 1019.5) * 24) - (((other.EW * 10) + 1019.5) * 24);
                return Math.Abs(Math.Sqrt(Math.Pow(Math.Abs(nsdiff), 2) + Math.Pow(Math.Abs(ewdiff), 2) + Math.Pow(Math.Abs(Z - other.Z), 2)));
            }

            public double DistanceToFlat(Coordinates other) {
                var nsdiff = (((NS * 10) + 1019.5) * 24) - (((other.NS * 10) + 1019.5) * 24);
                var ewdiff = (((EW * 10) + 1019.5) * 24) - (((other.EW * 10) + 1019.5) * 24);
                return Math.Abs(Math.Sqrt(Math.Pow(Math.Abs(nsdiff), 2) + Math.Pow(Math.Abs(ewdiff), 2)));
            }

            public override string ToString() {
                return $"{Math.Abs(NS).ToString("F1")}{(NS>0?"N":"S")}, {Math.Abs(EW).ToString("F1")}{(EW>0?"E":"W")} (Z {Z.ToString("F2")})";
            }
        }

        #region Config
        [Summary("VitalSharing")]
        [DefaultValue(true)]
        [Hotkey("VitalSharing", "Toggle VitalSharing functionality")]
        public bool VitalSharing {
            get { return (bool)GetSetting("VitalSharing"); }
            set { UpdateSetting("VitalSharing", value); }
        }

        [Summary("PatchExpressionEngine")]
        [DefaultValue(false)]
        [Hotkey("PatchExpressionEngine", "Overrides vtank's meta expression engine. This allows for new meta expression functions and language features.")]
        public bool PatchExpressionEngine {
            get { return (bool)GetSetting("PatchExpressionEngine"); }
            set { UpdateSetting("PatchExpressionEngine", value); }
        }

        [Summary("Detect and fix vtank nav portal loops")]
        [DefaultValue(false)]
        public bool FixPortalLoops {
            get { return (bool)GetSetting("FixPortalLoops"); }
            set { UpdateSetting("FixPortalLoops", value); }
        }

        [Summary("Number of portal loops to the same location to trigger portal loop fix")]
        [DefaultValue(3)]
        public int PortalLoopCount {
            get { return (int)GetSetting("PortalLoopCount"); }
            set { UpdateSetting("PortalLoopCount", value); }
        }
        #endregion

        #region Commands
        [Summary("Translates a VTank nav route from one landblock to another. Add force flag to overwrite the output nav. **NOTE**: This will translate **ALL** points, even if some are in a dungeon and some are not, it doesn't care.")]
        [Usage("/ub translateroute <startLandblock> <routeToLoad> <endLandblock> <routeToSaveAs> [force]")]
        [Example("/ub translateroute 0x00640371 eo-east.nav 0x002B0371 eo-main.nav", "Translates eo-east.nav to landblock 0x002B0371(eo main) and saves it as eo-main.nav if the file doesn't exist")]
        [Example("/ub translateroute 0x00640371 eo-east.nav 0x002B0371 eo-main.nav force", "Translates eo-east.nav to landblock 0x002B0371(eo main) and saves it as eo-main.nav, overwriting if the file exists")]
        [CommandPattern("translateroute", @"^ *(?<StartLandblock>[0-9A-Fx]+) +(?<RouteToLoad>.+\.(nav)) +(?<EndLandblock>[0-9A-Fx]+) +(?<RouteToSaveAs>.+\.(nav)) *(?<Force>force)?$")]
        public void TranslateRoute(string command, Match args) {
            try {
                LogDebug($"Translating route: RouteToLoad:{args.Groups["RouteToLoad"].Value} StartLandblock:{args.Groups["StartLandblock"].Value} EndLandblock:{args.Groups["EndLandblock"].Value} RouteToSaveAs:{args.Groups["RouteToSaveAs"].Value} Force:{!string.IsNullOrEmpty(args.Groups["Force"].Value)}");

                if (!uint.TryParse(args.Groups["StartLandblock"].Value.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint startLandblock)) {
                    LogError($"Could not parse hex value from StartLandblock: {args.Groups["StartLandblock"].Value}");
                    return;
                }

                if (!uint.TryParse(args.Groups["EndLandblock"].Value.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint endLandblock)) {
                    LogError($"Could not parse hex value from EndLandblock: {args.Groups["EndLandblock"].Value}");
                    return;
                }

                var loadPath = Path.Combine(Util.GetVTankProfilesDirectory(), args.Groups["RouteToLoad"].Value);
                if (!File.Exists(loadPath)) {
                    LogError($"Could not find route to load: {loadPath}");
                    return;
                }

                var savePath = Path.Combine(Util.GetVTankProfilesDirectory(), args.Groups["RouteToSaveAs"].Value);
                if (string.IsNullOrEmpty(args.Groups["Force"].Value) && File.Exists(savePath)) {
                    LogError($"Output path already exists! Run with force flag to overwrite: {savePath}");
                    return;
                }

                var route = new Lib.VTNav.VTNavRoute(loadPath, UB);
                if (!route.Parse()) {
                    LogError($"Unable to parse route");
                    return;
                }
                var allPoints = route.points.Where((p) => (p.Type == Lib.VTNav.eWaypointType.Point)).ToArray();
                if (allPoints.Length <= 0) {
                    LogError($"Unable to translate route, no nav points found! Type:{route.NavType}");
                    return;
                }

                var ewOffset = Geometry.LandblockXDifference(startLandblock, endLandblock) / 240f;
                var nsOffset = Geometry.LandblockYDifference(startLandblock, endLandblock) / 240f;

                foreach (var point in route.points) {
                    point.EW += ewOffset;
                    point.NS += nsOffset;
                }

                using (StreamWriter file = new StreamWriter(savePath)) {
                    route.Write(file);
                    file.Flush();
                }

                LogDebug($"Translated {route.RecordCount} records from {startLandblock:X8} to {endLandblock:X8} by adding offsets NS:{nsOffset} EW:{ewOffset}\nSaved to file: {savePath}");
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
        #endregion

        #region Expressions
        #region Variables
        #region testvar[string varname]
        [ExpressionMethod("testvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable is defined, 0 if it isn't")]
        [Summary("Checks if a variable is defined")]
        [Example("testvar[myvar]", "Returns 1 if `myvar` variable is defined")]
        public object Testvar(string varname) {
            return ExpressionVariables.ContainsKey(varname);
        }
        #endregion //testvar[string varname]
        #region getvar[string varname]
        [ExpressionMethod("getvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to get")]
        [ExpressionReturn(typeof(double), "Returns the value of a variable, or 0 if undefined")]
        [Summary("Returns the value stored in a variable")]
        [Example("getvar[myvar]", "Returns the value stored in `myvar` variable")]
        public object Getvar(string varname) {
            if (ExpressionVariables.ContainsKey(varname))
                return ExpressionVariables[varname];

            return 0;
        }
        #endregion //getvar[string varname]
        #region setvar[string varname, object value]
        [ExpressionMethod("setvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to set")]
        [ExpressionParameter(1, typeof(object), "value", "Value to store")]
        [ExpressionReturn(typeof(object), "Returns the newly set value")]
        [Summary("Stores a value in a variable")]
        [Example("setvar[myvar,1]", "Stores the number value `1` inside of `myvar` variable")]
        public object Setvar(string varname, object value) {
            object v = (value == null) ? 0 : value;
            if (ExpressionVariables.ContainsKey(varname))
                ExpressionVariables[varname] = v;
            else
                ExpressionVariables.Add(varname, v);

            return value;
        }
        #endregion //setvar[string varname, object value]
        #region touchvar[string varname]
        [ExpressionMethod("touchvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to touch")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was previously defined, 0 otherwise")]
        [Summary("Sets the value of a variable to 0 if the variable was previously undefined")]
        [Example("touchvar[myvar]", "Ensures that `myvar` has a value set")]
        public object Touchvar(string varname) {
            if (ExpressionVariables.ContainsKey(varname))
                return true;
            else
                ExpressionVariables.Add(varname, false);

            return false;
        }
        #endregion //touchvar[string varname]
        #region clearallvars[]
        [ExpressionMethod("clearallvars")]
        [Summary("Unsets all variables")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Example("clearallvars[]", "Unset all variables")]
        public object Clearallvars() {
            ExpressionVariables.Clear();
            return true;
        }
        #endregion //clearallvars[]
        #region clearvar[string varname]
        [ExpressionMethod("clearvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to clear")]
        [Summary("Clears the value of a variable")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was defined, 0 otherwise")]
        [Example("clearvar[myvar]", "Clears the value stored in `myvar` variable")]
        public object Clearvar(string varname) {
            if (ExpressionVariables.ContainsKey(varname)) {
                ExpressionVariables.Remove(varname);
                return true;
            }

            return false;
        }
        #endregion //clearvar[string varname]
        #region testpvar[string varname]
        [ExpressionMethod("testpvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable is defined, 0 if it isn't")]
        [Summary("Checks if a persistent variable is defined")]
        [Example("testpvar[myvar]", "Returns 1 if `myvar` persistent variable is defined")]
        public object Testpvar(string varname) {
            var variable = UB.Database.PersistentVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.And(
                        LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server),
                        LiteDB.Query.EQ("Character", UB.Core.CharacterFilter.Name)
                    )
                )
            );

            return variable != null;
        }
        #endregion //testpvar[string varname]
        #region getpvar[string varname]
        [ExpressionMethod("getpvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to get")]
        [ExpressionReturn(typeof(double), "Returns the value of a variable, or 0 if undefined")]
        [Summary("Returns the value stored in a variable")]
        [Example("getpvar[myvar]", "Returns the value stored in `myvar` variable")]
        public object Getpvar(string varname) {
            var variable = UB.Database.PersistentVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.And(
                        LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server),
                        LiteDB.Query.EQ("Character", UB.Core.CharacterFilter.Name)
                    )
                )
            );

            return EvaluateExpression(variable == null ? "0" : variable.Value);
        }
        #endregion //getpvar[string varname]
        #region setpvar[string varname, object value]
        [ExpressionMethod("setpvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to set")]
        [ExpressionParameter(1, typeof(object), "value", "Value to store")]
        [ExpressionReturn(typeof(object), "Returns the newly set value")]
        [Summary("Stores a value in a persistent variable that is available ever after relogging.  Persistent variables are not shared between characters.")]
        [Example("setpvar[myvar,1]", "Stores the number value `1` inside of `myvar` variable")]
        public object Setpvar(string varname, object value) {
            string expressionValue = "";
            var type = value.GetType();

            if (type == typeof(Boolean))
                expressionValue = ((Boolean)value) ? "1" : "0";
            else if (type == typeof(string))
                expressionValue = (string)value;
            else if (type == typeof(double))
                expressionValue = ((double)value).ToString();
            else {
                Logger.Error("Persistent variables can currently only store strings/numbers");
                return 0;
            }
            var variable = UB.Database.PersistentVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.And(
                        LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server),
                        LiteDB.Query.EQ("Character", UB.Core.CharacterFilter.Name)
                    )
                )
            );

            if (variable == null) {
                UB.Database.PersistentVariables.Insert(new Lib.Models.PersistentVariable() {
                    Server = UB.Core.CharacterFilter.Server,
                    Character = UB.Core.CharacterFilter.Name,
                    Name = varname,
                    Value = expressionValue
                });
            }
            else {
                variable.Value = expressionValue;
                UB.Database.PersistentVariables.Update(variable);
            }

            return EvaluateExpression(expressionValue);
        }
        #endregion //setpvar[string varname, object value]
        #region touchpvar[string varname]
        [ExpressionMethod("touchpvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to touch")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was previously defined, 0 otherwise")]
        [Summary("Sets the value of a persistent variable to 0 if the variable was previously undefined")]
        [Example("touchpvar[myvar]", "Ensures that `myvar` has a value set")]
        public object Touchpvar(string varname) {
            if ((bool)Testpvar(varname)) {
                return true;
            }
            else {
                Setpvar(varname, 0);
                return false;
            }
        }
        #endregion //touchpvar[string varname]
        #region clearallpvars[]
        [ExpressionMethod("clearallpvars")]
        [Summary("Unsets all persistent variables")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Example("clearallpvars[]", "Unset all persistent variables")]
        public object Clearallpvars() {
            UB.Database.PersistentVariables.Delete(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server),
                    LiteDB.Query.EQ("Character", UB.Core.CharacterFilter.Name)
                )
            );
            return true;
        }
        #endregion //clearallpvars[]
        #region clearpvar[string varname]
        [ExpressionMethod("clearpvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to clear")]
        [Summary("Clears the value of a persistent variable")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was defined, 0 otherwise")]
        [Example("clearpvar[myvar]", "Clears the value stored in `myvar` persistent variable")]
        public object Clearpvar(string varname) {
            if ((bool)Testpvar(varname)) {
                UB.Database.PersistentVariables.Delete(
                    LiteDB.Query.And(
                        LiteDB.Query.EQ("Name", varname),
                        LiteDB.Query.And(
                            LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server),
                            LiteDB.Query.EQ("Character", UB.Core.CharacterFilter.Name)
                        )
                    )
                );
                return true;
            }

            return false;
        }
        #endregion //clearpvar[string varname]
        #region testgvar[string varname]
        [ExpressionMethod("testgvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable is defined, 0 if it isn't")]
        [Summary("Checks if a global variable is defined")]
        [Example("testgvar[myvar]", "Returns 1 if `myvar` global variable is defined")]
        public object Testgvar(string varname) {
            var variable = UB.Database.GlobalVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server)
                )
            );

            return variable != null;
        }
        #endregion //testgvar[string varname]
        #region getgvar[string varname]
        [ExpressionMethod("getgvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to get")]
        [ExpressionReturn(typeof(double), "Returns the value of a variable, or 0 if undefined")]
        [Summary("Returns the value stored in a variable")]
        [Example("getgvar[myvar]", "Returns the value stored in `myvar` global variable")]
        public object Getgvar(string varname) {
            var variable = UB.Database.GlobalVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server)
                )
            );

            return EvaluateExpression(variable == null ? "0" : variable.Value);
        }
        #endregion //getgvar[string varname]
        #region setgvar[string varname, object value]
        [ExpressionMethod("setgvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to set")]
        [ExpressionParameter(1, typeof(object), "value", "Value to store")]
        [ExpressionReturn(typeof(object), "Returns the newly set value")]
        [Summary("Stores a value in a global variable. This variable is shared between all characters on the same server.")]
        [Example("setgvar[myvar,1]", "Stores the number value `1` inside of `myvar` variable")]
        public object Setgvar(string varname, object value) {
            string expressionValue = "";
            var type = value.GetType();

            if (type == typeof(Boolean))
                expressionValue = ((Boolean)value) ? "1" : "0";
            else if (type == typeof(string))
                expressionValue = (string)value;
            else if (type == typeof(double))
                expressionValue = ((double)value).ToString();
            else {
                Logger.Error("Global variables can currently only store strings/numbers");
                return 0;
            }
            var variable = UB.Database.GlobalVariables.FindOne(
                LiteDB.Query.And(
                    LiteDB.Query.EQ("Name", varname),
                    LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server)
                )
            );

            if (variable == null) {
                UB.Database.GlobalVariables.Insert(new Lib.Models.GlobalVariable() {
                    Server = UB.Core.CharacterFilter.Server,
                    Name = varname,
                    Value = expressionValue
                });
            }
            else {
                variable.Value = expressionValue;
                UB.Database.GlobalVariables.Update(variable);
            }

            return EvaluateExpression(expressionValue);
        }
        #endregion //setgvar[string varname, object value]
        #region touchgvar[string varname]
        [ExpressionMethod("touchgvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to touch")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was previously defined, 0 otherwise")]
        [Summary("Sets the value of a global variable to 0 if the variable was previously undefined")]
        [Example("touchgvar[myvar]", "Ensures that `myvar` global variable has a value set")]
        public object Touchgvar(string varname) {
            if ((bool)Testgvar(varname)) {
                return true;
            }
            else {
                Setgvar(varname, 0);
                return false;
            }
        }
        #endregion //touchgvar[string varname]
        #region clearallgvars[]
        [ExpressionMethod("clearallgvars")]
        [Summary("Unsets all global variables")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Example("clearallgvars[]", "Unset all global variables")]
        public object Clearallgvars() {
            UB.Database.GlobalVariables.Delete(
                LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server)
            );
            return true;
        }
        #endregion //clearallgvars[]
        #region cleargvar[string varname]
        [ExpressionMethod("cleargvar")]
        [ExpressionParameter(0, typeof(string), "varname", "Variable name to clear")]
        [Summary("Clears the value of a global variable")]
        [ExpressionReturn(typeof(double), "Returns 1 if the variable was defined, 0 otherwise")]
        [Example("cleargvar[myvar]", "Clears the value stored in `myvar` global variable")]
        public object Cleargvar(string varname) {
            if ((bool)Testpvar(varname)) {
                UB.Database.GlobalVariables.Delete(
                    LiteDB.Query.And(
                        LiteDB.Query.EQ("Name", varname),
                        LiteDB.Query.EQ("Server", UB.Core.CharacterFilter.Server)
                    )    
                );
                return true;
            }

            return false;
        }
        #endregion //cleargvar[string varname]
        #endregion //Variables
        #region Chat
        #region chatbox[string message]
        [ExpressionMethod("chatbox")]
        [ExpressionParameter(0, typeof(string), "message", "Message to send")]
        [ExpressionReturn(typeof(string), "Returns the string sent to the charbox")]
        [Summary("Sends a message to the chatbox as if you had typed it in")]
        [Example("chatbox[test]", "sends 'test' to the chatbox")]
        public object Chatbox(string text) {
            Util.DispatchChatToBoxWithPluginIntercept(text);
            return text;
        }
        #endregion //chatboxpaste[string message]
        #region chatboxpaste[string message]
        [ExpressionMethod("chatboxpaste")]
        [ExpressionParameter(0, typeof(string), "message", "Message to paste")]
        [ExpressionReturn(typeof(double), "Returns 1 if successful")]
        [Summary("Pastes a message to the chatbox, leaving focus, so that the user can complete typing it")]
        [Example("chatboxpaste[test]", "pastes `test` to the chatbox, without sending")]
        public object Chatboxpaste(string text) {
            Logger.Error("chatboxpaste[] is not currently implemented");
            return 0;
        }
        #endregion //chatboxpaste[string message]
        #region echo[string message, int color]
        [ExpressionMethod("echo")]
        [ExpressionParameter(0, typeof(string), "message", "Message to echo")]
        [ExpressionParameter(1, typeof(double), "color", "Message color")]
        [ExpressionReturn(typeof(double), "Returns 1 on success, 0 on failure")]
        [Summary("Echos chat to the chatbox with a color. Use `/ub printcolors` to view all colors")]
        [Example("echo[test,15]", "echos 'test' to the chatbox in red (Help)")]
        public object Echo(object text, double color) {
            Util.WriteToChat(text.ToString(), Convert.ToInt32(color), false);
            return true;
        }
        #endregion //echo[string message, int color]
        #endregion //Chat
        #region Character
        #region getcharintprop[int property]
        [ExpressionMethod("getcharintprop")]
        [ExpressionParameter(0, typeof(double), "property", "IntProperty to return")]
        [ExpressionReturn(typeof(double), "Returns an int property from your character")]
        [Summary("Returns an int property from your character, or 0 if it's undefined")]
        [Example("getcharintprop[25]", "Returns your character's current level")]
        public object Getcharintprop(double property) {
            var v = (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.GetCharProperty(Convert.ToInt32(property));
            return (v != -1) ? v : 0;
        }
        #endregion //getcharintprop[int property]
        #region getchardoubleprop[int property]
        [ExpressionMethod("getchardoubleprop")]
        [ExpressionParameter(0, typeof(double), "property", "DoubleProperty to return")]
        [ExpressionReturn(typeof(double), "Returns a double property from your character")]
        [Summary("Returns a double property from your character, or 0 if it's undefined")]
        [Example("getchardoubleprop[25]", "Returns your character's current level")]
        public object Getchardoubleprop(double property) {
            return GetCharacter().Values((DoubleValueKey)Convert.ToInt32(property), 0);
        }
        #endregion //getchardoubleprop[int property]
        #region getcharquadprop[int property]
        [ExpressionMethod("getcharquadprop")]
        [ExpressionParameter(0, typeof(double), "property", "QuadProperty to return")]
        [ExpressionReturn(typeof(double), "Returns a quad property from your character")]
        [Summary("Returns a quad property from your character, or 0 if it's undefined")]
        [Example("getcharquadprop[25]", "Returns your character's TotalExperience")]
        public object Getcharquadprop(double property) {
            switch (property) {
                case 1:
                    return (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.TotalXP;
                case 2:
                    return (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.UnassignedXP;
                case 6:
                    // TODO:
                    Logger.Error($"getcharquadprop[6] (LuminancePointsCurrent) is currently unsupported and will always return 0");
                    return 0;
            }

            return 0;
        }
        #endregion //getcharquadprop[int property]
        #region getcharboolprop[int property]
        [ExpressionMethod("getcharboolprop")]
        [ExpressionParameter(0, typeof(double), "property", "BoolProperty to return")]
        [ExpressionReturn(typeof(double), "Returns a bool property from your character")]
        [Summary("Returns a bool property from your character, or 0 if it's undefined")]
        [Example("getcharboolprop[110]", "Returns your character's AwayFromKeyboard status")]
        public object Getcharboolprop(double property) {
            return GetCharacter().Values((BoolValueKey)Convert.ToInt32(property), false);
        }
        #endregion //getcharboolprop[int property]
        #region getcharstringprop[int property]
        [ExpressionMethod("getcharstringprop")]
        [ExpressionParameter(0, typeof(double), "property", "StringProperty to return")]
        [ExpressionReturn(typeof(string), "Returns a string property from your character")]
        [Summary("Returns a string property from your character, or false if it's undefined")]
        [Example("getcharstringprop[1]", "Returns your character's name")]
        public object Getcharstringprop(double property) {
            var v = GetCharacter().Values((StringValueKey)Convert.ToInt32(property), "");

            if (string.IsNullOrEmpty(v))
                return 0;

            return v;
        }
        #endregion //getcharstringprop[int property]
        #region getisspellknown[int spellId]
        [ExpressionMethod("getisspellknown")]
        [ExpressionParameter(0, typeof(double), "spellId", "Spell ID to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if your character knows this spell, 0 otherwise")]
        [Summary("Checks if your character knowns a spell by id")]
        [Example("getisspellknown[2931]", "Checks if your character knowns the Recall Aphus Lassel spell")]
        public object Getisspellknown(double spellId) {
            return Spells.IsKnown(Convert.ToInt32(spellId));
        }
        #endregion //getisspellknown[int spellId]
        #region getcancastspell_hunt[int spellId]
        [ExpressionMethod("getcancastspell_hunt")]
        [ExpressionParameter(0, typeof(double), "spellId", "Spell ID to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if your character can cast this spell while hunting, 0 otherwise")]
        [Summary("Checks if your character is capable of casting spellId while hunting")]
        [Example("getcancastspell_hunt[2931]", "Checks if your character is capable of casting the Recall Aphus Lassel spell while hunting")]
        public object Getcancastspell_hunt(double spellId) {
            var id = Convert.ToInt32(spellId);
            return Spells.IsKnown(id) && Spells.HasComponents(id) && Spells.HasSkillHunt(id);
        }
        #endregion //getcancastspell_hunt[int spellId]
        #region getcancastspell_buff[int spellId]
        [ExpressionMethod("getcancastspell_buff")]
        [ExpressionParameter(0, typeof(double), "spellId", "Spell ID to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if your character can cast this spell while buffing, 0 otherwise")]
        [Summary("Checks if your character is capable of casting spellId while buffing")]
        [Example("getcancastspell_hunt[2931]", "Checks if your character is capable of casting the Recall Aphus Lassel spell while buffing")]
        public object Getcancastspell_buff(double spellId) {
            var id = Convert.ToInt32(spellId);
            return Spells.IsKnown(id) && Spells.HasComponents(id) && Spells.HasSkillBuff(id);
        }
        #endregion //getcancastspell_buff[int spellId]
        #region getcharvital_base[int vitalId]
        [ExpressionMethod("getcharvital_base")]
        [ExpressionParameter(0, typeof(double), "vitalId", "Which vital to get. 1 = Health, 2 = Stamina, 3 = Mana.")]
        [ExpressionReturn(typeof(double), "Returns the base (unbuffed) vital value")]
        [Summary("Gets your characters base (unbuffed) vital value")]
        [Example("getcharvital_base[1]", "Returns your character's base health")]
        public object Getcharvital_base(double vitalId) {
            var id = Convert.ToInt32(vitalId * 2);
            return UtilityBeltPlugin.Instance.Core.CharacterFilter.Vitals[(CharFilterVitalType)id].Base;
        }
        #endregion //getcharvital_base[int vitalId]
        #region getcharvital_current[int vitalId]
        [ExpressionMethod("getcharvital_current")]
        [ExpressionParameter(0, typeof(double), "vitalId", "Which vital to get. 1 = Health, 2 = Stamina, 3 = Mana.")]
        [ExpressionReturn(typeof(double), "Returns the current vital value")]
        [Summary("Gets your characters current vital value")]
        [Example("getcharvital_current[2]", "Returns your character's current stamina")]
        public object Getcharvital_current(double vitalId) {
            var id = Convert.ToInt32(vitalId * 2);
            return UtilityBeltPlugin.Instance.Core.CharacterFilter.Vitals[(CharFilterVitalType)id].Current;
        }
        #endregion //getcharvital_current[int vitalId]
        #region getcharvital_buffedmax[int vitalId]
        [ExpressionMethod("getcharvital_buffedmax")]
        [ExpressionParameter(0, typeof(double), "vitalId", "Which vital to get. 1 = Health, 2 = Stamina, 3 = Mana.")]
        [ExpressionReturn(typeof(double), "Returns the buffed maximum vital value")]
        [Summary("Gets your characters buffed maximum vital value")]
        [Example("getcharvital_current[3]", "Returns your character's buffed maximum mana")]
        public object Getcharvital_buffedmax(double vitalId) {
            var id = Convert.ToInt32(vitalId * 2);
            return UtilityBeltPlugin.Instance.Core.CharacterFilter.Vitals[(CharFilterVitalType)id].Buffed;
        }
        #endregion //getcharvital_buffedmax[int vitalId]
        #region getcharskill_traininglevel[int skillId]
        [ExpressionMethod("getcharskill_traininglevel")]
        [ExpressionParameter(0, typeof(double), "skillId", "Which skill to check.")]
        [ExpressionReturn(typeof(double), "Returns current training level of a skill. 0 = Unusable, 1 = Untrained, 2 = Trained, 3 = Specialized")]
        [Summary("Gets your characters training level for a specified skill")]
        [Example("getcharskill_traininglevel[23]", "Returns your character's LockPick skill training level")]
        public object Getcharskill_traininglevel(double skillId) {
            return (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.Skills[(CharFilterSkillType)Convert.ToInt32(skillId)].Training;
        }
        #endregion //getcharskill_traininglevel[int vitalId]
        #region getcharskill_base[int skillId]
        [ExpressionMethod("getcharskill_base")]
        [ExpressionParameter(0, typeof(double), "skillId", "Which skill to check.")]
        [ExpressionReturn(typeof(double), "Returns base skill level of the specified skill")]
        [Summary("Gets your characters base skill level for a speficied skill")]
        [Example("getcharskill_base[43]", "Returns your character's base Void skill level")]
        public object Getcharskill_base(double skillId) {
            return (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.Skills[(CharFilterSkillType)Convert.ToInt32(skillId)].Base;
        }
        #endregion //getcharskill_base[int vitalId]
        #region getcharskill_buffed[int skillId]
        [ExpressionMethod("getcharskill_buffed")]
        [ExpressionParameter(0, typeof(double), "skillId", "Which skill to check.")]
        [ExpressionReturn(typeof(double), "Returns buffed skill level of the specified skill")]
        [Summary("Gets your characters buffed skill level for a speficied skill")]
        [Example("getcharskill_buffed[33]", "Returns your character's buffed Life Magic skill level")]
        public object Getcharskill_buffed(double skillId) {
            return (double)UtilityBeltPlugin.Instance.Core.CharacterFilter.Skills[(CharFilterSkillType)Convert.ToInt32(skillId)].Buffed;
        }
        #endregion //getcharskill_buffed[int vitalId]
        #region getplayerlandcell[]
        [ExpressionMethod("getplayerlandcell")]
        [ExpressionReturn(typeof(double), "Returns the landcell your character is currently standing in, including the landblock")]
        [Summary("Gets the landcell your character is currently standing in, including the landblock")]
        [Example("getplayerlandcell[]", "Returns your character's current landblock as in int")]
        public object Getplayerlandcell() {
            return (double)UtilityBeltPlugin.Instance.Core.Actions.Landcell;
        }
        #endregion //getplayerlandcell[]
        #region getplayercoordinates[]
        [ExpressionMethod("getplayercoordinates")]
        [ExpressionReturn(typeof(Coordinates), "Returns your character's global coordinates object")]
        [Summary("Gets the a coordinates object representing your characters current global position")]
        [Example("getplayercoordinates[]", "Returns your character's current position as coordinates object")]
        public object Getplayercoordinates() {
            return Wobjectgetphysicscoordinates((Wobject)Wobjectgetplayer());
        }
        #endregion //getplayercoordinates[]
        #endregion //Character
        #region Coordinates
        #region coordinategetns[coordinates obj]
        [ExpressionMethod("coordinategetns")]
        [ExpressionParameter(0, typeof(Coordinates), "obj", "coordinates object to get the NS position of")]
        [ExpressionReturn(typeof(double), "Returns the NS position of a coordinates object as a number")]
        [Summary("Gets the NS position of a coordinates object as a number")]
        [Example("coordinategetns[getplayercoordinates[]]", "Returns your character's current NS position")]
        public object Coordinategetns(Coordinates coords) {
            return coords.NS;
        }
        #endregion //coordinategetns[coordinates obj]
        #region coordinategetwe[coordinates obj]
        [ExpressionMethod("coordinategetwe")]
        [ExpressionParameter(0, typeof(Coordinates), "obj", "coordinates object to get the EW position of")]
        [ExpressionReturn(typeof(double), "Returns the EW position of a coordinates object as a number")]
        [Summary("Gets the EW position of a coordinates object as a number")]
        [Example("coordinategetwe[getplayercoordinates[]]", "Returns your character's current EW position")]
        public object Coordinategetew(Coordinates coords) {
            return coords.EW;
        }
        #endregion //coordinategetwe[coordinates obj]
        #region coordinategetz[coordinates obj]
        [ExpressionMethod("coordinategetz")]
        [ExpressionParameter(0, typeof(Coordinates), "obj", "coordinates object to get the Z position of")]
        [ExpressionReturn(typeof(double), "Returns the Z position of a coordinates object as a number")]
        [Summary("Gets the Z position of a coordinates object as a number")]
        [Example("coordinategetz[getplayercoordinates[]]", "Returns your character's current Z position")]
        public object Coordinategetz(Coordinates coords) {
            return coords.Z;
        }
        #endregion //coordinategetz[coordinates obj]
        #region coordinatetostring[coordinates obj]
        [ExpressionMethod("coordinatetostring")]
        [ExpressionParameter(0, typeof(Coordinates), "obj", "coordinates object to convert to a string")]
        [ExpressionReturn(typeof(double), "Returns a string representation of a coordinates object, like `1.2N, 34.5E`")]
        [Summary("Converts a coordinates object to a string representation")]
        [Example("coordinatetostring[getplayercoordinates[]]", "Returns your character's current coordinates as a string, eg `1.2N, 34.5E`")]
        public object Coordinatetostring(Coordinates coords) {
            return coords.ToString();
        }
        #endregion //coordinatetostring[coordinates obj]
        #region coordinateparse[string coordstring]
        [ExpressionMethod("coordinateparse")]
        [ExpressionParameter(0, typeof(string), "coordstring", "coordinates string to parse")]
        [ExpressionReturn(typeof(Coordinates), "Returns a coordinates object")]
        [Summary("Converts a coordinate string like `1.2N, 3.4E` to a coordinates object")]
        [Example("coordinateparse[`1.2N, 3.4E`]", "Returns a coordinates object representing `1.2N, 3.4E`")]
        public object Coordinateparse(string coordsToParse) {
            return Coordinates.FromString(coordsToParse);
        }
        #endregion //coordinateparse[string coordstring]
        #region coordinatedistancewithz[coordinates obj1, coordinates obj2]
        [ExpressionMethod("coordinatedistancewithz")]
        [ExpressionParameter(0, typeof(Coordinates), "obj1", "first coordinates object")]
        [ExpressionParameter(1, typeof(Coordinates), "obj2", "second coordinates object")]
        [ExpressionReturn(typeof(double), "Returns the 3d distance in meters between obj1 and obj2")]
        [Summary("Gets the 3d distance in meters between two coordinates objects")]
        [Example("coordinatedistancewithz[coordinateparse[`1.2N, 3.4E`], coordinateparse[`5.6N, 7.8E`]]", "Returns the 3d distance between `1.2N, 3.4E` and `5.6N, 7.8E`")]
        public object Coordinatedistancewithz(Coordinates obj1, Coordinates obj2) {
            return obj1.DistanceTo(obj2);
        }
        #endregion //coordinatedistancewithz[coordinates obj1, coordinates obj2]
        #region coordinatedistanceflat[coordinates obj1, coordinates obj2]
        [ExpressionMethod("coordinatedistanceflat")]
        [ExpressionParameter(0, typeof(Coordinates), "obj1", "first coordinates object")]
        [ExpressionParameter(1, typeof(Coordinates), "obj2", "second coordinates object")]
        [ExpressionReturn(typeof(double), "Returns the 2d distance in meters between obj1 and obj2 (ignoring Z)")]
        [Summary("Gets the 2d distance in meters between two coordinates objects (ignoring Z)")]
        [Example("coordinatedistancewithz[coordinateparse[`1.2N, 3.4E`], coordinateparse[`5.6N, 7.8E`]]", "Returns the 2d distance between `1.2N, 3.4E` and `5.6N, 7.8E` (ignoring Z)")]
        public object Coordinatedistanceflat(Coordinates obj1, Coordinates obj2) {
            return obj1.DistanceToFlat(obj2);
        }
        #endregion //coordinatedistanceflat[coordinates obj1, coordinates obj2]
        #endregion //Coordinates
        #region WorldObjects
        #region wobjectgetphysicscoordinates[worldobject wo]
        [ExpressionMethod("wobjectgetphysicscoordinates")]
        [ExpressionParameter(0, typeof(Wobject), "wo", "world object to get coordinates of")]
        [ExpressionReturn(typeof(Coordinates), "Returns a coordinates object representing the passed wobject")]
        [Summary("Gets a coordinates object representing a world objects current position")]
        [Example("wobjectgetphysicscoordinates[wobjectgetplayer[]]", "Returns a coordinates object representing the current player's position")]
        public object Wobjectgetphysicscoordinates(Wobject wo) {
            var pos = PhysicsObject.GetPosition(wo.Wo.Id);
            var landcell = PhysicsObject.GetLandcell(wo.Wo.Id);
            return new Coordinates() {
                NS = Geometry.LandblockToNS((uint)landcell, pos.Y),
                EW = Geometry.LandblockToEW((uint)landcell, pos.X),
                Z = pos.Z
            };
        }
        #endregion //wobjectgetphysicscoordinates[worldobject wo]
        #region wobjectgetname[worldobject wo]
        [ExpressionMethod("wobjectgetname")]
        [ExpressionParameter(0, typeof(Wobject), "wo", "world object to get the name of")]
        [ExpressionReturn(typeof(string), "Returns a string of wobject's name")]
        [Summary("Gets the name string of a wobject")]
        [Example("wobjectgetname[wobjectgetplayer[]]", "Returns a coordinates object representing the current player's position")]
        public object Wobjectgetname(Wobject wo) {
            return Util.GetObjectName(wo.Wo.Id);
        }
        #endregion //wobjectgetname[worldobject wo]
        #region wobjectgetobjectclass[worldobject wo]
        [ExpressionMethod("wobjectgetobjectclass")]
        [ExpressionParameter(0, typeof(Wobject), "wo", "world object to get the objectclass of")]
        [ExpressionReturn(typeof(double), "Returns a number representing the passed wobjects objectclass")]
        [Summary("Gets the objectclass as a number from a wobject")]
        [Example("wobjectgetobjectclass[wobjectgetplayer[]]", "Returns 24 (Player ObjectClass)")]
        public object Wobjectgetobjectclass(Wobject wo) {
            return Convert.ToDouble(wo.Wo.ObjectClass);
        }
        #endregion //wobjectgetobjectclass[worldobject wo]
        #region wobjectgettemplatetype[worldobject wo]
        [ExpressionMethod("wobjectgettemplatetype")]
        [ExpressionParameter(0, typeof(Wobject), "wo", "world object to get the template type of")]
        [ExpressionReturn(typeof(double), "Returns a number representing the passed wobjects template type")]
        [Summary("Gets the template type as a number from a wobject")]
        [Example("wobjectgettemplatetype[wobjectgetplayer[]]", "Returns 1 (Player template type)")]
        public object Wobjectgettemplatetype(Wobject wo) {
            return Convert.ToDouble(wo.Wo.Type);
        }
        #endregion //wobjectgettemplatetype[worldobject wo]
        #region wobjectgetisdooropen[worldobject wo]
        [ExpressionMethod("wobjectgetisdooropen")]
        [ExpressionParameter(0, typeof(Wobject), "wo", "door world object to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if the wo door is open, 0 otherwise")]
        [Summary("Checks if a door wobject is open")]
        [Example("wobjectgetisdooropen[wobjectfindnearestdoor[]]", "Returns 1 if the door is open, 0 otherwise")]
        public object Wobjectgetisdooropen(Wobject wo) {
            return UBHelper.InventoryManager.IsDoorOpen(wo.Wo.Id);
        }
        #endregion //wobjectgetisdooropen[worldobject wo]
        #region wobjectfindnearestmonster[]
        [ExpressionMethod("wobjectfindnearestmonster")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject representing the nearest monster")]
        [Summary("Gets a worldobject representing the nearest monster, or 0 if none was found")]
        [Example("wobjectfindnearestmonster[]", "Returns a worldobject representing the nearest monster")]
        public object Wobjectfindnearestmonster() {
            WorldObject closest = Util.FindClosestByObjectClass(ObjectClass.Monster);

            if (closest != null)
                return new Wobject(closest.Id);

            return 0;
        }
        #endregion //wobjectfindnearestmonster[]
        #region wobjectfindnearestdoor[]
        [ExpressionMethod("wobjectfindnearestdoor")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject representing the nearest door")]
        [Summary("Gets a worldobject representing the nearest door, or 0 if none was found")]
        [Example("wobjectfindnearestdoor[]", "Returns a worldobject representing the nearest door")]
        public object Wobjectfindnearestdoor() {
            WorldObject closest = Util.FindClosestByObjectClass(ObjectClass.Door);

            if (closest != null)
                return new Wobject(closest.Id);

            return 0;
        }
        #endregion //wobjectfindnearestdoor[]
        #region wobjectfindnearestbyobjectclass[int objectclass]
        [ExpressionMethod("wobjectfindnearestbyobjectclass")]
        [ExpressionParameter(0, typeof(double), "objectclass", "objectclass to filter by")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject representing the nearest matching objectclass")]
        [Summary("Gets a worldobject representing the nearest object matching objectclass, or 0 if none was found")]
        [Example("wobjectfindnearestbyobjectclass[24]", "Returns a worldobject of the nearest matching objectclass")]
        public object Wobjectfindnearestbyobjectclass(double objectClass) {
            WorldObject closest = null;
            var wos = UtilityBeltPlugin.Instance.Core.WorldFilter.GetByObjectClass((ObjectClass)Convert.ToInt32(objectClass));
            var closestDistance = float.MaxValue;
            foreach (var wo in wos) {
                if (wo.Id == UtilityBeltPlugin.Instance.Core.CharacterFilter.Id)
                    continue;
                if (PhysicsObject.GetDistance(wo.Id) < closestDistance) {
                    closest = wo;
                    closestDistance = PhysicsObject.GetDistance(wo.Id);
                }
            }
            wos.Dispose();

            if (closest != null)
                return new Wobject(closest.Id);

            return 0;
        }
        #endregion //wobjectfindnearestbyobjectclass[int objectclass]
        #region wobjectfindnearestbyobjectclass[int objectclass]
        [ExpressionMethod("wobjectfindininventorybytemplatetype")]
        [ExpressionParameter(0, typeof(double), "templatetype", "templatetype to filter by")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject")]
        [Summary("Gets a worldobject representing the first inventory item matching template type, or 0 if none was found")]
        [Example("wobjectfindininventorybytemplatetype[9060]", "Returns a worldobject of the first inventory item that is a Titan Mana Charge (template type 9060)")]
        public object Wobjectfindininventorybytemplatetype(double templateType) {
            var wos = UtilityBeltPlugin.Instance.Core.WorldFilter.GetInventory();
            var typeInt = Convert.ToInt32(templateType);
            foreach (var wo in wos) {
                if (wo.Type == typeInt) {
                    var r = new Wobject(wo.Id);
                    wos.Dispose();
                    return r;
                }
            }
            wos.Dispose();

            return 0;
        }
        #endregion //wobjectfindininventorybytemplatetype[int objectclass]
        #region wobjectfindininventorybyname[string name]
        [ExpressionMethod("wobjectfindininventorybyname")]
        [ExpressionParameter(0, typeof(string), "name", "exact name to filter by")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject")]
        [Summary("Gets a worldobject representing the first inventory item matching an exact name, or 0 if none was found")]
        [Example("wobjectfindininventorybyname[Massive Mana Charge]", "Returns a worldobject of the first inventory item that is named `Massive Mana Charge`")]
        public object Wobjectfindininventorybyname(string name) {
            var wos = UtilityBeltPlugin.Instance.Core.WorldFilter.GetInventory();
            wos.SetFilter(new ByNameFilter(name));

            if (wos.Count > 0) {
                var first = wos.First();
                wos.Dispose();
                return new Wobject(first.Id);
            }
            wos.Dispose();

            return 0;
        }
        #endregion //wobjectfindininventorybyname[string name]
        #region wobjectfindininventorybynamerx[string namerx]
        [ExpressionMethod("wobjectfindininventorybynamerx")]
        [ExpressionParameter(0, typeof(string), "namerx", "name regex to filter by")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject")]
        [Summary("Gets a worldobject representing the first inventory item matching name regex, or 0 if none was found")]
        [Example("wobjectfindininventorybynamerx[`Massive.*`]", "Returns a worldobject of the first inventory item that matches regex `Massive.*`")]
        public object Wobjectfindininventorybynamerx(string namerx) {
            var wos = UtilityBeltPlugin.Instance.Core.WorldFilter.GetInventory();
            var re = new Regex(namerx);
            foreach (var wo in wos) {
                if (re.IsMatch(wo.Name)) {
                    wos.Dispose();
                    return new Wobject(wo.Id);
                }
            }

            wos.Dispose();

            return 0;
        }
        #endregion //wobjectfindininventorybynamerx[string name]
        #region wobjectgetselection[]
        [ExpressionMethod("wobjectgetselection")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject representing the currently selected object, or 0 if none")]
        [Summary("Gets a worldobject representing the currently selected object")]
        [Example("wobjectgetselection[]", "Returns a worldobject representing the currently selected object")]
        public object Wobjectgetselection() {
            if (UtilityBeltPlugin.Instance.Core.Actions.IsValidObject(UtilityBeltPlugin.Instance.Core.Actions.CurrentSelection))
                return new Wobject(UtilityBeltPlugin.Instance.Core.Actions.CurrentSelection);
            return false;
        }
        #endregion //wobjectgetselection[]
        #region wobjectgetplayer[]
        [ExpressionMethod("wobjectgetplayer")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject representing the current player")]
        [Summary("Gets a worldobject representing the current player")]
        [Example("wobjectgetplayer[]", "Returns a worldobject representing the current player")]
        public object Wobjectgetplayer() {
            return new Wobject(UtilityBeltPlugin.Instance.Core.CharacterFilter.Id);
        }
        #endregion //wobjectgetplayer[worldobject wo]
        #region wobjectfindnearestbynameandobjectclass[int objectclass, string namerx]
        [ExpressionMethod("wobjectfindnearestbynameandobjectclass")]
        [ExpressionParameter(0, typeof(string), "objectclass", "objectclass to filter by")]
        [ExpressionParameter(1, typeof(string), "namerx", "name regex to filter by")]
        [ExpressionReturn(typeof(Wobject), "Returns a worldobject")]
        [Summary("Gets a worldobject representing the first object matching objectclass and name regex, or 0 if none was found")]
        [Example("wobjectfindnearestbynameandobjectclass[24,`Crash.*`]", "Returns a worldobject of the first object found matching objectlass 24 (player) and name regex `Crash.*`")]
        public object Wobjectfindnearestbynameandobjectclass(double objectClass, string namerx) {
            var wos = UtilityBeltPlugin.Instance.Core.WorldFilter.GetByObjectClass((ObjectClass)Convert.ToInt32(objectClass));
            var re = new Regex(namerx);
            foreach (var wo in wos) {
                if (wo.Id == UtilityBeltPlugin.Instance.Core.CharacterFilter.Id)
                    continue;
                if (re.IsMatch(wo.Name)) {
                    wos.Dispose();
                    return new Wobject(wo.Id);
                }
            }

            wos.Dispose();

            return 0;
        }
        #endregion //wobjectfindnearestbynameandobjectclass[int objectclass, string namerx]
        #endregion //WorldObjects
        #region Actions
        #region actiontryselect[wobject obj]
        [ExpressionMethod("actiontryselect")]
        [ExpressionParameter(0, typeof(string), "obj", "wobject to select")]
        [ExpressionReturn(typeof(double), "Returns 0")]
        [Summary("Attempts to select a worldobject")]
        [Example("actiontryselect[wobjectgetplayer[]]", "Attempts to select the current player")]
        public object Actiontryselect(Wobject obj) {
            UtilityBeltPlugin.Instance.Core.Actions.SelectItem(obj.Wo.Id);
            return 0;
        }
        #endregion //actiontryselect[wobject obj]
        #region actiontryuseitem[wobject obj]
        [ExpressionMethod("actiontryuseitem")]
        [ExpressionParameter(0, typeof(string), "obj", "wobject to try to use")]
        [ExpressionReturn(typeof(double), "Returns 0")]
        [Summary("Attempts to use a worldobject")]
        [Example("actiontryuseitem[wobjectgetplayer[]]", "Attempts to use the current player (opens backpack)")]
        public object Actiontryuseitem(Wobject obj) {
            UtilityBeltPlugin.Instance.Core.Actions.UseItem(obj.Wo.Id, 0);
            return 0;
        }
        #endregion //actiontryuseitem[wobject obj]
        #region actiontryapplyitem[wobject useObj, wobject onObj]
        [ExpressionMethod("actiontryapplyitem")]
        [ExpressionParameter(0, typeof(string), "useObj", "wobject to use first")]
        [ExpressionParameter(0, typeof(string), "onObj", "wobject to be used on")]
        [ExpressionReturn(typeof(double), "Returns 0 if failed, and 1 if it *could* succeed")]
        [Summary("Attempts to use a worldobject on another worldobject")]
        [Example("actiontryapplyitem[wobjectfindininventorybynamerx[`.* Healing Kit`],wobjectwobjectgetplayer[]]", "Attempts to use any healing kit in your inventory, on yourself")]
        public object Actiontryapplyitem(Wobject useObj, Wobject onObj) {
            if (UtilityBeltPlugin.Instance.Core.Actions.BusyState != 0)
                return 0;
            UtilityBeltPlugin.Instance.Core.Actions.ApplyItem(useObj.Wo.Id, onObj.Wo.Id);
            return 1;
        }
        #endregion //actiontryapplyitem[wobject useObj, wobject onObj]
        #region actiontrygiveitem[wobject item, wobject destination]
        [ExpressionMethod("actiontrygiveitem")]
        [ExpressionParameter(0, typeof(string), "give", "wobject to give")]
        [ExpressionParameter(0, typeof(string), "destination", "wobject to be given to")]
        [ExpressionReturn(typeof(double), "Returns 0 if failed, and 1 if it *could* succeed")]
        [Summary("Attempts to give a worldobject to another worlobject, like an npc")]
        [Example("actiontrygiveitem[wobjectfindininventorybynamerx[`.* Healing Kit`],wobjectgetselection[]]", "Attempts to to give any healing kit in your inventory to the currently selected object")]
        public object Actiontrygiveitem(Wobject give, Wobject destination) {
            if (UtilityBeltPlugin.Instance.Core.Actions.BusyState != 0)
                return 0;
            UtilityBeltPlugin.Instance.Core.Actions.GiveItem(give.Wo.Id, destination.Wo.Id);
            return 1;
        }
        #endregion //actiontrygiveitem[wobject item, wobject destination]
        #region actiontryequipanywand[]
        [ExpressionMethod("actiontryequipanywand")]
        [ExpressionReturn(typeof(double), "Returns 1 if a wand is already equipped, 0 otherwise")]
        [Summary("Attempts to take one step towards equipping any wand from the current profile's items list")]
        [Example("actiontryequipanywand[]", "Attempts to equip any wand")]
        public object Actiontryequipanywand() {
            return TryEquipAnyWand();
        }
        #endregion //actiontryequipanywand[]
        #region actiontrycastbyid[int spellId]
        [ExpressionMethod("actiontrycastbyid")]
        [ExpressionParameter(0, typeof(double), "spellId", "spellId to cast")]
        [ExpressionReturn(typeof(double), "Returns 1 if the attempt has begun, 0 if the attempt has not yet been made, or 2 if the attempt is impossible")]
        [Summary("Attempts to cast a spell by id. Checks spell requirements as if it were a vtank 'hunting' spell. If the character is not in magic mode, one step is taken towards equipping any wand")]
        [Example("actiontrycastbyid[2931]", "Attempts to cast Recall Aphus Lassel")]
        public object Actiontrycastbyid(double spellId) {
            var id = Convert.ToInt32(spellId);
            if (!Spells.HasSkillHunt(id))
                return 2;
            if (!TryEquipAnyWand())
                return 0;

            UtilityBeltPlugin.Instance.Core.Actions.CastSpell(id, 0);

            return 1;
        }
        #endregion //actiontrycastbyid[int spellId]
        #region actiontrycastbyidontarget[int spellId, wobject target]
        [ExpressionMethod("actiontrycastbyidontarget")]
        [ExpressionParameter(0, typeof(double), "spellId", "spellId to cast")]
        [ExpressionParameter(0, typeof(Wobject), "target", "target to cast on")]
        [ExpressionReturn(typeof(double), "Returns 1 if the attempt has begun, 0 if the attempt has not yet been made, or 2 if the attempt is impossible")]
        [Summary("Attempts to cast a spell by id on a worldobject. Checks spell requirements as if it were a vtank 'hunting' spell. If the character is not in magic mode, one step is taken towards equipping any wand")]
        [Example("actiontrycastbyidontarget[1,wobjectgetselection[]]", "Attempts to cast Strength Other, on your currently selected target")]
        public object Actiontrycastbyidontarget(double spellId, Wobject target) {
            var id = Convert.ToInt32(spellId);
            if (!Spells.HasSkillHunt(id))
                return 2;
            if (!TryEquipAnyWand())
                return 0;

            UtilityBeltPlugin.Instance.Core.Actions.CastSpell(id, target.Wo.Id);

            return 1;
        }
        #endregion //actiontrycastbyidontarget[int spellId, wobject target]
        #endregion //Actions
        #region HUDs
        #region statushud[string key, string value]
        [ExpressionMethod("statushud")]
        [ExpressionParameter(0, typeof(string), "key", "key to update")]
        [ExpressionParameter(0, typeof(string), "value", "value to update with")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Summary("Updates an entry in the Virindi HUDs Status HUD.")]
        [Example("statushud[test,my value]", "Updates the V Status Hud key `test` to `my value`")]
        public object Statushud(string key, string value) {
            // TODO: ub huds
            Type vhud = typeof(uTank2.PluginCore).Assembly.GetType("aw");
            var updateKeyMethod = vhud.GetMethod("b", BindingFlags.Public | BindingFlags.Static, null, CallingConventions.Standard, new Type[] { typeof(string), typeof(string), typeof(string) }, null);
            updateKeyMethod.Invoke(null, new object[] { "VTank Meta", key, value });
            return 1;
        }
        #endregion //statushud[string key, string value]
        #region statushudcolored[string key, string value, int color]
        [ExpressionMethod("statushudcolored")]
        [ExpressionParameter(0, typeof(string), "key", "key to update")]
        [ExpressionParameter(0, typeof(string), "value", "value to update with")]
        [ExpressionParameter(0, typeof(double), "color", "The color, in RGB number format. For example, pure red is 16711680 (0xFF0000)")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Summary("Updates an entry in the Virindi HUDs Status HUD with color")]
        [Example("statushudcolored[test,my value,16711680]", "Updates the V Status Hud key `test` to `my value` in red")]
        public object Statushudcolored(string key, string value, double color) {
            // TODO: ub huds
            Type vhud = typeof(uTank2.PluginCore).Assembly.GetType("aw");
            var updateKeyMethod = vhud.GetMethod("b", BindingFlags.Public | BindingFlags.Static, null, CallingConventions.Standard, new Type[] { typeof(string), typeof(string), typeof(string), typeof(Color) }, null);
            var c = Convert.ToInt32(color);
            var r = (0xFF0000 & c) >> 0x10;
            var g = (0x00FF00 & c) >> 0x08;
            var b = (0x0000FF & c) >> 0x00;
            updateKeyMethod.Invoke(null, new object[] { "VTank Meta", key, value, Color.FromArgb(0xFF, r, g, b)});
            
            return 1;
        }
        #endregion //statushudcolored[string key, string value, int color]
        #endregion //HUDs
        #region Misc
        #region isfalse[int value]
        [ExpressionMethod("isfalse")]
        [ExpressionParameter(0, typeof(double), "value", "value to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if value is false, 0 otherwise")]
        [Summary("Checks if a value is equal to false (0)")]
        [Example("isfalse[0]", "Checks that 0 is false, and returns true because it is")]
        public object Isfalse(double value) {
            return value.Equals(0);
        }
        #endregion //isfalse[int value]
        #region istrue[int value]
        [ExpressionMethod("istrue")]
        [ExpressionParameter(0, typeof(double), "value", "value to check")]
        [ExpressionReturn(typeof(double), "Returns 1 if value is true, 0 otherwise")]
        [Summary("Checks if a value is equal to true (1)")]
        [Example("istrue[1]", "Checks that 0 is true, and returns true because it is")]
        public object Istrue(double value) {
            return value.Equals(0);
        }
        #endregion //istrue[int value]
        #region iif[int value, object truevalue, object falsevalue]
        [ExpressionMethod("iif")]
        [ExpressionParameter(0, typeof(double), "value", "value to check")]
        [ExpressionParameter(0, typeof(object), "truevalue", "value to return if value is true")]
        [ExpressionParameter(0, typeof(object), "falsevalue", "value to return if value is false")]
        [ExpressionReturn(typeof(double), "Returns 1 if value is true, 0 otherwise")]
        [Summary("Checks if the first parameter is true, if so returns the second argument.  If the first parameter is false or not a number, returns the second argument")]
        [Example("iif[1,2,3]", "Returns 2 (second param) because 1 (first param) is true")]
        public object Iif(object value, object truevalue, object falsevalue) {
            return (value.GetType() == typeof(double) && value.Equals(1)) ? truevalue : falsevalue;
        }
        #endregion //iif[int value, any truevalue, any falsevalue]
        #region randint[int min, int max]
        [ExpressionMethod("randint")]
        [ExpressionParameter(0, typeof(double), "min", "minimum value to return")]
        [ExpressionParameter(0, typeof(double), "max", "maximum value to return (-1)")]
        [ExpressionReturn(typeof(double), "Returns a number between min and (max-1)")]
        [Summary("Generates a random number between min and (max-1)")]
        [Example("randint[0,2]", "Returns a random number, 0 or 1, but not 2")]
        public object Randint(double min, double max) {
            return rnd.Next(Convert.ToInt32(min), Convert.ToInt32(max));
        }
        #endregion //randint[int min, int max]
        #region cstr[int number]
        [ExpressionMethod("cstr")]
        [ExpressionParameter(0, typeof(double), "number", "number to convert")]
        [ExpressionReturn(typeof(string), "Returns a string representation of number")]
        [Summary("Converts a number to a string")]
        [Example("cstr[2]", "Returns a string of `2`")]
        public object Cstr(double number) {
            return number.ToString();
        }
        #endregion //cstr[int number]
        #region strlen[string tocheck]
        [ExpressionMethod("strlen")]
        [ExpressionParameter(0, typeof(double), "tocheck", "string to check")]
        [ExpressionReturn(typeof(double), "Returns the length of tocheck string")]
        [Summary("Gets the length of a string")]
        [Example("strlen[test]", "Returns a length of 4")]
        public object Strlen(string tocheck) {
            return tocheck.Length;
        }
        #endregion //strlen[string tocheck]
        #region getobjectinternaltype[object tocheck]
        [ExpressionMethod("getobjectinternaltype")]
        [ExpressionParameter(0, typeof(double), "tocheck", "object to check")]
        [ExpressionReturn(typeof(double), "Values are: 0=none, 1=number, 3=string, 7=object")]
        [Summary("Gets internal type of an object, as a number")]
        [Example("getobjectinternaltype[test]", "Returns a length of 4")]
        public object Getobjectinternaltype(object tocheck) {
            if (tocheck == null)
                return 0;
            if (tocheck.GetType() == typeof(double))
                return 1;
            if (tocheck.GetType() == typeof(string))
                return 3;

            return 7;
        }
        #endregion //getobjectinternaltype[object tocheck]
        #region cstrf[int number, string format]
        [ExpressionMethod("cstrf")]
        [ExpressionParameter(0, typeof(double), "number", "number to convert")]
        [ExpressionParameter(1, typeof(string), "format", "string format. See: http://msdn.microsoft.com/en-us/library/kfsatb94.aspx")]
        [ExpressionReturn(typeof(string), "formatted string, from a number")]
        [Summary("Converts a number to a string using a specified format")]
        [Example("cstrf[3.14159,`N3`]", "Formats 3.14159 to a string with 3 decimal places")]
        public object Cstrf(double number, string format) {
            return number.ToString(format);
        }
        #endregion //cstrf[int number, string format]
        #region cnumber[string number]
        [ExpressionMethod("cnumber")]
        [ExpressionParameter(0, typeof(string), "number", "string number to convert")]
        [ExpressionReturn(typeof(double), "floating point number from a string")]
        [Summary("Converts a string to a floating point number")]
        [Example("cnumber[`3.14159`]", "Converts `3.14159` to a number")]
        public object Cnumber(string number) {
            if (Double.TryParse(number, out double result))
                return result;
            return 0;
        }
        #endregion //cnumber[string number]
        #region floor[int number]
        [ExpressionMethod("floor")]
        [ExpressionParameter(0, typeof(double), "number", "number to floot")]
        [ExpressionReturn(typeof(double), "Returns a whole number")]
        [Summary("returns the largest integer less than or equal to a given number.")]
        [Example("floor[3.14159]", "returns 3")]
        public object Floor(double number) {
            return Math.Floor(number);
        }
        #endregion //floor[int number]
        #region ceiling[int number]
        [ExpressionMethod("ceiling")]
        [ExpressionParameter(0, typeof(double), "number", "number to ceil")]
        [ExpressionReturn(typeof(double), "Returns a whole number")]
        [Summary("rounds a number up to the next largest whole number or integer")]
        [Example("ceiling[3.14159]", "returns 3")]
        public object Ceiling(double number) {
            return Math.Ceiling(number);
        }
        #endregion //ceiling[int number]
        #region round[int number]
        [ExpressionMethod("round")]
        [ExpressionParameter(0, typeof(double), "number", "number to round")]
        [ExpressionReturn(typeof(double), "Returns a whole number")]
        [Summary("returns the value of a number rounded to the nearest integer")]
        [Example("round[3.14159]", "returns 3")]
        public object Round(double number) {
            return Math.Round(number);
        }
        #endregion //round[int number]
        #region abs[int number]
        [ExpressionMethod("abs")]
        [ExpressionParameter(0, typeof(double), "number", "number to get absolute value of")]
        [ExpressionReturn(typeof(double), "Returns a positive number")]
        [Summary("Returns the absolute value of a number")]
        [Example("abs[-3.14159]", "returns 3.14159")]
        public object Abs(double number) {
            return Math.Abs(number);
        }
        #endregion //abs[int number]
        #region vtsetmetastate[string state]
        [ExpressionMethod("vtsetmetastate")]
        [ExpressionParameter(0, typeof(string), "state", "new state to switch to")]
        [ExpressionReturn(typeof(double), "Returns 1")]
        [Summary("Changes the current vtank meta state")]
        [Example("vtsetmetastate[myState]", "sets vtank meta state to `myState`")]
        public object Vtsetmetastate(string state) {
            Util.Decal_DispatchOnChatCommand($"/vt setmetastate {state}");
            return 1;
        }
        #endregion //vtsetmetastate[string state]
        #endregion //Misc
        #region Stopwatch
        #region stopwatchcreate[]
        [ExpressionMethod("stopwatchcreate")]
        [ExpressionReturn(typeof(Stopwatch), "Returns a stopwatch object")]
        [Summary("Creates a new stopwatch object.  The stopwatch object is stopped by default.")]
        [Example("stopwatchcreate[]", "returns a new stopwatch")]
        public object Stopwatchcreate() {
            return new Stopwatch();
        }
        #endregion //stopwatchcreate[]
        #region stopwatchstart[stopwatch watch]
        [ExpressionMethod("stopwatchstart")]
        [ExpressionParameter(0, typeof(Stopwatch), "watch", "stopwatch to start")]
        [ExpressionReturn(typeof(Stopwatch), "Returns a stopwatch object")]
        [Summary("Starts a stopwatch if not already started")]
        [Example("stopwatchstart[stopwatchcreate[]]", "starts a new stopwatch")]
        public object Stopwatchstart(Stopwatch watch) {
            watch.Start();
            return watch;
        }
        #endregion //stopwatchstart[stopwatch watch]
        #region stopwatchstop[stopwatch watch]
        [ExpressionMethod("stopwatchstop")]
        [ExpressionParameter(0, typeof(Stopwatch), "watch", "stopwatch to stop")]
        [ExpressionReturn(typeof(Stopwatch), "Returns a stopwatch object")]
        [Summary("Stops a stopwatch if not already stopped")]
        [Example("stopwatchstop[stopwatchcreate[]]", "stops a new stopwatch")]
        public object Stopwatchstop(Stopwatch watch) {
            watch.Stop();
            return watch;
        }
        #endregion //stopwatchstart[stopwatch watch]
        #region stopwatchelapsedseconds[stopwatch watch]
        [ExpressionMethod("stopwatchelapsedseconds")]
        [ExpressionParameter(0, typeof(Stopwatch), "watch", "stopwatch to check")]
        [ExpressionReturn(typeof(Stopwatch), "Returns a int elapsed seconds")]
        [Summary("Gets the amount of seconds a stopwatch has been running for")]
        [Example("stopwatchelapsedseconds[stopwatchcreate[]]", "Returns the amount of a seconds a new stopwatch has been running for (0 obv)")]
        public object Stopwatchelapsedseconds(Stopwatch watch) {
            return watch.Elapsed();
        }
        #endregion //stopwatchelapsedseconds[stopwatch watch]
        #endregion //Stopwatch
        #endregion //Expressions

        private bool isFixingPortalLoops = false;
        private int portalExitCount = 0;
        private int lastPortalExitLandcell = 0;

        public VTankControl(UtilityBeltPlugin ub, string name) : base(ub, name) {
            DoVTankPatches();

            if (UB.Core.CharacterFilter.LoginStatus != 0) Enable();
            else UB.Core.CharacterFilter.LoginComplete += CharacterFilter_LoginComplete;

            PropertyChanged += VTankControl_PropertyChanged;

            if (FixPortalLoops) {
                UB.Core.CharacterFilter.ChangePortalMode += CharacterFilter_ChangePortalMode;
                isFixingPortalLoops = true;
            }
        }

        class ExpressionErrorListener : DefaultErrorStrategy {
            public override void ReportError(Parser recognizer, RecognitionException e) {
                throw new Exception($"Expression Error: {e.Message} @ char position {e.OffendingToken.Column}");
            }
        }

        public object EvaluateExpression(string expression, bool silent=false) {
            try {
                // we call AddChatText directly instead of Util.WriteToChat so it doesnt feed the message to vtank
                if (!silent)
                    UtilityBeltPlugin.Instance.Core.Actions.AddChatText($"[UB] Evaluating expression: \"{expression}\"", 5);

                AntlrInputStream inputStream = new AntlrInputStream(expression);
                MetaExpressionsLexer spreadsheetLexer = new MetaExpressionsLexer(inputStream);
                CommonTokenStream commonTokenStream = new CommonTokenStream(spreadsheetLexer);
                MetaExpressionsParser expressionParser = new MetaExpressionsParser(commonTokenStream);
                expressionParser.ErrorHandler = new ExpressionErrorListener();
                MetaExpressionsParser.ParseContext parseContext = expressionParser.parse();
                ExpressionVisitor visitor = new ExpressionVisitor();

                return visitor.Visit(parseContext);
            }
            catch (Exception ex) {
                Logger.LogException(ex);
                Logger.Error(ex.Message.ToString());
            }

            return 0;
        }

        #region VTank Patches
        private void DoVTankPatches() {
            try {
                UBHelper.vTank.UnpatchVTankExpressions();
                if (!PatchExpressionEngine)
                    return;
                UBHelper.vTank.PatchVTankExpressions(new UBHelper.vTank.Del_EvaluateExpression(EvaluateExpression));
            }
            catch (Exception ex) { Logger.LogException(ex); Logger.Error(ex.ToString()); }
        }
        #endregion

        #region Helpers
        private WorldObject GetCharacter() {
            return UtilityBeltPlugin.Instance.Core.WorldFilter[UtilityBeltPlugin.Instance.Core.CharacterFilter.Id];
        }
        private bool TryEquipAnyWand() {
            //TODO: implement this fully in ub
            FieldInfo fieldInfo = uTank2.PluginCore.PC.GetType().GetField("dz", BindingFlags.NonPublic | BindingFlags.Static);
            var dz = fieldInfo.GetValue(uTank2.PluginCore.PC);
            FieldInfo ofieldInfo = dz.GetType().GetField("o", BindingFlags.Public | BindingFlags.Instance);
            var o = ofieldInfo.GetValue(dz);
            var method = o.GetType().GetMethod("a", BindingFlags.Public | BindingFlags.Instance, null, CallingConventions.Standard, new Type[] { typeof(CombatState), typeof(int), typeof(bool) }, null);
            return (bool)method.Invoke(o, new object[] { CombatState.Magic, 0, true });
        }
        #endregion

        private void VTankControl_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName.Equals("FixPortalLoops")) {
                if (FixPortalLoops && !isFixingPortalLoops) {
                    UB.Core.CharacterFilter.ChangePortalMode += CharacterFilter_ChangePortalMode;
                    isFixingPortalLoops = true;
                }
                else if (!FixPortalLoops && isFixingPortalLoops) {
                    UB.Core.CharacterFilter.ChangePortalMode -= CharacterFilter_ChangePortalMode;
                    isFixingPortalLoops = false;
                }
            }

            if (e.PropertyName.Equals("PatchExpressionEngine")) {
                DoVTankPatches();
            }
        }

        private void CharacterFilter_ChangePortalMode(object sender, Decal.Adapter.Wrappers.ChangePortalModeEventArgs e) {
            try {
                if (e.Type != Decal.Adapter.Wrappers.PortalEventType.ExitPortal)
                    return;

                if (lastPortalExitLandcell == UB.Core.Actions.Landcell) {
                    portalExitCount++;
                }
                else {
                    portalExitCount = 1;
                    lastPortalExitLandcell = UB.Core.Actions.Landcell;
                }
                
                if (portalExitCount >= PortalLoopCount) {
                    DoPortalLoopFix();
                    return;
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void CharacterFilter_LoginComplete(object sender, EventArgs e) {
            try {
                UB.Core.CharacterFilter.LoginComplete -= CharacterFilter_LoginComplete;
                Enable();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        public void Enable() {
            UBHelper.vTank.Enable();
            UB.Core.CharacterFilter.Logoff += CharacterFilter_Logoff;
        }

        private void CharacterFilter_Logoff(object sender, Decal.Adapter.Wrappers.LogoffEventArgs e) {
            try {
                if (e.Type == Decal.Adapter.Wrappers.LogoffEventType.Authorized)
                    UBHelper.vTank.Disable();
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        private void DoPortalLoopFix() {
            Util.WriteToChat($"Nav: {UBHelper.vTank.Instance.NavCurrent}");
            Util.DispatchChatToBoxWithPluginIntercept($"/vt nav save {VTNavRoute.NoneNavName}");
            UBHelper.vTank.Instance.NavDeletePoint(0);
        }

        protected override void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    UB.Core.CharacterFilter.LoginComplete -= CharacterFilter_LoginComplete;
                    UB.Core.CharacterFilter.Logoff -= CharacterFilter_Logoff;
                    
                    if (isFixingPortalLoops)
                        UB.Core.CharacterFilter.ChangePortalMode -= CharacterFilter_ChangePortalMode;
                    if (PatchExpressionEngine)
                        UBHelper.vTank.UnpatchVTankExpressions();

                    base.Dispose(disposing);
                }
                disposedValue = true;
            }
        }
    }
}
