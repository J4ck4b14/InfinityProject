using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using NUnit.Framework;

namespace InfinityProject.Tests
{
    public class FloraTests
    {
        [Test] public void Generator_IsDeterministic()
        {
            BuildInputs(6101,10f,0.6f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData climate);
            FloraGenerationSettings s=Single(DefaultSpecies()); FloraData a=FloraGenerator.Generate(g,ground,climate,s), b=FloraGenerator.Generate(g,ground,climate,s);
            CollectionAssert.AreEqual(a.Species[0].EstablishmentSuitability,b.Species[0].EstablishmentSuitability);
            CollectionAssert.AreEqual(a.Species[0].LimitingFactor,b.Species[0].LimitingFactor);
        }

        [Test] public void TemperatureOutsideRangeBlocksEstablishment()
        {
            BuildInputs(6102,-20f,0.6f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(DefaultSpecies()));
            Assert.AreEqual(0f,d.Species[0].EstablishmentSuitability[0]); Assert.AreEqual(FloraLimitingFactor.Temperature,(FloraLimitingFactor)d.Species[0].LimitingFactor[0]);
        }

        [Test] public void WaterAvailabilityCanBeLimiting()
        {
            FloraSpeciesProfile p=DefaultSpecies(); p.MinimumWaterAvailabilityPotential=0.2f; p.OptimalWaterAvailabilityPotential=0.6f; p.MaximumWaterAvailabilityPotential=1f;
            BuildInputs(6103,10f,0.4f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(p));
            Assert.AreEqual(0.5f,d.Species[0].EstablishmentSuitability[0],0.001f); Assert.AreEqual(FloraLimitingFactor.WaterAvailability,(FloraLimitingFactor)d.Species[0].LimitingFactor[0]);
        }

        [Test] public void PoorRetentionSuppressesOtherwiseSuitableSite()
        {
            FloraSpeciesProfile p=DefaultSpecies(); p.MinimumSoilRetentionPotential=0.2f;p.FullSoilRetentionPotential=0.8f;
            BuildInputs(6104,10f,0.6f,0.5f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(p));
            Assert.AreEqual(0.5f,d.Species[0].EstablishmentSuitability[0],0.001f); Assert.AreEqual(FloraLimitingFactor.SoilRetention,(FloraLimitingFactor)d.Species[0].LimitingFactor[0]);
        }

        [Test] public void DifferentWaterNichesProduceDifferentResponses()
        {
            FloraSpeciesProfile low=DefaultSpecies(); low.StableId="low";low.MinimumWaterAvailabilityPotential=0f;low.OptimalWaterAvailabilityPotential=0.3f;low.MaximumWaterAvailabilityPotential=0.55f;
            FloraSpeciesProfile high=DefaultSpecies(); high.StableId="high";high.MinimumWaterAvailabilityPotential=0.5f;high.OptimalWaterAvailabilityPotential=0.8f;high.MaximumWaterAvailabilityPotential=1f;
            BuildInputs(6105,10f,0.3f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData c);
            FloraData d=FloraGenerator.Generate(g,ground,c,new FloraGenerationSettings{Species=new System.Collections.Generic.List<FloraSpeciesProfile>{low,high}});
            Assert.Greater(d.Species[0].EstablishmentSuitability[0],d.Species[1].EstablishmentSuitability[0]);
        }

        [Test] public void SameWaterAvailabilityIgnoresHowSupplyWasPartitionedUpstream()
        {
            FloraSpeciesProfile p=DefaultSpecies();
            BuildInputs(6106,10f,0.65f,1f,out GeographyData g,out GroundConditionData a,out ClimateData c);
            BuildInputs(6106,10f,0.65f,1f,out _,out GroundConditionData b,out _);
            // TWI/wetness can differ without changing Flora if generic water availability is identical.
            for(int i=0;i<b.HydrologicalWetnessIndex.Length;i++) b.HydrologicalWetnessIndex[i]=-20f;
            FloraData da=FloraGenerator.Generate(g,a,c,Single(p)); FloraData db=FloraGenerator.Generate(g,b,c,Single(p));
            CollectionAssert.AreEqual(da.Species[0].EstablishmentSuitability,db.Species[0].EstablishmentSuitability);
        }

        [Test] public void FloraDoesNotGateOnClimatePrecipitationAfterWaterAvailabilityExists()
        {
            FloraSpeciesProfile p=DefaultSpecies();
            BuildInputs(6112,10f,0.65f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData dryClimate);
            ClimateData wetClimate=new(g.Resolution,g.WidthMeters,g.LengthMeters,g.Seed,C(g.Resolution*g.Resolution,10f),C(g.Resolution*g.Resolution,0.95f),10f,10f,0.95f,0.95f);
            FloraData dry=FloraGenerator.Generate(g,ground,dryClimate,Single(p)); FloraData wet=FloraGenerator.Generate(g,ground,wetClimate,Single(p));
            CollectionAssert.AreEqual(dry.Species[0].EstablishmentSuitability,wet.Species[0].EstablishmentSuitability);
        }

