namespace InfinityProject.Time
{
 /// <summary>
 /// Test shim to override Time.DeltaTime in edit-mode/unit tests.
 /// Systems will prefer this value when set (non-zero), otherwise fall back to SystemAPI.Time.DeltaTime.
 /// </summary>
 public static class TimeTestShim
 {
 // Seconds to use for DeltaTime when non-zero. Tests can set this value and clear it after.
 public static float OverrideDeltaSeconds =0f;

 public static float EffectiveDeltaSeconds(float defaultDelta)
 {
 return OverrideDeltaSeconds >0f ? OverrideDeltaSeconds : defaultDelta;
 }
 }
}
