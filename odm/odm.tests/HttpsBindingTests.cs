using System;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    [TestClass]
    public class HttpsBindingTests
    {
        /// <summary>
        /// Verifies that CreateChannelFactory with useTls=true produces a binding
        /// containing HttpsTransportBindingElement.
        /// </summary>
        [TestMethod]
        public void UnsecureFactory_WithTls_CreatesHttpsBinding()
        {
            // CreateChannelFactory<Device>(mtomEncoding, wsAddressing, securityToken, useTls)
            // is private static, so invoke via reflection.
            var factoryType = typeof(NvtSessionFactory);
            var method = factoryType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name == "CreateChannelFactory" && m.GetParameters().Length == 4)
                .FirstOrDefault();

            Assert.IsNotNull(method, "CreateChannelFactory method not found via reflection");

            // Make the generic method for onvif10_device.Device
            var genericMethod = method.MakeGenericMethod(typeof(onvif10_device.Device));

            // Call with useTls = true
            var factory = genericMethod.Invoke(null, new object[] { false, false, false, true });
            Assert.IsNotNull(factory, "Factory should not be null");

            var channelFactory = (ChannelFactory)factory;
            var binding = channelFactory.Endpoint.Binding as CustomBinding;
            Assert.IsNotNull(binding, "Binding should be a CustomBinding");

            var httpsElement = binding.Elements
                .OfType<HttpsTransportBindingElement>()
                .FirstOrDefault();
            Assert.IsNotNull(httpsElement, "Binding must contain HttpsTransportBindingElement when useTls=true");
            Assert.IsFalse(httpsElement.RequireClientCertificate, "RequireClientCertificate should be false");
        }

        /// <summary>
        /// Verifies that CreateChannelFactory with useTls=false produces a plain
        /// HttpTransportBindingElement (NOT Https).
        /// </summary>
        [TestMethod]
        public void UnsecureFactory_WithoutTls_CreatesHttpBinding()
        {
            var factoryType = typeof(NvtSessionFactory);
            var method = factoryType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name == "CreateChannelFactory" && m.GetParameters().Length == 4)
                .FirstOrDefault();

            Assert.IsNotNull(method, "CreateChannelFactory method not found via reflection");

            var genericMethod = method.MakeGenericMethod(typeof(onvif10_device.Device));
            var factory = genericMethod.Invoke(null, new object[] { false, false, false, false });
            var channelFactory = (ChannelFactory)factory;
            var binding = channelFactory.Endpoint.Binding as CustomBinding;
            Assert.IsNotNull(binding, "Binding should be a CustomBinding");

            var httpElement = binding.Elements
                .OfType<HttpTransportBindingElement>()
                .FirstOrDefault();
            Assert.IsNotNull(httpElement, "Binding must contain HttpTransportBindingElement when useTls=false");

            var httpsElement = binding.Elements
                .OfType<HttpsTransportBindingElement>()
                .FirstOrDefault();
            Assert.IsNull(httpsElement, "Binding must NOT contain HttpsTransportBindingElement when useTls=false");
        }
    }
}