        [Test] public void InitialBiomassStartsOnlyAboveThreshold()
        {
            FloraSpeciesProfile p=DefaultSpecies();
            p.EstablishmentThreshold=0.8f;

            BuildInputs(6107,10f,0.5f,1f,out GeographyData g,out GroundConditionData belowGround,out ClimateData c);
            FloraData below=FloraGenerator.Generate(g,belowGround,c,Single(p));
            Assert.Less(below.Species[0].EstablishmentSuitability[0],p.EstablishmentThreshold);
            Assert.AreEqual(0f,below.Species[0].InitialBiomass[0]);
            Assert.AreEqual(0f,below.Species[0].InitialBiomassKgPerSquareMeter[0]);

            BuildInputs(6107,10f,0.6f,1f,out _,out GroundConditionData aboveGround,out _);
            FloraData above=FloraGenerator.Generate(g,aboveGround,c,Single(p));
            Assert.Greater(above.Species[0].EstablishmentSuitability[0],p.EstablishmentThreshold);
            Assert.Greater(above.Species[0].InitialBiomass[0],0f);
            Assert.Greater(above.Species[0].InitialBiomassKgPerSquareMeter[0],0f);
        }

        [Test] public void OutputsRemainFiniteAndNormalized()
        {
            BuildInputs(6108,10f,0.7f,0.8f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(DefaultSpecies()));
            for(int i=0;i<d.Species[0].EstablishmentSuitability.Length;i++){Assert.That(d.Species[0].EstablishmentSuitability[i],Is.InRange(0f,1f));Assert.That(d.Species[0].InitialBiomass[i],Is.InRange(0f,1f));}
        }

        [Test] public void NoLimitingFactorWhenEveryResponseIsFull()
        {
            FloraSpeciesProfile p=DefaultSpecies(); BuildInputs(6109,10f,0.6f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(p));
            Assert.AreEqual(1f,d.Species[0].EstablishmentSuitability[0],0.0001f); Assert.AreEqual(FloraLimitingFactor.None,(FloraLimitingFactor)d.Species[0].LimitingFactor[0]);
        }

        [Test] public void ReportsMultipleWhenConditionsCoLimit()
        {
            FloraSpeciesProfile p=DefaultSpecies();p.MinimumWaterAvailabilityPotential=0f;p.OptimalWaterAvailabilityPotential=1f;p.MaximumWaterAvailabilityPotential=1f;p.MinimumSoilRetentionPotential=0f;p.FullSoilRetentionPotential=1f;
            BuildInputs(6110,10f,0.5f,0.5f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(p));
            Assert.AreEqual(FloraLimitingFactor.Multiple,(FloraLimitingFactor)d.Species[0].LimitingFactor[0]);
        }

        [Test] public void CarryingCapacityTracksSuitabilityAndSpeciesMaximum()
        {
            FloraSpeciesProfile p=DefaultSpecies();p.MaximumBiomassKgPerSquareMeter=4f; BuildInputs(6111,10f,0.6f,1f,out GeographyData g,out GroundConditionData ground,out ClimateData c); FloraData d=FloraGenerator.Generate(g,ground,c,Single(p));
            Assert.AreEqual(4f,d.Species[0].CarryingCapacityKgPerSquareMeter[0],0.0001f);Assert.Greater(d.Species[0].InitialBiomassKgPerSquareMeter[0],0f);
        }

        [Test] public void DefaultSpeciesHaveDistinctWaterNiches()
        {
            var defaults=FloraGenerationSettings.CreateDefaultSpecies(); Assert.Less(defaults[0].MinimumWaterAvailabilityPotential,defaults[1].MinimumWaterAvailabilityPotential); Assert.Less(defaults[2].OptimalWaterAvailabilityPotential,defaults[1].OptimalWaterAvailabilityPotential);
        }

        private static FloraSpeciesProfile DefaultSpecies()=>new(){StableId="test",DisplayName="Test",MinimumTemperatureCelsius=0f,OptimalTemperatureCelsius=10f,MaximumTemperatureCelsius=20f,MinimumWaterAvailabilityPotential=0.2f,OptimalWaterAvailabilityPotential=0.6f,MaximumWaterAvailabilityPotential=1f,MinimumSoilRetentionPotential=0f,FullSoilRetentionPotential=0.5f,EstablishmentThreshold=0.2f,InitialOccupancyFraction=0.65f,MaximumBiomassKgPerSquareMeter=1f,IntrinsicGrowthRatePerDay=0.05f,SeedBankRecruitmentRatePerDay=0.002f,StressMortalityRatePerDay=0.1f,CompetitionStrength=0.2f};
        private static FloraGenerationSettings Single(FloraSpeciesProfile p)=>new(){Species=new System.Collections.Generic.List<FloraSpeciesProfile>{p}};
        private static void BuildInputs(int seed,float temp,float water,float retention,out GeographyData g,out GroundConditionData ground,out ClimateData climate)
        {
            const int n=2;const float size=100f;g=new GeographyData(n,size,size,100f,seed,new float[n*n]);float[] twi=C(n*n,8f),ret=C(n*n,retention),runon=C(n*n,0.2f),wat=C(n*n,water);
            ground=new GroundConditionData(n,size,size,seed,0L,twi,runon,wat,ret,8f,8f,8f,8f); climate=new ClimateData(n,size,size,seed,C(n*n,temp),C(n*n,0.5f),temp,temp,0.5f,0.5f);
        }
        private static float[] C(int n,float v){var a=new float[n];for(int i=0;i<n;i++)a[i]=v;return a;}
    }
}
