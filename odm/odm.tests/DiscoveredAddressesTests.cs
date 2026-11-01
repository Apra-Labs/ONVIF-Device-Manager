using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Announced addresses with port 0 are dropped, so they can't replace a camera's working address.
    /// </summary>
    [TestClass]
    public class DiscoveredAddressesTests
    {
        static readonly Uri Good = new Uri("http://192.168.1.51:10080/onvif/device_service");
        static readonly Uri NoPort = new Uri("http://192.168.1.51/onvif/device_service");
        static readonly Uri PortZero = new Uri("http://192.168.1.51:0/");

        [TestMethod]
        public void Usable_KeepsExplicitAndDefaultPorts()
        {
            CollectionAssert.AreEqual(new[] { Good, NoPort }, DiscoveredAddresses.Usable(new[] { Good, NoPort }));
        }

        [TestMethod]
        public void Usable_DropsPortZero()
        {
            Assert.AreEqual(0, DiscoveredAddresses.Usable(new[] { PortZero }).Length);
        }

        [TestMethod]
        public void Usable_KeepsTheGoodAddressBesideAPortZeroOne()
        {
            CollectionAssert.AreEqual(new[] { Good }, DiscoveredAddresses.Usable(new[] { PortZero, Good }));
        }
    }
}
