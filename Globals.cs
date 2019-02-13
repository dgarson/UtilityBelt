﻿using System;

using Decal.Adapter;
using Decal.Adapter.Wrappers;
using UtilityBelt.Views;

namespace UtilityBelt
{
	public static class Globals {
		public static void Init(string pluginName, PluginHost host, CoreManager core) {
			PluginName = pluginName;

			Host = host;

			Core = core;
        }

		public static string PluginName { get; private set; }

		public static PluginHost Host { get; private set; }

        public static CoreManager Core { get; private set; }

        public static MainView View { get; set; }
        public static Config Config { get; set; }
    }
}
