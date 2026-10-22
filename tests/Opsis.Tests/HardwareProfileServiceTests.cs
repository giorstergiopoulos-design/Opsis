using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// Φάση 1, αποδοχή 1α ("unit tests για τα όρια RAM/cores/μπαταρίας· χειροκίνητο override δουλεύει·
// καμία αλλαγή συμπεριφοράς σε μηχάνημα Balanced"). Τρέχει πάνω στο πραγματικό AppSettings.Load()/
// Save() (δεν υπάρχει εύκολο seam να το απομονώσει κανείς χωρίς μεγαλύτερο refactor) — κάθε test
// επαναφέρει ρητά το override στην αρχική του τιμή στο τέλος, ώστε η σειρά εκτέλεσης να μην έχει
// σημασία και να μην αφήνει ίχνος στις πραγματικές ρυθμίσεις της συνεδρίας.
[Collection("AppSettingsSerial")] // AppSettings.Load/Save δεν είναι thread-safe για ταυτόχρονα tests
public sealed class HardwareProfileServiceTests
{
    [Fact]
    public void Override_RoundTrips_ToLow()
    {
        var before = HardwareProfileService.GetOverride();
        try
        {
            HardwareProfileService.SetOverride(HardwareTier.Low);
            Assert.Equal(HardwareTier.Low, HardwareProfileService.CurrentTier);
            Assert.Equal(HardwareTier.Low, HardwareProfileService.GetOverride());
        }
        finally { HardwareProfileService.SetOverride(before); }
    }

    [Fact]
    public void Override_RoundTrips_ToHigh()
    {
        var before = HardwareProfileService.GetOverride();
        try
        {
            HardwareProfileService.SetOverride(HardwareTier.High);
            Assert.Equal(HardwareTier.High, HardwareProfileService.CurrentTier);
        }
        finally { HardwareProfileService.SetOverride(before); }
    }

    [Fact]
    public void ClearingOverride_FallsBackToAutoDetect_ValidTier()
    {
        var before = HardwareProfileService.GetOverride();
        try
        {
            HardwareProfileService.SetOverride(HardwareTier.Low);
            HardwareProfileService.SetOverride(null);
            Assert.Null(HardwareProfileService.GetOverride());
            // Auto-detect πρέπει να γυρίσει μία από τις 3 έγκυρες τιμές, ό,τι κι αν είναι το πραγματικό υλικό.
            Assert.True(HardwareProfileService.CurrentTier is HardwareTier.Low or HardwareTier.Balanced or HardwareTier.High);
        }
        finally { HardwareProfileService.SetOverride(before); }
    }

    [Theory]
    [InlineData(HardwareTier.Low, "Low Power")]
    [InlineData(HardwareTier.High, "High")]
    public void PerformanceModeManager_CombinesHardwareTier_OnACPower(HardwareTier forced, string expectedMode)
    {
        // Μόνο έγκυρο όταν δεν τρέχουμε σε μπαταρία/χαμηλή μπαταρία (αλλιώς αυτά τα δύο υπερισχύουν
        // ήδη πριν καν φτάσει στο HardwareProfileService) — σε CI/desktop πάνω σε ρεύμα, ισχύει.
        if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline) return;

        var before = HardwareProfileService.GetOverride();
        try
        {
            HardwareProfileService.SetOverride(forced);
            Assert.Equal(expectedMode, PerformanceModeManager.GetRecommendedMode());
        }
        finally { HardwareProfileService.SetOverride(before); }
    }
}
