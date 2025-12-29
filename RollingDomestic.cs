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

[Guid("D4E5F6A7-B890-1234-5678-9012CDEFAB12")]
[AddonProperties("Rolling Domestic", "^3DevKilo^7", "1.0")]
public class RollingDomestic : Callout
{
    static Random rnd = new Random();
    private Vehicle _vehicle;
    private Ped _driver;
    private Ped _passenger;
    private RollingDomesticScenario _scenario;

    private enum RollingDomesticScenario
    {
        Argument,
        Ejection,
        Assault
    }

    public RollingDomestic()
    {
        if (CalloutConfig.RollingDomesticConfig.FixedLocation && CalloutConfig.RollingDomesticConfig.Locations.Any())
            InitInfo(CalloutConfig.RollingDomesticConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());

        ShortName = "Rolling Domestic";
        CalloutDescription = "911 Report: Violent dispute reported inside a moving vehicle. Caller states passengers are fighting.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.RollingDomesticConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.RollingDomesticConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.RollingDomesticConfig.MinSpawnDistance,
            CalloutConfig.RollingDomesticConfig.MaxSpawnDistance);
        var offsetX = rnd.Next(-1 * distance, distance);
        var offsetY = rnd.Next(-1 * distance, distance);
        return World.GetNextPositionOnStreet(Game.PlayerPed.GetOffsetPosition(new Vector3(offsetX, offsetY, 0)));
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

    public override async Task OnAccept()
    {
        InitBlip();

        // Pick a random scenario
        var values = Enum.GetValues(typeof(RollingDomesticScenario));
        _scenario = (RollingDomesticScenario)values.GetValue(rnd.Next(values.Length));

        var hash = GetRandomVehicleHash();
        _vehicle = await SpawnVehicle(hash, Location, rnd.Next(0, 360));

        if (_vehicle is not null)
        {
            // Spawn driver
            _driver = await SpawnPed(PedHash.Abigail, Location, 0); // Placeholder hash
            if (_driver is not null)
            {
                _driver.SetIntoVehicle(_vehicle, VehicleSeat.Driver);
                // Erratic driving
                _driver.Task.CruiseWithVehicle(_vehicle, 25f, 786603); // Driving style: Normal but we will tweak it
            }

            // Spawn passenger
            _passenger = await SpawnPed(PedHash.Bevhills01AFM, Location, 0); // Placeholder hash
            if (_passenger is not null)
            {
                _passenger.SetIntoVehicle(_vehicle, VehicleSeat.Passenger);
            }
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_vehicle is not null && _vehicle.Exists())
        {
            var blip = _vehicle.AttachBlip();
            blip.Name = "Suspect Vehicle";
            blip.Sprite = BlipSprite.PersonalVehicleCar;
            blip.Color = BlipColor.Red;

            NotificationService.ShowNetworkedNotification("Dispatch: Vehicle located. Occupants appear to be arguing violently.", "Dispatch");

            if (_driver is not null)
            {
                _driver.AlwaysKeepTask = true;
                _driver.BlockPermanentEvents = true;
                var dBlip = _driver.AttachBlip();
                dBlip.Name = "Driver";
                dBlip.Scale = 0.7f;
            }
            if (_passenger is not null)
            {
                _passenger.AlwaysKeepTask = true;
                _passenger.BlockPermanentEvents = true;
                var pBlip = _passenger.AttachBlip();
                pBlip.Name = "Passenger";
                pBlip.Scale = 0.7f;
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
            if (_driver is null || !_driver.IsAlive)
            {
                if (_driver is not null && _driver.AttachedBlip is not null) _driver.AttachedBlip.Delete();
                return false;
            }

            // Blip management
            if (_driver.IsCuffed && _driver.AttachedBlip is not null)
            {
                if (_driver.AttachedBlip.Color != BlipColor.Blue)
                {
                    _driver.AttachedBlip.Color = BlipColor.Blue;
                    _driver.AttachedBlip.IsFlashing = true;
                }
            }
            if (_passenger is not null)
            {
                if (_passenger.IsDead && _passenger.AttachedBlip is not null)
                {
                    _passenger.AttachedBlip.Delete();
                }
                else if (_passenger.IsCuffed && _passenger.AttachedBlip is not null)
                {
                    if (_passenger.AttachedBlip.Color != BlipColor.Blue)
                    {
                        _passenger.AttachedBlip.Color = BlipColor.Blue;
                        _passenger.AttachedBlip.IsFlashing = true;
                    }
                }
            }

            // Trigger behavior if player is close
            if (Game.PlayerPed.Position.DistanceToSquared(_vehicle.Position) < 3600.0f) // 60m
            {
                // Simulate erratic behavior
                if (rnd.Next(100) < 5) // 5% chance per tick
                {
                    // Swerve or brake check
                    _vehicle.SteeringScale = 2.0f; // Temporary swerve
                    API.SetVehicleIndicatorLights(_vehicle.Handle, 1, true); // Left blinker
                    API.SetVehicleIndicatorLights(_vehicle.Handle, 0, true); // Right blinker
                    NotificationService.InfoNotify("Vehicle is swerving erratically.", "Observation");
                }

                if (_scenario == RollingDomesticScenario.Ejection)
                {
                    if (_passenger is not null && _passenger.IsAlive && _passenger.IsInVehicle(_vehicle) && _vehicle.Speed > 5f)
                    {
                        // Chance to jump out or be pushed
                        if (rnd.Next(100) < 2) 
                        {
                            _passenger.Task.LeaveVehicle(_vehicle, true); // Bail out
                            // Driver flees after passenger exits
                            _driver.Task.FleeFrom(Game.PlayerPed);
                            NotificationService.ShowNetworkedNotification("Passenger has been ejected from the moving vehicle!", "Dispatch");
                            return true; // Keep monitoring driver
                        }
                    }
                }
                else if (_scenario == RollingDomesticScenario.Argument)
                {
                    // Just erratic driving, maybe they stop eventually
                    if (Game.PlayerPed.Position.DistanceToSquared(_vehicle.Position) < 400.0f) // 20m
                    {
                        // If player gets very close, maybe they pull over
                        if (rnd.Next(100) < 1)
                        {
                            _driver.Task.ParkVehicle(_vehicle, _vehicle.Position, _vehicle.Heading);
                            NotificationService.ShowNetworkedNotification("Vehicle is pulling over.", "Dispatch");
                        }
                    }
                }
            }
            
            // End condition: Driver arrested or dead
            if (_driver.IsCuffed || _driver.IsDead) return false;

            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        if (_vehicle is not null && _vehicle.Exists())
            _vehicle.AttachedBlip?.Delete();

        if (_driver is not null && _driver.Exists())
            _driver.AttachedBlip?.Delete();
            
        if (_passenger is not null && _passenger.Exists())
            _passenger.AttachedBlip?.Delete();

        base.OnCancelBefore();
    }
}