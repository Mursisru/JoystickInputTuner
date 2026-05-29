using System.Globalization;
using System.Text;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.App.Services;

public static class FilterSettingsLogFormatter
{
    public static string Format(
        FilterSettings settings,
        int pollingHz,
        string language,
        string deviceId,
        string axisId,
        string profileName)
    {
        settings = FilterSettingsNormalizer.Ensure(settings);
        var sb = new StringBuilder(1200);
        sb.Append("profile=").Append(profileName);
        sb.Append("; lang=").Append(language);
        sb.Append("; polling=").Append(pollingHz);
        sb.Append("; device=").Append(string.IsNullOrWhiteSpace(deviceId) ? "-" : deviceId);
        sb.Append("; axis=").Append(string.IsNullOrWhiteSpace(axisId) ? "-" : axisId);

        AppendDeadzone(sb, settings.Deadzone);
        AppendMedian(sb, settings.Median);
        AppendHampel(sb, settings.Hampel);
        AppendSpikeGate(sb, settings.SpikeGate);
        AppendZGuard(sb, settings.ZImpulseGuard);
        AppendAxisIntent(sb, settings.AxisIntent);
        AppendCrossShield(sb, settings.CrossAxisShield);
        AppendAxisBindLock(sb, settings.AxisBindLock);
        AppendRateLimiter(sb, settings.RateLimiter);
        AppendEma(sb, settings.Ema);
        return sb.ToString();
    }

