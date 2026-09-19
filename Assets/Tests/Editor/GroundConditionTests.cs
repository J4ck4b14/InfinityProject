using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using NUnit.Framework;
using UnityEngine;

namespace InfinityProject.Tests
{
    public class GroundConditionTests
    {
        [Test] public void Generator_IsDeterministic()
        {
            GeographyData g = MakePlane(9, 800f, 80f, 3101); HydrologyData h = MakeHydrology(g, 40000f); ClimateData c = MakeClimate(g, 0.48f);
            GroundConditionData a = GroundConditionGenerator.Generate(g, h, c), b = GroundConditionGenerator.Generate(g, h, c);
            CollectionAssert.AreEqual(a.HydrologicalWetnessIndex, b.HydrologicalWetnessIndex); CollectionAssert.AreEqual(a.RunOnPotential, b.RunOnPotential);
            CollectionAssert.AreEqual(a.WaterAvailabilityPotential, b.WaterAvailabilityPotential); CollectionAssert.AreEqual(a.SoilRetentionPotential, b.SoilRetentionPotential);
        }

        [Test] public void MoreContributingAreaMeansMoreRunOnAndWaterAtSameClimate()
        {
            GeographyData g = MakePlane(5, 400f, 100f, 3102); float[] a = Constant(25, 2000f); int low=g.Index(1,2), high=g.Index(3,2); a[high]=100000f;
            GroundConditionData d = GroundConditionGenerator.Generate(g, MakeHydrology(g,a), MakeClimate(g,0.4f));
            Assert.Greater(d.RunOnPotential[high], d.RunOnPotential[low]); Assert.Greater(d.WaterAvailabilityPotential[high], d.WaterAvailabilityPotential[low]);
        }

        [Test] public void DirectPrecipitationCreatesWaterWithoutTopographicConvergence()
        {
            var settings = new GroundConditionGenerationSettings { RunOnLocalSupportAreaSquareMeters = 5000f };
            GeographyData g = MakePlane(5,400f,30f,3103); GroundConditionData d = GroundConditionGenerator.Generate(g, MakeHydrology(g,1000f), MakeClimate(g,0.62f), settings);
            Assert.AreEqual(0f, d.RunOnPotential[g.Index(2,2)], 0.0001f); Assert.AreEqual(0.62f, d.WaterAvailabilityPotential[g.Index(2,2)], 0.0001f);
        }

        [Test] public void RunOnAddsWaterButCannotExceedOne()
        {
            var s = new GroundConditionGenerationSettings { RunOnLocalSupportAreaSquareMeters=100f, RunOnResponseAreaSquareMeters=1000f, RunOnWaterBoostStrength=1f };
            GeographyData g=MakePlane(5,400f,20f,3104); GroundConditionData d=GroundConditionGenerator.Generate(g,MakeHydrology(g,1000000f),MakeClimate(g,0.8f),s);
            Assert.Greater(d.WaterAvailabilityPotential[g.Index(2,2)],0.8f); Assert.LessOrEqual(d.WaterAvailabilityPotential[g.Index(2,2)],1f);
        }

        [Test] public void FlatTerrainRemainsFiniteAndNormalized()
        {
            GeographyData g=new(9,800f,800f,100f,3105,new float[81]); GroundConditionData d=GroundConditionGenerator.Generate(g,MakeHydrology(g,30000f),MakeClimate(g,0.48f));
            for(int i=0;i<81;i++){Assert.IsFalse(float.IsNaN(d.HydrologicalWetnessIndex[i])); Assert.That(d.WaterAvailabilityPotential[i],Is.InRange(0f,1f)); Assert.That(d.SoilRetentionPotential[i],Is.InRange(0f,1f));}
        }

        [Test] public void FixedScaleSlopeSuppressesSubScaleCorrugation()
        {
            GeographyData g=MakeCorrugatedTerrain(257,256f,3110); int c=g.Resolution/2; float local=g.GetSlopeDegrees(c,c); float scaled=GroundConditionGenerator.SampleSlopeDegreesAtScale(g,c,c,24f);
            Assert.Greater(local,25f); Assert.Less(scaled,8f);
        }

        [Test] public void FixedScaleSlopeIsComparableAcrossRasterResolutions()
        {
            GeographyData a=MakeCorrugatedTerrain(129,256f,3111), b=MakeCorrugatedTerrain(257,256f,3111);
            float sa=GroundConditionGenerator.SampleSlopeDegreesAtScale(a,a.Resolution/2,a.Resolution/2,24f), sb=GroundConditionGenerator.SampleSlopeDegreesAtScale(b,b.Resolution/2,b.Resolution/2,24f);
            Assert.AreEqual(sa,sb,0.75f);
        }

        [Test] public void ZeroSlopeRadiusUsesNativeGeographySlope()
        {
            GeographyData g=MakeCorrugatedTerrain(129,256f,3112); int c=g.Resolution/2;
            Assert.AreEqual(g.GetSlopeDegrees(c,c),GroundConditionGenerator.SampleSlopeDegreesAtScale(g,c,c,0f),0.0001f);
        }

