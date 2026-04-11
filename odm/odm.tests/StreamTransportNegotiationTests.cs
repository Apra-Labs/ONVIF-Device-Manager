using System;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests verifying that NvtSessionFactory.CreateChannelFactory
    /// selects the correct WCF transport binding element for HTTPS vs HTTP sessions.
    ///
    /// Key assertions (TASK-8):
    ///   - HTTPS session => SslStreamTransportBindingElement (NOT HttpsTransportBindingElement)
    ///   - HTTP  session => HttpTransportBindingElement
    ///   - MessageVersion is Soap12 (AddressingNone) for both plain and HTTPS
    ///
    /// Reflection pattern mirrors HttpsBindingTests.cs.
    /// </summary>
    [TestClass]
    public class StreamTransportNegotiationTests
    {
        private static MethodInfo GetCreateChannelFactoryMethod()
        {
            var factoryType = typeof(NvtSessionFactory);
            var method = factoryType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name == "CreateChannelFactory" && m.GetParameters().Length == 4)
                .FirstOrDefault();
            Assert.IsNotNull(method, "CreateChannelFactory(4 params) not found via reflection");
            return method;
        }

        private static CustomBinding GetBinding(bool useTls)
        {
            var method = GetCreateChannelFactoryMethod();
            var generic = method.MakeGenericMethod(typeof(onvif10_device.Device));
            var factory = (ChannelFactory)generic.Invoke(null,
                new object[] { false, false, false, useTls });
            Assert.IsNotNull(factory, "ChannelFactory must not be null");
            var binding = factory.Endpoint.Binding as CustomBinding;
            Assert.IsNotNull(binding, "Binding must be a CustomBinding");
            return binding;
        }

        /// <summary>
        /// HTTPS session must use SslStreamTransportBindingElement.
        /// (TASK-6: replaces HttpsTransportBindingElement to send a single TLS record.)
        /// </summary>
        [TestMethod]
        public void HttpsSession_UsesCorrectTransport_SslStreamBindingElement()
        {
            var binding = GetBinding(useTls: true);

            var sslElement = binding.Elements
                .OfType<SslStreamTransportBindingElement>()
                .FirstOrDefault();

            Assert.IsNotNull(sslElement,
                "HTTPS binding must contain SslStreamTransportBindingElement");
        }

        /// <summary>
        /// HTTPS session must NOT contain the legacy HttpsTransportBindingElement.
        /// </summary>
        [TestMethod]
        public void HttpsSession_DoesNotUseHttpsTransportBindingElement()
        {
            var binding = GetBinding(useTls: true);

            var httpsElement = binding.Elements
                .OfType<HttpsTransportBindingElement>()
                .FirstOrDefault();

            Assert.IsNull(httpsElement,
                "HTTPS binding must NOT contain HttpsTransportBindingElement — it was replaced by SslStreamTransportBindingElement");
        }

        /// <summary>
        /// HTTP session must use HttpTransportBindingElement (not Https).
        /// </summary>
        [TestMethod]
        public void HttpSession_UsesHttpTransportBindingElement()
        {
            var binding = GetBinding(useTls: false);

            var httpElement = binding.Elements
                .OfType<HttpTransportBindingElement>()
                .FirstOrDefault();

            Assert.IsNotNull(httpElement,
                "HTTP binding must contain HttpTransportBindingElement");

            var httpsElement = binding.Elements
                .OfType<HttpsTransportBindingElement>()
                .FirstOrDefault();

            Assert.IsNull(httpsElement,
                "HTTP binding must NOT contain HttpsTransportBindingElement");
        }

        /// <summary>
        /// The HTTPS binding's message encoder must use Soap12 (AddressingNone).
        /// Soap12WSAddressing10 would cause cameras to fail with MustUnderstand faults
        /// on the wsa:Action header.
        /// </summary>
        [TestMethod]
        public void HttpsSession_MessageVersion_IsSoap12AddressingNone()
        {
            var binding = GetBinding(useTls: true);

            var encodingElement = binding.Elements
                .OfType<MessageEncodingBindingElement>()
                .FirstOrDefault();

            Assert.IsNotNull(encodingElement,
                "Binding must contain a MessageEncodingBindingElement");

            var encoderFactory = encodingElement.CreateMessageEncoderFactory();
            Assert.AreEqual(MessageVersion.Soap12, encoderFactory.MessageVersion,
                "Message version must be Soap12 (AddressingNone) — not Soap12WSAddressing10");
        }
    }
}
