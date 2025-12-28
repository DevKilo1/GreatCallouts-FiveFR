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

[Guid("C3D4E5F6-A7B8-9012-3456-789012CDEFAB")]
[AddonProperties("Stolen Vehicle", "^3DevKilo", "1.0")]
public class StolenVehicle : Callout
{
    static Random rnd = new Random();
    private Vehicle _vehicle;
    private List<Ped> _suspects = new();
    private StolenVehicleScenario _scenario;
    private bool _willRam;
    private bool _isImpersonating;
    private Vehicle _targetVehicle;
    private bool _pursuitTriggered;

    private enum StolenVehicleScenario
    {
        Joyride,
        FelonyStop,
        EmergencyJoyride
    }

    public StolenVehicle()
    {
        if (CalloutConfig.StolenVehicleConfig.FixedLocation && CalloutConfig.StolenVehicleConfig.Locations.Any())
            InitInfo(CalloutConfig.StolenVehicleConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());

        ShortName = "Stolen Vehicle";
        CalloutDescription =
            "ANPR Hit: A vehicle reported stolen has been detected in the area. Intercept and investigate.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.StolenVehicleConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.StolenVehicleConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.StolenVehicleConfig.MinSpawnDistance,
            CalloutConfig.StolenVehicleConfig.MaxSpawnDistance);
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
        var vehicleHashes = Enum.GetValues(typeof(VehicleHash)).Cast<VehicleHash>().Where(v => API.IsModelAVehicle((uint)v)).ToArray();
        VehicleHash selectedHash;
        do
        {
            selectedHash = vehicleHashes[rnd.Next(vehicleHashes.Length)];
        } while (!(new Model(selectedHash) is Model { IsCar: false, IsValid: true } model &&
                   API.GetVehicleClassFromName((uint)model.Hash) < 13));
        return selectedHash;
    }

    private VehicleHash GetRandomEmergencyVehicleHash()
    {
        var hashes = new[] { VehicleHash.FireTruk, VehicleHash.Ambulance, VehicleHash.Police, VehicleHash.Police2, VehicleHash.Police3, VehicleHash.Sheriff };
        return hashes[rnd.Next(hashes.Length)];
    }

    public override async Task OnAccept()
    {
        InitBlip();

        // Pick a random scenario
        var values = Enum.GetValues(typeof(StolenVehicleScenario));
        _scenario = (StolenVehicleScenario)values.GetValue(rnd.Next(values.Length));

        VehicleHash hash;
        if (_scenario == StolenVehicleScenario.EmergencyJoyride)
            hash = GetRandomEmergencyVehicleHash();
        else
            hash = GetRandomVehicleHash();

        _vehicle = await SpawnVehicle(hash, Location, rnd.Next(0, 360));

        if (_vehicle is not null)
        {
            _vehicle.IsStolen = true;
            
            if (_scenario == StolenVehicleScenario.EmergencyJoyride)
            {
                _vehicle.IsSirenActive = true;
                if (rnd.Next(2) == 0) _willRam = true;
                else _isImpersonating = true;
            }

            // Spawn driver
            var driver = await SpawnPed(GetRandomPedHash(), Location, 0);
            if (driver is not null)
            {
                driver.SetIntoVehicle(_vehicle, VehicleSeat.Driver);
                _suspects.Add(driver);

                if (_scenario == StolenVehicleScenario.Joyride)
                {
                    // Drive normally until spotted
                    driver.Task.CruiseWithVehicle(_vehicle, 20f, 786603); // Driving style: Normal
                }
                else if (_scenario == StolenVehicleScenario.FelonyStop)
                {
                    // Drive aggressively
                    driver.Task.CruiseWithVehicle(_vehicle, 40f, 1074528293); // Driving style: Rushed
                }
                else if (_scenario == StolenVehicleScenario.EmergencyJoyride)
                {
                    if (_isImpersonating)
                    {
                        // Try to find a vehicle to chase
                        var vehicles = World.GetAllVehicles().Where(v => v != _vehicle && v.Driver is not null && v.Driver.IsAlive).ToArray();
                        if (vehicles.Any())
                        {
                            _targetVehicle = vehicles[rnd.Next(vehicles.Length)];
                            driver.Task.VehicleChase(_targetVehicle.Driver);
                        }
                        else
                        {
                            // Fallback to reckless driving
                            driver.Task.CruiseWithVehicle(_vehicle, 50f, 1074528293);
                        }
                    }
                    else
                    {
                        // Just drive recklessly for now, ram logic in OnStart
                        driver.Task.CruiseWithVehicle(_vehicle, 50f, 1074528293);
                    }
                }
            }
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_vehicle is not null)
        {
            var blip = _vehicle.AttachBlip();
            blip.Name = "Stolen Vehicle";
            blip.Sprite = BlipSprite.PersonalVehicleCar;

            if (_suspects.Any())
            {
                foreach (var s in _suspects)
                {
                    s.AlwaysKeepTask = true;
                    s.BlockPermanentEvents = true;
                }
            }
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Logic loop
        await QueueService.Predicate(() =>
        {
            if (_vehicle is null || !_vehicle.Exists()) return false;

            // Pursuit logic
            if (_suspects.All(s => !s.IsAlive || s.IsCuffed)) return false;

            // Trigger pursuit if player is close
            if (!_pursuitTriggered && Game.PlayerPed.Position.DistanceToSquared(_vehicle.Position) < 2500.0f) // 50m
            {
                _pursuitTriggered = true;

                if (_scenario == StolenVehicleScenario.Joyride)
                {
                    foreach (var s in _suspects)
                    {
                        if (s.IsInVehicle(_vehicle))
                            s.Task.FleeFrom(Game.PlayerPed);
                        else
                            s.Task.FleeFrom(Game.PlayerPed);
                    }
                }
                else if (_scenario == StolenVehicleScenario.FelonyStop)
                {
                    foreach (var s in _suspects)
                    {
                        if (s.IsInVehicle(_vehicle))
                            s.Task.VehicleChase(Game.PlayerPed);
                        else
                            s.Task.FightAgainst(Game.PlayerPed);
                    }
                }
                else if (_scenario == StolenVehicleScenario.EmergencyJoyride)
                {
                    if (_willRam)
                    {
                        foreach (var s in _suspects)
                        {
                            if (s.IsInVehicle(_vehicle))
                                s.Task.VehicleChase(Game.PlayerPed);
                        }
                    }
                    else if (_isImpersonating)
                    {
                        // Switch to fleeing if player intervenes
                        foreach (var s in _suspects)
                        {
                            if (s.IsInVehicle(_vehicle))
                                s.Task.FleeFrom(Game.PlayerPed);
                        }
                    }
                    else
                    {
                        foreach (var s in _suspects)
                        {
                            if (s.IsInVehicle(_vehicle))
                                s.Task.FleeFrom(Game.PlayerPed);
                        }
                    }
                }
            }

            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        if (_vehicle is not null && _vehicle.Exists())
            _vehicle.AttachedBlip?.Delete();

        foreach (var s in _suspects)
        {
            if (s is not null && s.Exists())
                s.AttachedBlip?.Delete();
        }

        base.OnCancelBefore();
    }
}