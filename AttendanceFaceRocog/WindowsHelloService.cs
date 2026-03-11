using System.Threading.Tasks;
using Windows.Security.Credentials.UI;

namespace AttendanceFaceRocog
{
    internal static class WindowsHelloService
    {
        public static async Task<(bool Success, string Message)> VerifyAsync()
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();

            switch (availability)
            {
                case UserConsentVerifierAvailability.Available:
                    break;

                case UserConsentVerifierAvailability.DeviceNotPresent:
                    return (false, "Windows Hello device not present.");

                case UserConsentVerifierAvailability.NotConfiguredForUser:
                    return (false, "Windows Hello is not configured for this Windows user.");

                case UserConsentVerifierAvailability.DisabledByPolicy:
                    return (false, "Windows Hello is disabled by policy.");

                case UserConsentVerifierAvailability.DeviceBusy:
                    return (false, "Windows Hello device is busy.");

                default:
                    return (false, $"Windows Hello unavailable: {availability}");
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(
                "Verify your identity to record attendance.");

            return result switch
            {
                UserConsentVerificationResult.Verified => (true, "Windows Hello verified."),
                UserConsentVerificationResult.DeviceBusy => (false, "Windows Hello device is busy."),
                UserConsentVerificationResult.Canceled => (false, "Windows Hello verification canceled."),
                UserConsentVerificationResult.RetriesExhausted => (false, "Windows Hello retries exhausted."),
                UserConsentVerificationResult.DisabledByPolicy => (false, "Windows Hello disabled by policy."),
                UserConsentVerificationResult.DeviceNotPresent => (false, "Windows Hello device not present."),
                UserConsentVerificationResult.NotConfiguredForUser => (false, "Windows Hello is not configured."),
                _ => (false, $"Windows Hello failed: {result}")
            };
        }
    }
}