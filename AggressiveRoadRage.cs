using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FiveFR._API.Attributes;
using FiveFR._API.Classes;
using FiveFR._API.Extensions;
using FiveFR._API.Services;

namespace GreatCallouts_FiveFR;

[Guid("B1C2D3E4-F5A6-7890-1234-567890123456")]
[AddonProperties("Aggressive Road Rage", "^3DevKilo", "1.0")]
public class AggressiveRoadRage : Callout
{
    static Random rnd = new Random();
    private Vehicle _vehicle1;
    private Vehicle _vehicle2;
    private Ped _driver1;
    private Ped _driver2;
    private Blip _blip1;
    private Blip _blip2;
    private bool _pursuitTriggered;
    private bool _hasReactedToStop;

    private RelationshipGroup _group1;
    private RelationshipGroup _group2;

    public AggressiveRoadRage()
    {
        _group1 = new RelationshipGroup(API.GetHashKey("RAGE_DRIVER_1"));
        _group2 = new RelationshipGroup(API.GetHashKey("RAGE_DRIVER_2"));

        if (CalloutConfig.AggressiveRoadRageConfig.FixedLocation &&
            CalloutConfig.AggressiveRoadRageConfig.Locations.Any())
            InitInfo(CalloutConfig.AggressiveRoadRageConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());

        ShortName = "Aggressive Road Rage";
        CalloutDescription =
            "911 Report: Two vehicles are engaged in a high-speed duel, ramming each other and endangering traffic. Intercept immediately.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.AggressiveRoadRageConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.AggressiveRoadRageConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.AggressiveRoadRageConfig.MinSpawnDistance,
            CalloutConfig.AggressiveRoadRageConfig.MaxSpawnDistance);
        var offsetX = rnd.Next(-1 * distance, distance);
        var offsetY = rnd.Next(-1 * distance, distance);
        return World.GetNextPositionOnStreet(Game.PlayerPed.GetOffsetPosition(new Vector3(offsetX, offsetY, 0)));
    }

    private PedHash GetRandomPedHash()
    {
        var pedHashes = Enum.GetValues(typeof(PedHash)).Cast<PedHash>().Where(p => API.IsModelAPed((uint)p)).ToArray();
        PedHash selectedHash;
        var mathPed = -1;
        do
        {
            selectedHash = pedHashes[rnd.Next(pedHashes.Length)];
            if (mathPed > -1)
            {
                API.DeleteEntity(ref mathPed);
                mathPed = -1;
            }

            mathPed = API.CreatePed(0, (uint)selectedHash, Location.X, Location.Y, Location.Z, 0f, false, true);
        } while (!API.IsPedHuman(mathPed));

        if (mathPed > -1)
            API.DeleteEntity(ref mathPed);

        return selectedHash;
    }

    private VehicleHash GetRandomVehicleHash()
    {
        var vehicleHashes = Enum.GetValues(typeof(VehicleHash)).Cast<VehicleHash>()
            .Where(v => API.IsModelAVehicle((uint)v)).ToArray();
        VehicleHash selectedHash;
        do
        {
            selectedHash = vehicleHashes[rnd.Next(vehicleHashes.Length)];
        } while (!(new Model(selectedHash) is Model { IsCar: false, IsValid: true } model &&
                   API.GetVehicleClassFromName((uint)model.Hash) < 13));

        return selectedHash;
    }

    public override async Task OnAccept()
    {
        InitBlip();

        Vector3 spawnPos = Location;
        float heading = rnd.Next(0, 360);

        // Spawn Vehicle 1
        _vehicle1 = await SpawnVehicle(GetRandomVehicleHash(), spawnPos, heading);
        _driver1 = await SpawnPed(GetRandomPedHash(), spawnPos, heading);
        if (_driver1 is not null && _vehicle1 is not null)
        {
            _driver1.SetIntoVehicle(_vehicle1, VehicleSeat.Driver);
            _driver1.BlockPermanentEvents = true;
            _driver1.AlwaysKeepTask = true;
            _driver1.RelationshipGroup = _group1;
        }

        // Spawn Vehicle 2 slightly behind or ahead
        Vector3 offset = spawnPos + new Vector3(rnd.Next(-10, 10), rnd.Next(-10, 10), 0);
        _vehicle2 = await SpawnVehicle(GetRandomVehicleHash(), offset, heading);
        _driver2 = await SpawnPed(GetRandomPedHash(), offset, heading);
        if (_driver2 is not null && _vehicle2 is not null)
        {
            _driver2.SetIntoVehicle(_vehicle2, VehicleSeat.Driver);
            _driver2.BlockPermanentEvents = true;
            _driver2.AlwaysKeepTask = true;
            _driver2.RelationshipGroup = _group2;
        }

        // Set hostility
        _group1.SetRelationshipBetweenGroups(_group2, Relationship.Hate, true);
        _group2.SetRelationshipBetweenGroups(_group1, Relationship.Hate, true);

        // Initial behavior: Chase each other
        if (_driver1 is not null && _driver2 is not null)
        {
            // Driver 1 chases Driver 2
            _driver1.Task.VehicleChase(_driver2);
            // Driver 2 flees or chases back (randomize for variety)
            if (rnd.Next(2) == 0)
                _driver2.Task.VehicleChase(_driver1);
            else
                _driver2.Task.FleeFrom(_driver1);
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_vehicle1 is not null && _vehicle1.Exists())
        {
            _blip1 = _vehicle1.AttachBlip();
            _blip1.Name = "Road Rage Suspect 1";
            _blip1.Sprite = BlipSprite.PersonalVehicleCar;
            _blip1.Color = BlipColor.Red;
        }

        if (_vehicle2 is not null && _vehicle2.Exists())
        {
            _blip2 = _vehicle2.AttachBlip();
            _blip2.Name = "Road Rage Suspect 2";
            _blip2.Sprite = BlipSprite.PersonalVehicleCar;
            _blip2.Color = BlipColor.Red;
        }

        if (_vehicle1 is null || _vehicle2 is null)
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Logic loop
        await QueueService.Predicate(() =>
        {
            if (_vehicle1 is null || !_vehicle1.Exists() || _vehicle2 is null || !_vehicle2.Exists()) return false;
            if (_driver1 is null || !_driver1.IsAlive || _driver2 is null || !_driver2.IsAlive) return false;

            // Check if player is close enough to intervene
            if (!_pursuitTriggered && Game.PlayerPed.Position.DistanceToSquared(_vehicle1.Position) < 2500.0f) // 50m
            {
                _pursuitTriggered = true;
            }

            // If one stops, the other might ram or flee
            if (!_hasReactedToStop)
            {
                if (_vehicle1.Speed < 1.0f && _vehicle2.Speed > 5.0f)
                {
                    _hasReactedToStop = true;
                    if (rnd.Next(2) == 0)
                        _driver2.Task.VehicleChase(_driver1); // Ram
                    else
                        _driver2.Task.FleeFrom(Game.PlayerPed); // Flee
                }
                else if (_vehicle2.Speed < 1.0f && _vehicle1.Speed > 5.0f)
                {
                    _hasReactedToStop = true;
                    if (rnd.Next(2) == 0)
                        _driver1.Task.VehicleChase(_driver2); // Ram
                    else
                        _driver1.Task.FleeFrom(Game.PlayerPed); // Flee
                }
            }

            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        _blip1?.Delete();
        _blip2?.Delete();
        _group1?.Remove();
        _group2?.Remove();

        base.OnCancelBefore();
    }
}