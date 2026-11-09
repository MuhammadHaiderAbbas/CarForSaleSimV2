namespace CarSales.Sim
{
    /// <summary>
    /// Preset physics profiles per handling class. Numbers are based on typical real cars of each type
    /// (mass, torque, gearing), so 0-100 km/h times and top speeds land in realistic ranges
    /// (checked by <see cref="PerformanceEstimator"/> in the tests).
    /// </summary>
    public static class VehicleProfiles
    {
        public static VehicleProfile For(VehicleClass c, PowertrainType powertrain)
        {
            var p = Base(c);
            if (powertrain == PowertrainType.Electric && !p.IsElectric) p = ElectrifiedCopy(p);
            else if (powertrain == PowertrainType.Hybrid && !p.IsElectric)
            {
                p.powertrain = PowertrainType.Hybrid;
                p.engine.peakTorqueNm *= 1.15f;          // electric boost at low revs
                p.engine.peakTorqueRpm = System.Math.Max(p.engine.idleRpm + 400f, p.engine.peakTorqueRpm * 0.7f);
                p.energyPercentPerKm *= 0.7f;
            }
            else if (powertrain == PowertrainType.Diesel && p.powertrain == PowertrainType.Petrol) p = DieselCopy(p);
            else if (powertrain == PowertrainType.Petrol && p.powertrain == PowertrainType.Diesel) p = PetrolCopy(p);
            p.name = $"{c} ({p.powertrain})";
            p.engine.Invalidate();
            return p;
        }

        static VehicleProfile Make(VehicleClass c, PowertrainType pt, Drivetrain dt, float mass, float torque, float peakRpm, float plateauEnd,
                                   float idle, float redline, float[] ratios, float final, float up, float down,
                                   float radius, float dragArea, float steer, float comHeight)
        {
            return new VehicleProfile
            {
                vehicleClass = c,
                powertrain = pt,
                drivetrain = dt,
                engine = new EngineSpec
                {
                    idleRpm = idle, redlineRpm = redline, peakTorqueNm = torque, peakTorqueRpm = peakRpm,
                    plateauEndRpm = plateauEnd, engineBrakeNm = torque * 0.16f
                },
                gearbox = new GearboxSpec
                {
                    ratios = ratios, reverseRatio = ratios[0] * 0.95f, finalDrive = final,
                    upshiftRpm = up, downshiftRpm = down, shiftTime = 0.25f, efficiency = 0.9f
                },
                chassis = new ChassisSpec
                {
                    massKg = mass, wheelRadius = radius, dragArea = dragArea, maxSteerAngle = steer, centerOfMassHeight = comHeight
                }
            };
        }

        static VehicleProfile Base(VehicleClass c)
        {
            var p = BaseUnlimited(c);
            // Electronic speed limiters, like the real cars of these classes.
            if (c == VehicleClass.Sports) p.chassis.topSpeedLimitKmh = 290f;
            else if (c == VehicleClass.Luxury || c == VehicleClass.SportsSedan) p.chassis.topSpeedLimitKmh = 250f;
            return p;
        }

        static VehicleProfile BaseUnlimited(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.CityHatch:
                    return Make(c, PowertrainType.Petrol, Drivetrain.FWD, 1080f, 118f, 3500f, 4200f, 850f, 6500f,
                                new[] { 3.55f, 1.95f, 1.3f, 0.97f, 0.78f }, 4.1f, 6000f, 2100f, 0.30f, 0.62f, 37f, 0.42f);
                case VehicleClass.FamilyHatch:
                    return Make(c, PowertrainType.Petrol, Drivetrain.FWD, 1250f, 215f, 1800f, 4000f, 800f, 6300f,
                                new[] { 3.6f, 2.1f, 1.4f, 1.05f, 0.85f, 0.7f }, 3.7f, 5800f, 1900f, 0.31f, 0.66f, 36f, 0.44f);
                case VehicleClass.SportsSedan:
                    return Make(c, PowertrainType.Petrol, Drivetrain.RWD, 1520f, 380f, 1700f, 4600f, 750f, 6800f,
                                new[] { 3.5f, 2.2f, 1.5f, 1.15f, 0.92f, 0.75f }, 3.5f, 6400f, 2100f, 0.33f, 0.66f, 35f, 0.46f);
                case VehicleClass.SUV:
                    return Make(c, PowertrainType.Diesel, Drivetrain.AWD, 1950f, 390f, 1750f, 2750f, 750f, 4600f,
                                new[] { 3.9f, 2.2f, 1.45f, 1.05f, 0.82f, 0.66f }, 3.6f, 4100f, 1500f, 0.37f, 0.95f, 34f, 0.62f);
                case VehicleClass.Pickup:
                    return Make(c, PowertrainType.Diesel, Drivetrain.RWD, 2100f, 360f, 2000f, 2800f, 750f, 4400f,
                                new[] { 4.2f, 2.4f, 1.5f, 1.0f, 0.8f, 0.68f }, 3.9f, 3900f, 1450f, 0.38f, 1.05f, 34f, 0.66f);
                case VehicleClass.Van:
                    return Make(c, PowertrainType.Diesel, Drivetrain.FWD, 1900f, 290f, 1750f, 2500f, 750f, 4500f,
                                new[] { 4.1f, 2.2f, 1.4f, 1.0f, 0.78f, 0.64f }, 3.9f, 4000f, 1450f, 0.34f, 1.15f, 36f, 0.68f);
                case VehicleClass.Sports:
                    return Make(c, PowertrainType.Petrol, Drivetrain.RWD, 1450f, 530f, 4500f, 5800f, 900f, 7500f,
                                new[] { 3.0f, 2.1f, 1.6f, 1.25f, 1.0f, 0.82f }, 3.7f, 7200f, 2800f, 0.34f, 0.62f, 34f, 0.4f);
                case VehicleClass.Luxury:
                    return Make(c, PowertrainType.Petrol, Drivetrain.RWD, 1900f, 520f, 1500f, 4500f, 700f, 6300f,
                                new[] { 4.7f, 3.1f, 2.1f, 1.67f, 1.29f, 1.0f, 0.84f, 0.67f }, 2.8f, 5900f, 1700f, 0.35f, 0.7f, 34f, 0.48f);
                case VehicleClass.ElectricHatch:
                    return ElectrifiedCopy(BaseUnlimited(VehicleClass.FamilyHatch));
                default: // Sedan
                    return Make(c, PowertrainType.Petrol, Drivetrain.FWD, 1420f, 230f, 1800f, 4200f, 800f, 6300f,
                                new[] { 3.7f, 2.05f, 1.35f, 1.0f, 0.8f, 0.66f }, 3.5f, 5800f, 1900f, 0.32f, 0.66f, 35f, 0.46f);
            }
        }

        static VehicleProfile DieselCopy(VehicleProfile p)
        {
            var d = p.Copy();
            d.powertrain = PowertrainType.Diesel;
            d.engine.idleRpm = 780f;
            d.engine.redlineRpm = 4800f;
            d.engine.peakTorqueNm *= 1.3f;
            d.engine.peakTorqueRpm = 1750f;
            d.engine.plateauEndRpm = 2750f;
            d.engine.engineBrakeNm = d.engine.peakTorqueNm * 0.18f;
            d.gearbox.upshiftRpm = 4300f;
            d.gearbox.downshiftRpm = 1500f;
            d.gearbox.finalDrive *= 0.88f;
            d.chassis.massKg += 60f;
            d.energyPercentPerKm *= 0.8f;
            return d;
        }

        static VehicleProfile PetrolCopy(VehicleProfile p)
        {
            var d = p.Copy();
            d.powertrain = PowertrainType.Petrol;
            d.engine.idleRpm = 800f;
            d.engine.redlineRpm = 6300f;
            d.engine.peakTorqueNm /= 1.25f;
            d.engine.peakTorqueRpm = 2000f;
            d.engine.plateauEndRpm = 4500f;
            d.engine.engineBrakeNm = d.engine.peakTorqueNm * 0.16f;
            d.gearbox.upshiftRpm = 5800f;
            d.gearbox.downshiftRpm = 1900f;
            d.gearbox.finalDrive /= 0.88f;
            d.chassis.massKg -= 60f;
            d.energyPercentPerKm /= 0.8f;
            return d;
        }

        static VehicleProfile ElectrifiedCopy(VehicleProfile p)
        {
            var e = p.Copy();
            e.powertrain = PowertrainType.Electric;
            e.vehicleClass = p.vehicleClass == VehicleClass.FamilyHatch ? VehicleClass.ElectricHatch : p.vehicleClass;
            e.chassis.massKg += 350f;                         // battery
            e.chassis.centerOfMassHeight -= 0.08f;            // battery in the floor
            float scale = e.chassis.massKg / 1600f;
            e.electric = new ElectricSpec
            {
                maxTorqueNm = 310f * scale, maxPowerKw = 150f * scale, maxRpm = 12000f, ratio = 9f,
                regenFraction = 0.25f, efficiency = 0.92f
            };
            e.energyPercentPerKm = p.energyPercentPerKm * 1.1f;
            return e;
        }
    }
}