        [Test] public void ScaledSlopeRaisesRetentionOnFineCorrugation()
        {
            GeographyData g=MakeCorrugatedTerrain(257,256f,3113); HydrologyData h=MakeHydrology(g,50000f); ClimateData c=MakeClimate(g,0.48f);
            GroundConditionData local=GroundConditionGenerator.Generate(g,h,c,new GroundConditionGenerationSettings{SlopeAnalysisRadiusMeters=0f});
            GroundConditionData scaled=GroundConditionGenerator.Generate(g,h,c,new GroundConditionGenerationSettings{SlopeAnalysisRadiusMeters=24f}); int i=g.Index(g.Resolution/2,g.Resolution/2);
            Assert.Greater(scaled.SoilRetentionPotential[i],local.SoilRetentionPotential[i]);
        }


        [Test] public void RunOnMappingUsesPhysicalSquareMetres()
        {
            var s=new GroundConditionGenerationSettings{RunOnLocalSupportAreaSquareMeters=1000f,RunOnResponseAreaSquareMeters=9000f};
            Assert.AreEqual(0f,GroundConditionGenerator.RunOnPotentialFromArea(1000f,s),0.0001f); Assert.Greater(GroundConditionGenerator.RunOnPotentialFromArea(10000f,s),0.6f);
        }

        [Test] public void WaterAvailabilityRespondsToBothClimateAndRunOn()
        {
            var s=new GroundConditionGenerationSettings{RunOnWaterBoostStrength=0.8f}; float dry=GroundConditionGenerator.WaterAvailabilityFromSupply(0.3f,0f,s); float runon=GroundConditionGenerator.WaterAvailabilityFromSupply(0.3f,0.8f,s); float rainy=GroundConditionGenerator.WaterAvailabilityFromSupply(0.7f,0f,s);
            Assert.Greater(runon,dry); Assert.Greater(rainy,dry);
        }

        [Test] public void ZeroRunOnBoostMakesWaterAvailabilityEqualClimateSupply()
        {
            var s=new GroundConditionGenerationSettings{RunOnLocalSupportAreaSquareMeters=1f,RunOnResponseAreaSquareMeters=100f,RunOnWaterBoostStrength=0f};
            GeographyData g=MakePlane(5,400f,20f,3115); GroundConditionData d=GroundConditionGenerator.Generate(g,MakeHydrology(g,1000000f),MakeClimate(g,0.37f),s);
            for(int i=0;i<d.WaterAvailabilityPotential.Length;i++) Assert.AreEqual(0.37f,d.WaterAvailabilityPotential[i],0.0001f);
        }

        [Test] public void PercentileRangeIsOrdered()
        {
            GeographyData g=MakePlane(17,1600f,200f,3114); float[] a=new float[289]; for(int i=0;i<a.Length;i++) a[i]=1000f+i*250f;
            GroundConditionData d=GroundConditionGenerator.Generate(g,MakeHydrology(g,a),MakeClimate(g,0.48f)); Assert.LessOrEqual(d.MinimumWetnessIndex,d.Percentile05WetnessIndex); Assert.LessOrEqual(d.Percentile05WetnessIndex,d.Percentile95WetnessIndex); Assert.LessOrEqual(d.Percentile95WetnessIndex,d.MaximumWetnessIndex);
        }

        private static GeographyData MakePlane(int n,float width,float maxElevation,int seed){float[] h=new float[n*n]; for(int y=0;y<n;y++)for(int x=0;x<n;x++)h[y*n+x]=x/(n-1f); return new GeographyData(n,width,width,maxElevation,seed,h);}
        private static GeographyData MakeCorrugatedTerrain(int n,float width,int seed){const float maxE=100f; float[] h=new float[n*n]; float spacing=width/(n-1); for(int y=0;y<n;y++)for(int x=0;x<n;x++){float wx=x*spacing; float e=20f+0.05f*wx+2f*Mathf.Sin(wx*Mathf.PI*0.5f); h[y*n+x]=e/maxE;} return new GeographyData(n,width,width,maxE,seed,h);}
        private static ClimateData MakeClimate(GeographyData g,float p)=>new(g.Resolution,g.WidthMeters,g.LengthMeters,g.Seed,Constant(g.Resolution*g.Resolution,12f),Constant(g.Resolution*g.Resolution,p),12f,12f,p,p);
        private static HydrologyData MakeHydrology(GeographyData g,float a)=>MakeHydrology(g,Constant(g.Resolution*g.Resolution,a));
        private static HydrologyData MakeHydrology(GeographyData g,float[] a){int count=g.Resolution*g.Resolution; var r=new int[count];var b=new int[count];var t=new byte[count];var s=new byte[count];var d=new float[count];for(int i=0;i<count;i++){r[i]=-1;t[i]=(byte)HydrologyTerminalType.Outlet;} return new HydrologyData(g.Resolution,g.WidthMeters,g.LengthMeters,g.Seed,0L,r,a,b,t,s,d,1,0,count,0,0f);}
        private static float[] Constant(int n,float v){var a=new float[n];for(int i=0;i<n;i++)a[i]=v;return a;}
    }
}
