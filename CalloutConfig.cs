using System;
using System.Collections.Generic;
using System.Linq;
using CitizenFX.Core;
using FiveFR._API.Classes;
using FiveFR._API.Extensions;
using Newtonsoft.Json.Linq;

namespace GreatCallouts_FiveFR;

public static class CalloutConfig
{
    static Configuration<JObject> _config = new("addons/callouts/GreatCalloutsByKilo/config.json");


    public static class HotboxConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("Hotbox");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static bool FixedLocation = Config.EnsureValue("FixedLocation", false);
        

        public static List<Vector3> Locations
        {
            get
            {
                var locations = new List<Vector3>();
                try
                {
                    locations = Config.Ensure<JArray>("Locations").GetData().Select(v => ((JObject)v).ToVector3())
                        .ToList();
                }
                catch (Exception)
                {
                    FixedLocation = false;
                }

                return locations;
            }
        }
        
        public static float StartRadius = Config.EnsureValue("StartRadius", 100f);
        
    }
    
    public static class ShotsFiredConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("ShotsFired");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static bool FixedLocation = Config.EnsureValue("FixedLocation", false);


        public static List<Vector3> Locations
        {
            get
            {
                var locations = new List<Vector3>();
                try
                {
                    locations = Config.Ensure<JArray>("Locations").GetData().Select(v => ((JObject)v).ToVector3())
                        .ToList();
                }
                catch (Exception)
                {
                    FixedLocation = false;
                }

                return locations;
            }
        }

        public static int MinSpawnDistance = Config.EnsureValue("MinSpawnDistance", 200);
        public static int MaxSpawnDistance = Config.EnsureValue("MaxSpawnDistance", 750);
        public static int MinSuspects = Config.EnsureValue("MinSuspects", 1);
        public static int MaxSuspects = Config.EnsureValue("MaxSuspects", 8);
        public static float StartDistance = Config.EnsureValue("StartDistance", 200f);

    }
}

