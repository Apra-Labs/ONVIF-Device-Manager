using System;
using System.Net;
using System.ServiceModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for the withMedia2HttpFallback / withMedia1HttpFallback
    /// combinator behaviour.
    ///
    /// The F# combinators are private closures inside CreateSession(deviceUri), so
    /// they cannot be called directly from C#.  These tests exercise an equivalent
    /// C# replica of the same pattern (gated on NvtSessionFactory.IsConnectionRefused)
    /// to verify the three key invariants:
    ///
    ///   a) Connection-refused → fallback client is invoked and its result returned.
    ///   b) Non-connection-refused exception (e.g. FaultException) → propagates,
    ///      no fallback attempted.
    ///   c) Fallback xAddr is null → original exception propagates unchanged.
    /// </summary>
    [TestClass]
    public class MediaHttpFallbackTests
    {
        // ── C# replica of the combinator pattern ─────────────────────────────

        /// <summary>
        /// Mirrors the F# withMedia2HttpFallback / withMedia1HttpFallback logic:
        ///   try primary()
        ///   catch err when IsConnectionRefused(err):
        ///     if xAddr == null → rethrow
        ///     else             → return fallback()
        /// </summary>
        private static T RunWithFallback<T>(
            Func<T> primary,
            Uri httpXAddr,
            Func<T> buildFallback)
        {
            try
            {
                return primary();
            }
            catch (Exception err) when (NvtSessionFactory.IsConnectionRefused(err))
            {
                if (httpXAddr == null)
                    throw;
                return buildFallback();
            }
        }

        // ── Test a: connection-refused → fallback invoked ────────────────────

        [TestMethod]
        public void WithFallback_PrimaryThrowsConnectFailure_FallbackIsInvoked()
        {
            bool fallbackCalled = false;
            var connRefused = new WebException("Connection refused", WebExceptionStatus.ConnectFailure);
            var fakeXAddr   = new Uri("http://192.168.1.190:80/onvif/media_service");

            int result = RunWithFallback(
                primary:       () => throw connRefused,
                httpXAddr:     fakeXAddr,
                buildFallback: () => { fallbackCalled = true; return 42; });

            Assert.IsTrue(fallbackCalled,
                "Fallback must be invoked when primary throws connection-refused");
            Assert.AreEqual(42, result,
                "Result must come from the fallback client");
        }

        // ── Test b: non-connection-refused exception → propagates ─────────────

        [TestMethod]
        [ExpectedException(typeof(FaultException))]
        public void WithFallback_PrimaryThrowsFaultException_PropagatesWithoutFallback()
        {
            bool fallbackCalled = false;
            var fault     = new FaultException("ActionNotSupported");
            var fakeXAddr = new Uri("http://192.168.1.190:80/onvif/media_service");

            // Must throw FaultException; fallback must never be reached.
            RunWithFallback(
                primary:       () => throw fault,
                httpXAddr:     fakeXAddr,
                buildFallback: () => { fallbackCalled = true; return 0; });

            // Unreachable — [ExpectedException] verifies the throw.
            Assert.IsFalse(fallbackCalled, "Fallback must NOT be invoked for FaultException");
        }

        // ── Test c: fallback xAddr null → original exception propagates ───────

        [TestMethod]
        [ExpectedException(typeof(WebException))]
        public void WithFallback_FallbackXAddrIsNull_OriginalExceptionPropagates()
        {
            var connRefused = new WebException("Connection refused", WebExceptionStatus.ConnectFailure);

            // httpXAddr is null — combinator must rethrow the original WebException.
            RunWithFallback(
                primary:       () => throw connRefused,
                httpXAddr:     null,
                buildFallback: () => 0);
        }
    }
}
