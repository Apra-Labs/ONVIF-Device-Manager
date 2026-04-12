using odm.e2e_tests.Config;

namespace odm.e2e_tests.Helpers
{
    /// <summary>
    /// A camera found via ONVIF WS-Discovery, enriched with device info and matched config profile.
    /// </summary>
    public class DiscoveredCamera
    {
        /// <summary>ONVIF service endpoint URL (e.g. http://192.168.1.190/onvif/device_service)</summary>
        public string ServiceEndpoint { get; set; }

        /// <summary>IP address of the camera</summary>
        public string IpAddress { get; set; }

        /// <summary>Manufacturer string from GetDeviceInformation</summary>
        public string Manufacturer { get; set; }

        /// <summary>Model string from GetDeviceInformation</summary>
        public string Model { get; set; }

        /// <summary>Firmware version from GetDeviceInformation</summary>
        public string FirmwareVersion { get; set; }

        /// <summary>Matched config profile, or null if no profile matches (classifies as unknown)</summary>
        public CameraProfile Profile { get; set; }

        /// <summary>Convenience: returns Profile.ClassifyAs or "unknown"</summary>
        public string CameraType => Profile?.ClassifyAs ?? "unknown";
    }
}
