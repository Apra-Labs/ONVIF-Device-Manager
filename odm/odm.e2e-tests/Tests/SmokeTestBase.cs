using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.e2e_tests.Config;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    [TestClass]
    public class SmokeTestBase
    {
        protected static SmokeConfig Config { get; private set; }
        protected static List<DiscoveredCamera> DiscoveredCameras { get; private set; }

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext ctx)
        {
            Config = SmokeConfig.Load();

            // Discover and classify cameras before any tests run
            var discoveryTimeout = TimeSpan.FromSeconds(Config.Timeouts.DiscoverySeconds);
            DiscoveredCameras = CameraDispatcher.DiscoverAndClassify(Config, discoveryTimeout);

            ctx.WriteLine($"Discovery complete: {DiscoveredCameras.Count} camera(s) found.");
            foreach (var cam in DiscoveredCameras)
                ctx.WriteLine($"  {cam.CameraType}: {cam.Manufacturer} {cam.Model} @ {cam.IpAddress}");
        }
    }
}
