using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FiveFR._API.Attributes;
using FiveFR._API.Classes;
using FiveFR._API.Extensions;
using FiveFR._API.Services;

namespace GreatCallouts_FiveFR
{
    [Guid("10DFA719-5CED-4193-9BE9-67C71A8BCEA9")]
    [AddonProperties( "Hotbox Call", "^3DevKilo", "1.0.0")]
    public class HotboxCall : Callout
    {
        private int _particleHandle;
        private Blip? _blip;
        
        Vector3 GetLocation()
        {
            var distance = rnd.Next(CalloutConfig.HotboxConfig.MinSpawnDistance, CalloutConfig.HotboxConfig.MaxSpawnDistance);
            var offsetX = rnd.Next(-100, 100) / 100f;
            var offsetY = rnd.Next(-100, 100) / 100f;
            var direction = new Vector3(offsetX, offsetY, 0);
            direction.Normalize();
            var pos = Game.PlayerPed.Position + (direction * distance);
            return pos.ClosestParkedCarPlacement();
        }

        public HotboxCall()
        {
            if (CalloutConfig.HotboxConfig.FixedLocation && CalloutConfig.HotboxConfig.Locations.Any())
                InitInfo( CalloutConfig.HotboxConfig.Locations.SelectRandom());
            else
                InitInfo(GetLocation());
            
            ShortName = "Suspicious Vehicle";
            CalloutDescription = "Reports of a suspiciously distracting vehicle in your area.";
            ResponseCode = 2;
            StartDistance = CalloutConfig.HotboxConfig.StartRadius;
        }

        public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.HotboxConfig.Enabled);

        static Random rnd = new Random();

        private const string ParticleAsset = "core";
        private const string ParticleEffect = "exp_grd_bzgas_smoke";
        
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

        public override Task OnAccept()
        {
            Vector3 outPosition = default;
            float outHeading = default;
            API.GetClosestVehicleNodeWithHeading(Location.X, Location.Y, Location.Z, ref outPosition, ref outHeading, 256, 1f, 0);
            SpawnVehicle(GetRandomVehicleHash(), Location, outHeading).ContinueWith(async task =>
            {
                if (task.IsFaulted)
                {
                    EndCallout();
                    return;
                }
                var veh = task.Result;
                var driver = await SpawnPed(GetRandomPedHash(), veh.Position, veh.Heading);
                driver.SetIntoVehicle(veh, VehicleSeat.Driver);
                
                int randomAdditionalPassengers = rnd.Next(CalloutConfig.HotboxConfig.MinSuspects, CalloutConfig.HotboxConfig.MaxSuspects + 1);
                // Seats: 0 (Passenger), 1 (LeftRear), 2 (RightRear)
                for (int i = 0; i < randomAdditionalPassengers; i++)
                {
                    if (API.IsVehicleSeatFree(veh.Handle, i))
                    {
                        var _ped = await SpawnPed(GetRandomPedHash(), veh.Position, veh.Heading);
                        _ped.SetIntoVehicle(veh, (VehicleSeat)i);
                        await BaseScript.Delay(100);
                    }
                }
            });
            InitBlip();
            return base.OnAccept();
        }
        
        

        private async Task SpawnSmokeInVehicle(Vehicle vehicle)
        {
            if (vehicle is null || !vehicle.Exists()) return;

            API.RequestNamedPtfxAsset(ParticleAsset);
            await QueueService.Predicate(() => !API.HasNamedPtfxAssetLoaded(ParticleAsset));

            API.UseParticleFxAssetNextCall(ParticleAsset);
            _particleHandle = API.StartParticleFxLoopedOnEntity(
                ParticleEffect,
                vehicle.Handle,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0.5f,
                false, false, false
            );
        }

        private void StopSmoke()
        {
            if (API.DoesParticleFxLoopedExist(_particleHandle))
            {
                API.StopParticleFxLooped(_particleHandle, false);
            }
        }

        public override async void OnStart(Ped player)
        {
            if (SpawnedEntities.FirstOrDefault(e => e is Vehicle) is not Vehicle vehicle)
            {
                EndCallout();
                return;
            }
            _blip = vehicle.AttachBlip();
            _blip.Name = "Suspicious Vehicle";
            _ = SpawnSmokeInVehicle(vehicle);

            NotificationService.ShowNetworkedNotification("Dispatch: Investigate the suspicious vehicle. Reports of smoke emanating from inside.", "Dispatch");

            await QueueService.Predicate(() => AssignedPlayers.Any(p =>
                !p.IsInVehicle() && p.Position.DistanceToSquared(vehicle.Position) <
                vehicle.Model.GetDimensions().LengthSquared()));
            
            NotificationService.InfoNotify("You sense a strong odor coming from the vehicle.", "Observation");
            
            StopSmoke();
            base.OnStart(player);
        }

        public override void OnCancelBefore()
        {
            _blip?.Delete();
            StopSmoke();
            base.OnCancelBefore();
        }
    }
}