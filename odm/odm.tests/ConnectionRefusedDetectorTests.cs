using System;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for NvtSessionFactory.IsConnectionRefused.
    ///
    /// Verifies that the detector:
    ///   - Returns true for WebException(ConnectFailure)
    ///   - Returns true for SocketException(ConnectionRefused) wrapped in AggregateException
    ///   - Returns true for CommunicationException wrapping SocketException(ConnectionReset)
    ///   - Returns false for FaultException (a CommunicationException subclass)
    ///   - Returns false for arbitrary exceptions
    /// </summary>
    [TestClass]
    public class ConnectionRefusedDetectorTests
    {
        [TestMethod]
        public void IsConnectionRefused_WebExceptionConnectFailure_ReturnsTrue()
        {
            var ex = new WebException("Connection refused", WebExceptionStatus.ConnectFailure);
            Assert.IsTrue(NvtSessionFactory.IsConnectionRefused(ex),
                "WebException with ConnectFailure status must be detected as connection-refused");
        }

        [TestMethod]
        public void IsConnectionRefused_SocketExceptionConnectionRefused_WrappedInAggregate_ReturnsTrue()
        {
            var socketEx = new SocketException((int)SocketError.ConnectionRefused);
            var aggregate = new AggregateException("aggregate", socketEx);
            Assert.IsTrue(NvtSessionFactory.IsConnectionRefused(aggregate),
                "SocketException(ConnectionRefused) nested inside AggregateException must be detected");
        }

        [TestMethod]
        public void IsConnectionRefused_SocketExceptionConnectionReset_WrappedInAggregate_ReturnsTrue()
        {
            var socketEx = new SocketException((int)SocketError.ConnectionReset);
            var aggregate = new AggregateException("aggregate", socketEx);
            Assert.IsTrue(NvtSessionFactory.IsConnectionRefused(aggregate),
                "SocketException(ConnectionReset) nested inside AggregateException must be detected");
        }

        [TestMethod]
        public void IsConnectionRefused_CommunicationException_WrappingSocketException_ReturnsTrue()
        {
            var socketEx = new SocketException((int)SocketError.ConnectionRefused);
            var commEx = new CommunicationException("comm", socketEx);
            Assert.IsTrue(NvtSessionFactory.IsConnectionRefused(commEx),
                "CommunicationException wrapping SocketException(ConnectionRefused) must be detected");
        }

        [TestMethod]
        public void IsConnectionRefused_FaultException_ReturnsFalse()
        {
            var fault = new FaultException("action not supported");
            Assert.IsFalse(NvtSessionFactory.IsConnectionRefused(fault),
                "FaultException must NOT be treated as connection-refused");
        }

        [TestMethod]
        public void IsConnectionRefused_RandomException_ReturnsFalse()
        {
            var ex = new Exception("something went wrong");
            Assert.IsFalse(NvtSessionFactory.IsConnectionRefused(ex),
                "Generic exception must NOT be treated as connection-refused");
        }

        [TestMethod]
        public void IsConnectionRefused_WebExceptionTimeout_ReturnsFalse()
        {
            var ex = new WebException("timeout", WebExceptionStatus.Timeout);
            Assert.IsFalse(NvtSessionFactory.IsConnectionRefused(ex),
                "WebException with Timeout status must NOT be treated as connection-refused");
        }
    }
}