    private static void AppendDeadzone(StringBuilder sb, DeadzoneSettings s)
    {
        sb.Append("; deadzone=").Append(s.Enabled ? "on" : "off");
        sb.Append("; deadzoneRadius=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.Radius);
        sb.Append("; deadzoneDynamic=").Append(s.Dynamic ? "on" : "off");
        sb.Append("; deadzoneDynMul=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.DynamicMultiplier);
    }

    private static void AppendMedian(StringBuilder sb, MedianSettings s)
    {
        sb.Append("; median=").Append(s.Enabled ? "on" : "off");
        sb.Append("; medianWin=").Append(s.WindowSize);
    }

    private static void AppendHampel(StringBuilder sb, HampelSettings s)
    {
        sb.Append("; hampel=").Append(s.Enabled ? "on" : "off");
        sb.Append("; hampelWin=").Append(s.WindowSize);
        sb.Append("; hampelSigma=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.SigmaMultiplier);
    }

    private static void AppendSpikeGate(StringBuilder sb, SpikeGateSettings s)
    {
        sb.Append("; spike=").Append(s.Enabled ? "on" : "off");
        sb.Append("; spikeRadial=").Append(s.RadialZonesEnabled ? "on" : "off");
        sb.Append("; spikeCenterEnd=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.CenterZoneEnd);
        sb.Append("; spikeCenterDelta=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.CenterDeltaThreshold);
        sb.Append("; spikeCenterVel=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.CenterVelocityThresholdPerSecond);
        sb.Append("; spikeCenterHold=").Append(s.CenterMaxConsecutiveSuppressions);
        sb.Append("; spikeCenterMul=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.CenterSmoothingMultiplier);
        sb.Append("; spikeOuterDelta=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.OuterDeltaThreshold);
        sb.Append("; spikeOuterVel=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.OuterVelocityThresholdPerSecond);
        sb.Append("; spikeOuterHold=").Append(s.OuterMaxConsecutiveSuppressions);
        sb.Append("; spikeDelta=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.DeltaThreshold);
        sb.Append("; spikeVel=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.VelocityThresholdPerSecond);
        sb.Append("; spikeMaxHold=").Append(s.MaxConsecutiveSuppressions);
        sb.Append("; spikeBlend=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.ZoneBlendWidth);
    }

    private static void AppendZGuard(StringBuilder sb, ZImpulseGuardSettings s)
    {
        sb.Append("; zGuard=").Append(s.Enabled ? "on" : "off");
        sb.Append("; zGuardR=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.CenterRadius);
        sb.Append("; zGuardT=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.IntentThreshold);
        sb.Append("; zGuardN=").Append(s.ConfirmSamples);
    }

    private static void AppendAxisIntent(StringBuilder sb, AxisIntentSettings s)
    {
        sb.Append("; axisIntent=").Append(s.Enabled ? "on" : "off");
        sb.Append("; axisIntentD=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.DeflectionThreshold);
        sb.Append("; axisIntentRel=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.ReleaseRatio);
        sb.Append("; axisIntentS=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.StrongDeflectionThreshold);
        sb.Append("; axisIntentN=").Append(s.ConfirmSamples);
        sb.Append("; axisIntentNxy=").Append(s.ConfirmSamplesWhileOthersActive);
        sb.Append("; axisIntentNoInstantXy=").Append(s.DisableInstantIntentWhileOthersActive ? "on" : "off");
    }

    private static void AppendCrossShield(StringBuilder sb, CrossAxisShieldSettings s)
    {
        sb.Append("; crossShield=").Append(s.Enabled ? "on" : "off");
        sb.Append("; crossTarget=").Append(s.TargetAxisId);
        sb.Append("; crossWatch=").Append(string.Join(",", s.WatchedAxes ?? []));
        sb.Append("; crossDef=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.OtherAxisDeflectionThreshold);
        sb.Append("; crossMinSamples=").Append(s.MinOtherAxisActiveSamples);
        sb.Append("; crossRel=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.ReleaseThresholdRatio);
        sb.Append("; crossEngage=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.BlockEngageRatio);
        sb.Append("; crossFullMul=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.FullBlockDeflectionMultiplier);
        sb.Append("; crossLockSmooth=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.LockSmoothingSeconds);
        sb.Append("; crossReleaseSmooth=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.ReleaseSmoothingSeconds);
        sb.Append("; crossRespectIntent=").Append(s.RespectTargetIntent ? "on" : "off");
        sb.Append("; crossDominance=").Append(s.RequireOtherAxisDominance ? "on" : "off");
        sb.Append("; crossDominanceRatio=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.OtherAxisDominanceRatio);
        sb.Append("; crossParasiticLeak=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.ParasiticClampLeakMultiplier);
        sb.Append("; crossVmin=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.MinOtherAxisVelocityPerSecond);
        sb.Append("; crossVmax=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.MaxOtherAxisVelocityPerSecond);
        sb.Append("; crossRateMul=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.RateLimitMultiplierWhenActive);
        sb.Append("; crossEmaMul=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.EmaAlphaMultiplierWhenActive);
        sb.Append("; crossLock=").Append(s.HardLockWhenActive ? "on" : "off");
        sb.Append("; crossLockCenter=").Append(s.HardLockForceCenter ? "on" : "off");
        sb.Append("; crossLeak=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.LockLeakMultiplier);
    }

    private static void AppendAxisBindLock(StringBuilder sb, AxisBindLockSettings s)
    {
        sb.Append("; axisBindLock=").Append(s.Enabled ? "on" : "off");
        sb.Append("; axisBindKind=").Append(string.IsNullOrWhiteSpace(s.BindDeviceKind) ? "-" : s.BindDeviceKind);
        sb.Append("; axisBindDevice=").Append(string.IsNullOrWhiteSpace(s.BindDeviceId) ? "-" : s.BindDeviceId);
        sb.Append("; axisBindBtn=").Append(s.ButtonIndex);
        sb.Append("; axisBindKey=").Append(s.KeyCode);
        sb.Append("; axisBindAnchor=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.LockAnchor);
    }

    private static void AppendRateLimiter(StringBuilder sb, RateLimiterSettings s)
    {
        sb.Append("; rateLimiter=").Append(s.Enabled ? "on" : "off");
        sb.Append("; rateMax=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.00}", s.MaxDeltaPerSecond);
    }

    private static void AppendEma(StringBuilder sb, EmaSettings s)
    {
        sb.Append("; ema=").Append(s.Enabled ? "on" : "off");
        sb.Append("; emaAlpha=").AppendFormat(CultureInfo.InvariantCulture, "{0:0.000}", s.Alpha);
    }
}
