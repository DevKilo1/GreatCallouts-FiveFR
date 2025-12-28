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

    public static class MutualCombatConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("MutualCombat");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static int MinSpawnDistance = Config.EnsureValue("MinSpawnDistance", 50);
        public static int MaxSpawnDistance = Config.EnsureValue("MaxSpawnDistance", 150);
        public static float StartDistance = Config.EnsureValue("StartDistance", 100f);
    }

    public static class RecklessDriverConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("RecklessDriver");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static int MinSpawnDistance = Config.EnsureValue("MinSpawnDistance", 300);
        public static int MaxSpawnDistance = Config.EnsureValue("MaxSpawnDistance", 800);
        public static float StartDistance = Config.EnsureValue("StartDistance", 200f);
    }

    public static class BrandishingConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("Brandishing");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static int MinSpawnDistance = Config.EnsureValue("MinSpawnDistance", 100);
        public static int MaxSpawnDistance = Config.EnsureValue("MaxSpawnDistance", 300);
        public static float StartDistance = Config.EnsureValue("StartDistance", 150f);
    }

    public static class PublicIntoxicationConfig
    {
        static DataContainer<JObject> Config = _config.Ensure<JObject>("PublicIntoxication");

        public static bool Enabled = Config.EnsureValue("Enabled", true);
        public static int MinSpawnDistance = Config.EnsureValue("MinSpawnDistance", 50);
        public static int MaxSpawnDistance = Config.EnsureValue("MaxSpawnDistance", 150);
        public static float StartDistance = Config.EnsureValue("StartDistance", 100f);
    }
}
